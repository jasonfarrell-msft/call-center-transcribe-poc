using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace CallCenterTranscription.Api.Services;

/// <summary>
/// BackgroundService that reads PCM audio frames from <see cref="IAudioSource"/> and feeds them
/// to Azure AI Speech for continuous recognition. Recognized utterances are pushed to the rep
/// dashboard over the existing PipelineHub SignalR path as <see cref="TranscriptEvent"/> objects —
/// the exact same DTO + method name the UI already consumes ("stream.transcript").
///
/// Lifecycle (per-call):
///   The service runs an OUTER loop for the lifetime of the host. Each iteration processes ONE
///   call: it waits (idle) for a call's audio session via <see cref="IAudioSource.ReadAsync"/>,
///   builds a FRESH SpeechRecognizer + push stream when the first frame arrives, streams the call,
///   then tears the recognizer down when the call ends and loops back to wait for the next call.
///   This replaces the earlier single-shot design that shut the pipeline down after one call.
///
/// Auth: DefaultAzureCredential → AAD token formatted as "aad#{resourceId}#{token}".
///       NO subscription key. 'Cognitive Services User' role required on the Speech resource.
///       A fresh token is acquired at the start of each call (POC calls are well under the
///       ~1-hour AAD token lifetime, so no mid-call refresh is needed).
///
/// Audio: PCM 16-bit mono 16,000 Hz — matches AudioFrame contract exactly.
///
/// Graceful degradation: if Speech:Endpoint / Speech:Region is missing, the service logs a
///       warning and returns without crashing the host. Per-call errors are logged and the loop
///       continues waiting for the next call.
/// </summary>
public sealed class SpeechTranscriptionService : BackgroundService
{
    private readonly IAudioSource _audioSource;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly ActiveCallStore _callStore;
    private readonly IConfiguration _config;
    private readonly ILogger<SpeechTranscriptionService> _logger;

