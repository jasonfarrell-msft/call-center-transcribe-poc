using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;

namespace CallCenterTranscription.Api.Services;

public sealed class SpeechTranscriptionService : BackgroundService
{
    private readonly IAudioSource _audioSource;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly ActiveCallStore _callStore;
    private readonly LiveSentimentStore _liveSentiment;
    private readonly IReasoningClient _reasoningClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SpeechTranscriptionService> _logger;

    public SpeechTranscriptionService(
        IAudioSource audioSource,
        IHubContext<PipelineHub> hub,
        ActiveCallStore callStore,
        LiveSentimentStore liveSentiment,
        IReasoningClient reasoningClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<SpeechTranscriptionService> logger)
    {
        _audioSource = audioSource;
        _hub = hub;
        _callStore = callStore;
        _liveSentiment = liveSentiment;
        _reasoningClient = reasoningClient;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var speechEndpoint = _config["Speech:Endpoint"];
        var speechRegion = _config["Speech:Region"];
        var speechResourceId = _config["Speech:ResourceId"];
        var translatorEndpoint = _config["Translator:Endpoint"];
        var translatorRegion = _config["Translator:Region"];
        var translatorTargetLanguage = _config["Translator:TargetLanguage"] ?? "en";
        var candidateLanguages = ParseCandidateLanguages(_config["Speech:CandidateLanguages"]);

        if (string.IsNullOrWhiteSpace(speechEndpoint) || string.IsNullOrWhiteSpace(speechRegion))
        {
            _logger.LogWarning(
                "SpeechTranscriptionService: Speech:Endpoint and/or Speech:Region are not configured. " +
                "Live transcription is disabled and mock feed remains available.");
            return;
        }

        var credential = new DefaultAzureCredential();
        var tokenScope = new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]);

        _logger.LogInformation(
            "SpeechTranscriptionService: ready (languages={Languages}, translator={TranslatorState}); waiting for call audio.",
            string.Join(",", candidateLanguages),
            string.IsNullOrWhiteSpace(translatorEndpoint) ? "disabled" : "enabled");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCallSessionAsync(
                        credential,
                        tokenScope,
                        speechEndpoint!,
                        speechResourceId,
                        candidateLanguages,
                        translatorEndpoint,
                        translatorRegion,
                        translatorTargetLanguage,
                        stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SpeechTranscriptionService: call session failed; pipeline stays alive and will wait for the next call.");
            }
        }

        _logger.LogInformation("SpeechTranscriptionService: host stopping; transcription loop ended.");
    }

    private async Task RunCallSessionAsync(
        DefaultAzureCredential credential,
        TokenRequestContext tokenScope,
        string speechEndpoint,
        string? speechResourceId,
        IReadOnlyList<string> candidateLanguages,
        string? translatorEndpoint,
        string? translatorRegion,
        string translatorTargetLanguage,
        CancellationToken stoppingToken)
    {
        // ConversationTranscriber gives speaker-attributed results (SpeakerId per utterance)
        // on the same Mixed 16kHz mono push stream — no topology change required (R1 safe).
        ConversationTranscriber? transcriber = null;
        AudioConfig? audioConfig = null;
        PushAudioInputStream? pushStream = null;
        long sequence = 0;
        long framesWritten = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var frame in _audioSource.ReadAsync(stoppingToken).ConfigureAwait(false))
            {
                if (transcriber is null)
                {
                    var accessToken = await credential.GetTokenAsync(tokenScope, stoppingToken).ConfigureAwait(false);

                    var speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint));
                    speechConfig.AuthorizationToken = BuildAuthToken(speechResourceId, accessToken.Token);
                    speechConfig.SpeechRecognitionLanguage = candidateLanguages[0];

                    var autoDetectConfig = AutoDetectSourceLanguageConfig.FromLanguages(candidateLanguages.ToArray());
                    // Keep IDENTICAL format: PCM 16-bit, 16kHz, mono — same as AudioFrame contract.
                    var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
                    pushStream = AudioInputStream.CreatePushStream(audioFormat);
                    audioConfig = AudioConfig.FromStreamInput(pushStream);
                    transcriber = new ConversationTranscriber(speechConfig, autoDetectConfig, audioConfig);

                    WireTranscriberHandlers(
                        transcriber,
                        () => Interlocked.Increment(ref sequence),
                        candidateLanguages[0],
                        credential,
                        tokenScope,
                        translatorEndpoint,
                        translatorRegion,
                        translatorTargetLanguage);

                    await transcriber.StartTranscribingAsync().ConfigureAwait(false);
                    _logger.LogInformation(
                        "SpeechTranscriptionService: ConversationTranscriber started for call {CallId}.",
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
            if (transcriber is not null)
            {
                pushStream!.Close();
                await transcriber.StopTranscribingAsync().ConfigureAwait(false);
                _logger.LogInformation(
                    "SpeechTranscriptionService: ConversationTranscriber stopped — {Frames} audio frames over {Seconds:F1}s for call {CallId}.",
                    framesWritten,
                    stopwatch.Elapsed.TotalSeconds,
                    _callStore.CallId ?? "(ended)");

                transcriber.Dispose();
                audioConfig?.Dispose();
                pushStream.Dispose();
            }
        }
    }

    /// <summary>
    /// Wires all ConversationTranscriber events for one call session.
    ///
    /// Speaker-attribution — two-slot phase-aware heuristic (decision: lacus-speaker-label-fix):
    ///
    ///   ConversationTranscriber assigns opaque SpeakerIds ("Guest-1", "Guest-2", "Unknown") by
    ///   diarization cluster, NOT chronological order. The old single-slot "first speaker =
    ///   customer" heuristic was correct in theory (customer is on the stream before the rep
    ///   joins) but WRONG in practice: when the customer is silent on hold and the rep says
    ///   the first complete utterance after accepting, the rep's greeting fires the first
    ///   Transcribed event and was incorrectly latched as Customer.
    ///
    ///   Fix: two closure slots (customerSpeakerId / repSpeakerId) with RepAccepted as the
    ///   authoritative phase boundary:
    ///
    ///   Phase 1 — PRE-ACCEPT (rep physically absent from the audio stream):
    ///     Any non-Unknown SpeakerId in a Transcribed event is DEFINITIVELY the customer.
    ///     Latch as customerSpeakerId. This path is still correct and unchanged.
    ///
    ///   Phase 2 — POST-ACCEPT (both speakers present):
    ///     Case A — customerSpeakerId already latched in Phase 1:
    ///       First new distinct SpeakerId = rep. Latch as repSpeakerId.
    ///     Case B — NEITHER latched yet:
    ///       Inbound call-order rule applies (customer initiates, rep joins second):
    ///       first speaker seen = CUSTOMER, second distinct speaker = REP.
    ///
    ///   isCustomer is determined by matching the latched customerSpeakerId exactly.
    ///   Rep audio is transcribed but never scored for sentiment.
    ///   "Unknown" / empty SpeakerIds are never latched and never scored.
    ///
    /// Accept-gate:
    ///   Neither transcript nor sentiment events are emitted until RepAccepted is true. The
    ///   transcriber warms up and may latch speaker IDs before accept, but all SignalR sends
    ///   are suppressed — the rep console shows "Call Pending" until that point.
    /// </summary>
    private void WireTranscriberHandlers(
        ConversationTranscriber transcriber,
        Func<long> nextSequence,
        string fallbackLanguage,
        DefaultAzureCredential credential,
        TokenRequestContext tokenScope,
        string? translatorEndpoint,
        string? translatorRegion,
        string translatorTargetLanguage)
    {
        var firstPartialLogged = false;

        // Per-call speaker attribution state machine. See SpeakerAttributionState for full decision record.
        var attribution = new SpeakerAttributionState();

        transcriber.Transcribing += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Result.Text))
            {
                return;
            }

            if (!firstPartialLogged)
            {
                firstPartialLogged = true;
                _logger.LogInformation("SpeechTranscriptionService: first partial recognized — audio is reaching Speech.");
            }

            // Suppress emission until the rep has accepted the call (badge: "Call Pending").
            if (!_callStore.RepAccepted)
            {
                return;
            }

            if (ResolveGroup() is not { } transcribing)
            {
                return;
            }

            var (callId, group) = transcribing;
            var seq = nextSequence();
            var detectedLanguage = ResolveDetectedLanguage(e.Result, fallbackLanguage);
            var speakerId = e.Result.SpeakerId ?? string.Empty;
            var isCustomer = attribution.IsCustomer(speakerId);
            var evt = BuildTranscriptEvent(callId, seq, e.Result.ResultId, e.Result.Text,
                                           isFinal: false, detectedLanguage, speakerId, isCustomer);
            _ = _hub.Clients.Group(group)
                .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);
        };

        transcriber.Transcribed += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech)
            {
                if (e.Result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogInformation(
                        "SpeechTranscriptionService: NoMatch — audio received but no speech recognized (silence/noise/wrong language?).");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(e.Result.Text))
            {
                return;
            }

            var speakerId = e.Result.SpeakerId ?? string.Empty;

            // Advance the phase-aware attribution state machine. Logs the transition when a
            // slot is newly latched; returns null when nothing changed (already resolved /
            // same-speaker repeat / Unknown speaker — no log spam).
            var transition = attribution.Observe(speakerId, _callStore.RepAccepted);
            if (transition is not null)
            {
                _logger.LogInformation(
                    "SpeechTranscriptionService: speaker attribution — {Transition} for call {CallId}.",
                    transition,
                    _callStore.CallId ?? "(unknown)");
            }

            var isCustomer = attribution.IsCustomer(speakerId);

            // Suppress emission until the rep has accepted the call.
            if (!_callStore.RepAccepted)
            {
                return;
            }

            if (ResolveGroup() is not { } recognized)
            {
                return;
            }

            var (callId, group) = recognized;
            var seq = nextSequence();
            var detectedLanguage = ResolveDetectedLanguage(e.Result, fallbackLanguage);
            var transcriptEvent = BuildTranscriptEvent(callId, seq, e.Result.ResultId, e.Result.Text,
                                                        isFinal: true, detectedLanguage, speakerId, isCustomer);

            // Emit transcript for ALL speakers — diarization adds attribution, it does not drop
            // rep speech. The SpeakerId and SpeakerRole fields let the UI style each side.
            _ = _hub.Clients.Group(group)
                .SendAsync(PipelineContract.StreamNames.Transcript, transcriptEvent, CancellationToken.None);

            // Score sentiment ONLY for the customer. Rep utterances (including empathy phrases
            // like "I'm so sorry to hear that") must never move the customer sentiment meter —
            // that is the entire point of this diarization upgrade.
            if (isCustomer)
            {
                var sentimentEvent = _liveSentiment.Append(callId, e.Result.Text);
                if (sentimentEvent is not null)
                {
                    _ = _hub.Clients.Group(group)
                        .SendAsync(PipelineContract.StreamNames.Sentiment, sentimentEvent, CancellationToken.None);
                }
            }

            _ = PublishTranslationIfNeededAsync(
                callId,
                group,
                transcriptEvent,
                credential,
                tokenScope,
                translatorEndpoint,
                translatorRegion,
                translatorTargetLanguage);

            if (isCustomer)
            {
                _ = PublishReasoningAsync(
                    group,
                    transcriptEvent);
            }

            _logger.LogInformation(
                "SpeechTranscriptionService: FINAL utterance seq={Seq} callId={CallId} speaker={SpeakerId} isCustomer={IsCustomer} language={Language} text=\"{Text}\"",
                seq,
                callId,
                speakerId,
                isCustomer,
                detectedLanguage,
                e.Result.Text);
        };

        transcriber.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: transcription canceled with ERROR — ErrorCode={Code} Details={Details}",
                    e.ErrorCode,
                    e.ErrorDetails);
            }
            else
            {
                _logger.LogInformation("SpeechTranscriptionService: transcription canceled — Reason={Reason}.", e.Reason);
            }
        };

        transcriber.SessionStopped += (_, _) =>
            _logger.LogInformation("SpeechTranscriptionService: ConversationTranscriber session stopped.");
    }

    private async Task PublishTranslationIfNeededAsync(
        string callId,
        string group,
        TranscriptEvent transcriptEvent,
        DefaultAzureCredential credential,
        TokenRequestContext tokenScope,
        string? translatorEndpoint,
        string? translatorRegion,
        string translatorTargetLanguage)
    {
        if (string.IsNullOrWhiteSpace(translatorEndpoint) ||
            string.IsNullOrWhiteSpace(transcriptEvent.DetectedLanguage) ||
            transcriptEvent.DetectedLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(transcriptEvent.Text))
        {
            return;
        }

        try
        {
            var token = await credential.GetTokenAsync(tokenScope, CancellationToken.None).ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildTranslatorUri(translatorEndpoint, translatorTargetLanguage, transcriptEvent.DetectedLanguage));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            if (!string.IsNullOrWhiteSpace(translatorRegion))
            {
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", translatorRegion);
            }

            request.Content = JsonContent.Create(new[] { new { Text = transcriptEvent.Text } });

            using var client = _httpClientFactory.CreateClient(nameof(SpeechTranscriptionService));
            using var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: translator returned {StatusCode} for call {CallId} utterance {UtteranceId}.",
                    (int)response.StatusCode,
                    callId,
                    transcriptEvent.UtteranceId);
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (!TryParseTranslation(document.RootElement, out var translatedText, out var sourceLanguage, out var targetLanguage))
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: translator payload missing translation result for call {CallId} utterance {UtteranceId}.",
                    callId,
                    transcriptEvent.UtteranceId);
                return;
            }

            var translationEvent = new TranslationEvent
            {
                CallId = callId,
                EventId = $"evt-translation-live-{transcriptEvent.Sequence}",
                Sequence = transcriptEvent.Sequence,
                UtteranceId = transcriptEvent.UtteranceId,
                RelatedTranscriptEventId = transcriptEvent.EventId,
                RelatedTranscriptSequence = transcriptEvent.Sequence,
                TimestampUtc = DateTimeOffset.UtcNow,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                OriginalText = transcriptEvent.Text,
                TranslatedText = translatedText,
                Source = "azure-ai-translator"
            };

            await _hub.Clients.Group(group)
                .SendAsync(PipelineContract.StreamNames.Translation, translationEvent, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SpeechTranscriptionService: translation failed for call {CallId} utterance {UtteranceId}; transcript continues without translation.",
                callId,
                transcriptEvent.UtteranceId);
        }
    }

    private async Task PublishReasoningAsync(
        string group,
        TranscriptEvent transcriptEvent)
    {
        try
        {
            await foreach (var reasoningEvent in _reasoningClient.ProcessTranscriptAsync(transcriptEvent, CancellationToken.None).ConfigureAwait(false))
            {
                switch (reasoningEvent)
                {
                    case ChurnRiskEvent churnRiskEvent:
                        await _hub.Clients.Group(group)
                            .SendAsync(PipelineContract.StreamNames.ChurnRisk, churnRiskEvent, CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    case KnowledgeCardEvent knowledgeCardEvent:
                        await _hub.Clients.Group(group)
                            .SendAsync(PipelineContract.StreamNames.KnowledgeCards, knowledgeCardEvent, CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    case NextBestActionEvent nextBestActionEvent:
                        await _hub.Clients.Group(group)
                            .SendAsync(PipelineContract.StreamNames.NextBestAction, nextBestActionEvent, CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SpeechTranscriptionService: reasoning failed for transcript {TranscriptEventId}; call continues without assist updates.",
                transcriptEvent.EventId);
        }
    }

    private static IReadOnlyList<string> ParseCandidateLanguages(string? configuredLanguages)
    {
        var parsed = (configuredLanguages ?? "en-US,es-ES")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length > 0 ? parsed : ["en-US"];
    }

    private static string BuildAuthToken(string? resourceId, string aadToken) =>
        string.IsNullOrWhiteSpace(resourceId)
            ? aadToken
            : $"aad#{resourceId}#{aadToken}";

    private static Uri BuildTranslatorUri(string endpoint, string targetLanguage, string sourceLanguage)
    {
        var uri = new Uri(endpoint, UriKind.Absolute);
        var path = uri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(uri)
        {
            Path = string.IsNullOrEmpty(path) ? "translate" : $"{path}/translate",
            Query = $"api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}&from={Uri.EscapeDataString(sourceLanguage)}"
        };

        return builder.Uri;
    }

    private static bool TryParseTranslation(
        JsonElement root,
        out string translatedText,
        out string sourceLanguage,
        out string targetLanguage)
    {
        translatedText = string.Empty;
        sourceLanguage = string.Empty;
        targetLanguage = "en";

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return false;
        }

        var firstResult = root[0];
        if (firstResult.TryGetProperty("detectedLanguage", out var detectedLanguage) &&
            detectedLanguage.TryGetProperty("language", out var detectedValue))
        {
            sourceLanguage = detectedValue.GetString() ?? string.Empty;
        }

        if (!firstResult.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array ||
            translations.GetArrayLength() == 0)
        {
            return false;
        }

        var firstTranslation = translations[0];
        translatedText = firstTranslation.TryGetProperty("text", out var textValue)
            ? textValue.GetString() ?? string.Empty
            : string.Empty;

        if (firstTranslation.TryGetProperty("to", out var toValue))
        {
            targetLanguage = toValue.GetString() ?? targetLanguage;
        }

        return !string.IsNullOrWhiteSpace(translatedText);
    }

    private static string ResolveDetectedLanguage(SpeechRecognitionResult result, string fallbackLanguage)
    {
        try
        {
            var autoDetected = AutoDetectSourceLanguageResult.FromResult(result).Language;
            if (!string.IsNullOrWhiteSpace(autoDetected))
            {
                return autoDetected;
            }
        }
        catch
        {
            // no-op: fallback to configured recognition language
        }

        return fallbackLanguage;
    }

    private (string callId, string group)? ResolveGroup()
    {
        var callId = _callStore.CallId;
        if (string.IsNullOrEmpty(callId))
        {
            return null;
        }

        return (callId, PipelineContract.GroupNames.ForCall(callId));
    }

    private static TranscriptEvent BuildTranscriptEvent(
        string callId,
        long seq,
        string resultId,
        string text,
        bool isFinal,
        string detectedLanguage,
        string speakerId,
        bool isCustomer) =>
        new()
        {
            CallId = callId,
            EventId = $"evt-transcript-live-{seq}",
            Sequence = seq,
            UtteranceId = resultId,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsFinal = isFinal,
            SpeakerId = string.IsNullOrEmpty(speakerId) ? "unknown" : speakerId,
            SpeakerDisplayLabel = isCustomer ? "Customer" : string.IsNullOrEmpty(speakerId) ? "Speaker" : "Rep",
            SpeakerRole = isCustomer ? "customer" : string.IsNullOrEmpty(speakerId) ? "unknown" : "rep",
            SpeakerLabelSource = "conversation-transcriber-diarization",
            Text = text,
            DetectedLanguage = detectedLanguage,
            Source = "azure-ai-speech"
        };
}
