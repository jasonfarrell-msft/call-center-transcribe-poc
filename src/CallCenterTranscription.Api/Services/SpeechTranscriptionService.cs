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

namespace CallCenterTranscription.Api.Services;

public sealed class SpeechTranscriptionService : BackgroundService
{
    private readonly IAudioSource _audioSource;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly ActiveCallStore _callStore;
    private readonly PipelineCurrentStateStore _currentStateStore;
    private readonly LiveSentimentStore _liveSentiment;
    private readonly IReasoningClient _reasoningClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SpeechTranscriptionService> _logger;

    public SpeechTranscriptionService(
        IAudioSource audioSource,
        IHubContext<PipelineHub> hub,
        ActiveCallStore callStore,
        PipelineCurrentStateStore currentStateStore,
        LiveSentimentStore liveSentiment,
        IReasoningClient reasoningClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<SpeechTranscriptionService> logger)
    {
        _audioSource = audioSource;
        _hub = hub;
        _callStore = callStore;
        _currentStateStore = currentStateStore;
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
                if (recognizer is null)
                {
                    var accessToken = await credential.GetTokenAsync(tokenScope, stoppingToken).ConfigureAwait(false);

                    var speechConfig = SpeechConfig.FromEndpoint(new Uri(speechEndpoint));
                    speechConfig.AuthorizationToken = BuildAuthToken(speechResourceId, accessToken.Token);
                    speechConfig.SpeechRecognitionLanguage = candidateLanguages[0];

                    var autoDetectConfig = AutoDetectSourceLanguageConfig.FromLanguages(candidateLanguages.ToArray());
                    var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
                    pushStream = AudioInputStream.CreatePushStream(audioFormat);
                    audioConfig = AudioConfig.FromStreamInput(pushStream);
                    recognizer = new SpeechRecognizer(speechConfig, autoDetectConfig, audioConfig);

                    WireRecognitionHandlers(
                        recognizer,
                        () => Interlocked.Increment(ref sequence),
                        candidateLanguages[0],
                        credential,
                        tokenScope,
                        translatorEndpoint,
                        translatorRegion,
                        translatorTargetLanguage);

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
                pushStream!.Close();
                await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                _logger.LogInformation(
                    "SpeechTranscriptionService: recognizer stopped — {Frames} audio frames over {Seconds:F1}s for call {CallId}.",
                    framesWritten,
                    stopwatch.Elapsed.TotalSeconds,
                    _callStore.CallId ?? "(ended)");

                recognizer.Dispose();
                audioConfig?.Dispose();
                pushStream.Dispose();
            }
        }
    }

    private void WireRecognitionHandlers(
        SpeechRecognizer recognizer,
        Func<long> nextSequence,
        string fallbackLanguage,
        DefaultAzureCredential credential,
        TokenRequestContext tokenScope,
        string? translatorEndpoint,
        string? translatorRegion,
        string translatorTargetLanguage)
    {
        var firstPartialLogged = false;

        recognizer.Recognizing += (_, e) =>
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

            if (ResolveGroup() is not { } recognizing)
            {
                return;
            }

            var (callId, group) = recognizing;
            var seq = nextSequence();
            var detectedLanguage = ResolveDetectedLanguage(e.Result, fallbackLanguage);
            var evt = BuildTranscriptEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: false, detectedLanguage);
            _ = _hub.Clients.Group(group)
                .SendAsync(PipelineContract.StreamNames.Transcript, evt, CancellationToken.None);
        };

        recognizer.Recognized += (_, e) =>
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

            if (ResolveGroup() is not { } recognized)
            {
                return;
            }

            var (callId, group) = recognized;
            var seq = nextSequence();
            var detectedLanguage = ResolveDetectedLanguage(e.Result, fallbackLanguage);
            var transcriptEvent = BuildTranscriptEvent(callId, seq, e.Result.ResultId, e.Result.Text, isFinal: true, detectedLanguage);
            _currentStateStore.AppendTranscriptEvent(transcriptEvent);

            _ = _hub.Clients.Group(group)
                .SendAsync(PipelineContract.StreamNames.Transcript, transcriptEvent, CancellationToken.None);

            _liveSentiment.Append(callId, e.Result.Text);

            _ = PublishTranslationIfNeededAsync(
                callId,
                group,
                transcriptEvent,
                credential,
                tokenScope,
                translatorEndpoint,
                translatorRegion,
                translatorTargetLanguage);

            _ = PublishReasoningAsync(
                group,
                transcriptEvent);

            _logger.LogInformation(
                "SpeechTranscriptionService: FINAL utterance seq={Seq} callId={CallId} language={Language} text=\"{Text}\"",
                seq,
                callId,
                detectedLanguage,
                e.Result.Text);
        };

        recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                _logger.LogWarning(
                    "SpeechTranscriptionService: recognition canceled with ERROR — ErrorCode={Code} Details={Details}",
                    e.ErrorCode,
                    e.ErrorDetails);
            }
            else
            {
                _logger.LogInformation("SpeechTranscriptionService: recognition canceled — Reason={Reason}.", e.Reason);
            }
        };

        recognizer.SessionStopped += (_, _) =>
            _logger.LogInformation("SpeechTranscriptionService: Speech recognition session stopped.");
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
            _currentStateStore.AppendTranslationEvent(translationEvent);

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
                        _currentStateStore.AppendChurnRiskEvent(churnRiskEvent);
                        await _hub.Clients.Group(group)
                            .SendAsync(PipelineContract.StreamNames.ChurnRisk, churnRiskEvent, CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    case KnowledgeCardEvent knowledgeCardEvent:
                        _currentStateStore.AppendKnowledgeCardEvent(knowledgeCardEvent);
                        await _hub.Clients.Group(group)
                            .SendAsync(PipelineContract.StreamNames.KnowledgeCards, knowledgeCardEvent, CancellationToken.None)
                            .ConfigureAwait(false);
                        break;
                    case NextBestActionEvent nextBestActionEvent:
                        _currentStateStore.AppendNextBestActionEvent(nextBestActionEvent);
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
        string detectedLanguage) =>
        new()
        {
            CallId = callId,
            EventId = $"evt-transcript-live-{seq}",
            Sequence = seq,
            UtteranceId = resultId,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsFinal = isFinal,
            SpeakerId = "customer",
            SpeakerDisplayLabel = "Customer",
            SpeakerRole = "customer",
            SpeakerLabelSource = "acs-unmixed-customer",
            Text = text,
            DetectedLanguage = detectedLanguage,
            Source = "azure-ai-speech"
        };
}