    public SpeechTranscriptionService(
        IAudioSource audioSource,
        IHubContext<PipelineHub> hub,
        ActiveCallStore callStore,
        IConfiguration config,
        ILogger<SpeechTranscriptionService> logger)
    {
        _audioSource = audioSource;
        _hub = hub;
        _callStore = callStore;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ── Guard: require Speech endpoint + region ──────────────────────────────────────────
        var speechEndpoint   = _config["Speech:Endpoint"];
        var speechRegion     = _config["Speech:Region"];
        var speechResourceId = _config["Speech:ResourceId"];

        if (string.IsNullOrWhiteSpace(speechEndpoint) || string.IsNullOrWhiteSpace(speechRegion))
        {
            _logger.LogWarning(
                "SpeechTranscriptionService: Speech:Endpoint and/or Speech:Region are not " +
                "configured. Live transcription is disabled. " +
                "Set Speech__Endpoint + Speech__Region (+ optionally Speech__ResourceId) " +
                "to enable. Scripted feed and SignalR UI remain intact.");
            return;
        }

        var credential = new DefaultAzureCredential();
        var tokenScope = new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]);
        var language   = (_config["Speech:CandidateLanguages"] ?? "en-US").Split(',')[0].Trim();

        _logger.LogInformation(
            "SpeechTranscriptionService: ready (language={Language}); waiting for call audio.",
            language);

        // ── Outer loop: one recognizer per call; idle between calls ─────────────────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCallSessionAsync(
                    credential, tokenScope, speechEndpoint!, speechResourceId, language, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SpeechTranscriptionService: call session failed; pipeline stays alive and " +
                    "will wait for the next call.");
            }
        }

        _logger.LogInformation("SpeechTranscriptionService: host stopping; transcription loop ended.");
    }

    /// <summary>
    /// Processes a single call: waits for its audio session, builds a recognizer on the first
    /// frame, streams audio until the call ends, then tears the recognizer down.
    /// </summary>
    private async Task RunCallSessionAsync(
        DefaultAzureCredential credential,
        TokenRequestContext tokenScope,
        string speechEndpoint,
        string? speechResourceId,
        string language,
        CancellationToken stoppingToken)
    {
        SpeechRecognizer? recognizer = null;
        AudioConfig? audioConfig = null;
        PushAudioInputStream? pushStream = null;
        long sequence = 0;
        long framesWritten = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var frame in _audioSource.ReadAsync(stoppingToken).ConfigureAwait(false))
            {
                // ── Lazily build the recognizer when the first frame of THIS call arrives ──────
                if (recognizer is null)
                {
                    var accessToken = await credential.GetTokenAsync(tokenScope, stoppingToken)
                                                      .ConfigureAwait(false);

                    var speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint));
                    speechConfig.AuthorizationToken = BuildAuthToken(speechResourceId, accessToken.Token);
                    speechConfig.SpeechRecognitionLanguage = language;

                    var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
                    pushStream  = AudioInputStream.CreatePushStream(audioFormat);
                    audioConfig = AudioConfig.FromStreamInput(pushStream);
                    recognizer  = new SpeechRecognizer(speechConfig, audioConfig);

                    WireRecognitionHandlers(recognizer, () => Interlocked.Increment(ref sequence));

                    await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
                    _logger.LogInformation(
                        "SpeechTranscriptionService: recognizer started for call {CallId}.",
                        _callStore.CallId ?? "(unknown)");
                }

                if (frame.Payload.Length > 0)
                {
                    pushStream!.Write(frame.Payload);
                    framesWritten++;
                }
            }
        }
        finally
        {
            if (recognizer is not null)
            {
                // Signal EOS to the Speech SDK and stop recognition cleanly.
                pushStream!.Close();
                await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                _logger.LogInformation(
                    "SpeechTranscriptionService: recognizer stopped — {Frames} audio frames over " +
                    "{Seconds:F1}s for call {CallId}.",
                    framesWritten, stopwatch.Elapsed.TotalSeconds, _callStore.CallId ?? "(ended)");

                recognizer.Dispose();
                audioConfig?.Dispose();
                pushStream.Dispose();
            }
        }
    }

    // ── Recognition event wiring ────────────────────────────────────────────────────────────────

    private void WireRecognitionHandlers(SpeechRecognizer recognizer, Func<long> nextSequence)
    {
        var firstPartialLogged = false;

        // ── Recognizing → interim TranscriptEvent (isFinal=false) ──────────────────────────
        recognizer.Recognizing += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Result.Text)) return;

            if (!firstPartialLogged)
            {
                firstPartialLogged = true;
                _logger.LogInformation(
                    "SpeechTranscriptionService: first partial recognized — audio is reaching Speech.");
            }

            if (ResolveGroup() is not { } recognizing) return;
            var (callId, group) = recognizing;
            var seq = nextSequence();

            var evt = BuildEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: false);
            _ = _hub.Clients.Group(group)
                    .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);
        };

        // ── Recognized → final TranscriptEvent (isFinal=true) ──────────────────────────────
        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech)
            {
                if (e.Result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogInformation(
                        "SpeechTranscriptionService: NoMatch — audio received but no speech recognized " +
                        "(silence/noise/wrong language?).");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Result.Text)) return;

            if (ResolveGroup() is not { } recognized) return;
            var (callId, group) = recognized;
            var seq = nextSequence();

            var evt = BuildEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: true);
            _ = _hub.Clients.Group(group)
                    .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);

            _logger.LogInformation(
                "SpeechTranscriptionService: FINAL utterance seq={Seq} callId={CallId} text=\"{Text}\"",
                seq, callId, e.Result.Text);
        };

        // ── Canceled → always log (the cardinal-sin signal for auth/network failures) ──────
        recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: recognition canceled with ERROR — " +
                    "ErrorCode={Code} Details={Details}",
                    e.ErrorCode, e.ErrorDetails);
            }
            else
            {
                _logger.LogInformation(
                    "SpeechTranscriptionService: recognition canceled — Reason={Reason}.",
                    e.Reason);
            }
        };

        recognizer.SessionStopped += (_, _) =>
            _logger.LogInformation("SpeechTranscriptionService: Speech recognition session stopped.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string BuildAuthToken(string? resourceId, string aadToken) =>
        string.IsNullOrWhiteSpace(resourceId)
            ? aadToken
            : $"aad#{resourceId}#{aadToken}";

    private (string callId, string group)? ResolveGroup()
    {
        // Live mode: only publish when a real call is active, so the publish group is always
        // identical to the callId broadcast in CallStarted (reviewer fix — no "live-call" fallback
        // that the browser never subscribed to).
        var callId = _callStore.CallId;
        if (string.IsNullOrEmpty(callId))
        {
            return null;
        }

        return (callId, PipelineContract.GroupNames.ForCall(callId));
    }

    private static TranscriptEvent BuildEvent(
        string callId,
        long seq,
        string resultId,
        string text,
        bool isFinal) =>
        new()
        {
            CallId             = callId,
            EventId            = $"evt-transcript-live-{seq}",
            Sequence           = seq,
            UtteranceId        = resultId,
            TimestampUtc       = DateTimeOffset.UtcNow,
            IsFinal            = isFinal,
            SpeakerId          = "unknown",
            SpeakerDisplayLabel = "Speaker",
            SpeakerRole        = "unknown",
            SpeakerLabelSource = "speech-sdk",
            Text               = text,
            Source             = "azure-ai-speech"
        };
}
