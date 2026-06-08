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
/// BackgroundService that reads PCM audio frames from <see cref="IAudioSource"/> and feeds
/// them to Azure AI Speech for continuous recognition. Recognized utterances are pushed to
/// the rep dashboard over the existing PipelineHub SignalR path as
/// <see cref="TranscriptEvent"/> objects — the exact same DTO + method name the UI already
/// consumes ("stream.transcript").
///
/// Auth: DefaultAzureCredential → AAD token formatted as "aad#{resourceId}#{token}".
///       NO subscription key. Cognitive Services User role required on the Speech resource.
///       Token is refreshed every 9 minutes (well within the 1-hour AAD token lifetime).
///
/// Audio: PCM 16-bit mono 16,000 Hz — matches AudioFrame contract exactly.
///        Frames are written to a PushAudioInputStream; end-of-stream is signalled when
///        the IAsyncEnumerable completes.
///
/// Coexistence: does not touch the scripted propane REST endpoints. The scripted feed
///              continues to function regardless of audio source mode.
///
/// Graceful degradation: if Speech:Endpoint / Speech:Region is missing, or if AAD auth
///                       fails (e.g., running locally without a managed identity), the service
///                       logs a warning and returns without crashing the host.
/// </summary>
public sealed class SpeechTranscriptionService : BackgroundService
{
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(9);

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

        // ── Acquire AAD token via DefaultAzureCredential (managed identity on ACA) ──────────
        var credential  = new DefaultAzureCredential();
        var tokenScope  = new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]);

        AccessToken accessToken;
        try
        {
            accessToken = await credential.GetTokenAsync(tokenScope, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SpeechTranscriptionService: Failed to acquire AAD token for Azure AI Speech. " +
                "Ensure the ACA system-assigned managed identity has the " +
                "'Cognitive Services User' role on the Speech resource. " +
                "Live transcription is disabled; scripted feed remains intact.");
            return;
        }

        // ── Build SpeechConfig using AAD token (aad#{resourceId}#{token}) — NO key ──────────
        // Custom-domain keyless auth: FromEndpoint then set AuthorizationToken.
        // Token format: "aad#<ARM resource ID>#<AAD token>"
        // If resourceId is not configured, fall back to the bare token (works when the
        // Speech resource does not use a custom subdomain — not recommended for prod).
        SpeechConfig speechConfig;
        try
        {
            speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint));
            speechConfig.AuthorizationToken = BuildAuthToken(speechResourceId, accessToken.Token);
            speechConfig.SpeechRecognitionLanguage =
                (_config["Speech:CandidateLanguages"] ?? "en-US").Split(',')[0].Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SpeechTranscriptionService: Failed to build SpeechConfig. " +
                "Live transcription disabled.");
            return;
        }

        // ── Audio: PCM 16-bit mono 16,000 Hz (matches AudioFrame.Encoding="pcm16") ──────────
        var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        using var pushStream = AudioInputStream.CreatePushStream(audioFormat);
        using var audioConfig = AudioConfig.FromStreamInput(pushStream);
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        long sequence = 0;

        // ── Recognizing → interim TranscriptEvent (isFinal=false) ──────────────────────────
        recognizer.Recognizing += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Result.Text)) return;

            if (ResolveGroup() is not { } recognizing) return;
            var (callId, group) = recognizing;
            var seq = Interlocked.Increment(ref sequence);

            var evt = BuildEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: false);
            _ = _hub.Clients.Group(group)
                    .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);
        };

        // ── Recognized → final TranscriptEvent (isFinal=true) ──────────────────────────────
        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech) return;
            if (string.IsNullOrWhiteSpace(e.Result.Text)) return;

            if (ResolveGroup() is not { } recognized) return;
            var (callId, group) = recognized;
            var seq = Interlocked.Increment(ref sequence);

            var evt = BuildEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: true);
            _ = _hub.Clients.Group(group)
                    .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);

            _logger.LogDebug(
                "SpeechTranscriptionService: FINAL utterance seq={Seq} text={Text}",
                seq, e.Result.Text);
        };

        recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: Recognition canceled — " +
                    "ErrorCode={Code} Details={Details}",
                    e.ErrorCode, e.ErrorDetails);
            }
        };

        recognizer.SessionStopped += (_, _) =>
            _logger.LogInformation("SpeechTranscriptionService: Speech recognition session stopped.");

        // ── Start continuous recognition ─────────────────────────────────────────────────────
        await recognizer.StartContinuousRecognitionAsync();
        _logger.LogInformation("SpeechTranscriptionService: Continuous recognition started.");

        // ── Token refresh loop: refresh every 9 minutes (AAD tokens expire in ~1 hour) ──────
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var refreshTask = RefreshTokenPeriodically(
            credential, tokenScope, speechResourceId, recognizer, linkedCts.Token);

        // ── Feed audio frames into the push stream ────────────────────────────────────────────
        try
        {
            await foreach (var frame in _audioSource.ReadAsync(stoppingToken).ConfigureAwait(false))
            {
                if (frame.Payload.Length == 0)
                {
                    _logger.LogDebug("SpeechTranscriptionService: Empty frame skipped.");
                    continue;
                }
                pushStream.Write(frame.Payload);
            }
            _logger.LogInformation("SpeechTranscriptionService: Audio source stream completed.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SpeechTranscriptionService: Stopping — cancellation requested.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpeechTranscriptionService: Unexpected error reading audio frames.");
        }
        finally
        {
            // Signal EOS to the Speech SDK; stops recognition cleanly.
            pushStream.Close();

            // Cancel the token refresh loop.
            await linkedCts.CancelAsync();

            await recognizer.StopContinuousRecognitionAsync();
            _logger.LogInformation("SpeechTranscriptionService: Shutdown complete.");

            try { await refreshTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
    }

    // ── Token refresh (long-running call guard) ───────────────────────────────────────────────

    private static async Task RefreshTokenPeriodically(
        DefaultAzureCredential credential,
        TokenRequestContext scope,
        string? resourceId,
        SpeechRecognizer recognizer,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TokenRefreshInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var refreshed = await credential.GetTokenAsync(scope, ct).ConfigureAwait(false);
                recognizer.AuthorizationToken = BuildAuthToken(resourceId, refreshed.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Swallow; the existing token remains valid for ~1 hour. The calling method
                // logged the failure context — don't double-log here.
            }
        }
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
