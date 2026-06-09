using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Options;

namespace CallCenterTranscription.Api.Services;

public sealed class AzureAiFoundryReasoningClient : IReasoningClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReasoningOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<AzureAiFoundryReasoningClient> _logger;

    public AzureAiFoundryReasoningClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ReasoningOptions> options,
        ILogger<AzureAiFoundryReasoningClient> logger)
        : this(httpClientFactory, options, logger, new DefaultAzureCredential())
    {
    }

    internal AzureAiFoundryReasoningClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ReasoningOptions> options,
        ILogger<AzureAiFoundryReasoningClient> logger,
        TokenCredential credential)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _credential = credential;
    }

    public async IAsyncEnumerable<IRealtimeEvent> ProcessTranscriptAsync(
        TranscriptEvent transcriptEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FoundryChatCompletionsUrl))
        {
            throw new InvalidOperationException("Reasoning:FoundryChatCompletionsUrl must be set when Reasoning:Mode is Hybrid or Live.");
        }

        var cards = KiraContentPack.Retrieve(transcriptEvent.Text, Math.Clamp(_options.MaxKnowledgeCards, 1, 3));
        var tokenRequest = new TokenRequestContext([_options.FoundryAudience]);
        var timeoutSeconds = Math.Clamp(_options.TimeoutSeconds, 3, 45);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var content = await GenerateReasoningAsync(transcriptEvent, cards, tokenRequest, timeoutCts.Token).ConfigureAwait(false);
        var parsed = ParseReasoning(content);
        var now = DateTimeOffset.UtcNow;

        var groundingContext = string.Join("; ", cards.Select(card => card.Title));
        var riskScore = Math.Clamp(parsed.RiskScore ?? 0.5, 0.01, 0.99);
        var riskLevel = NormalizeRiskLevel(parsed.RiskLevel, riskScore);
        var churnRationale = BuildRationale(parsed.Rationale, groundingContext);

        var action = string.IsNullOrWhiteSpace(parsed.Action) ? cards[0].RecommendedAction : parsed.Action.Trim();
        var confidence = Math.Clamp(parsed.Confidence ?? 0.6, 0.15, 0.99);
        var reasoning = BuildRationale(parsed.Reasoning, groundingContext);

        yield return new ChurnRiskEvent
        {
            CallId = transcriptEvent.CallId,
            EventId = $"evt-churn-risk-live-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            TimestampUtc = now,
            RiskLevel = riskLevel,
            RiskScore = riskScore,
            Rationale = churnRationale,
            Source = "azure-ai-foundry"
        };

        yield return new KnowledgeCardEvent
        {
            CallId = transcriptEvent.CallId,
            EventId = $"evt-knowledge-card-live-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            TimestampUtc = now,
            Cards = KiraContentPack.ToKnowledgeCards(cards),
            Source = "azure-ai-foundry-grounded"
        };

        yield return new NextBestActionEvent
        {
            CallId = transcriptEvent.CallId,
            EventId = $"evt-nba-live-{transcriptEvent.Sequence}",
            Sequence = transcriptEvent.Sequence,
            UtteranceId = transcriptEvent.UtteranceId,
            RelatedTranscriptEventId = transcriptEvent.EventId,
            RelatedTranscriptSequence = transcriptEvent.Sequence,
            TimestampUtc = now,
            Action = action,
            Confidence = confidence,
            Reasoning = reasoning,
            Source = "azure-ai-foundry"
        };
    }

    private async Task<string> GenerateReasoningAsync(
        TranscriptEvent transcriptEvent,
        IReadOnlyList<KiraContentPackEntry> cards,
        TokenRequestContext tokenRequest,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(_options.FoundryModel) ? null : _options.FoundryModel,
            temperature = 0.1,
            max_tokens = 300,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are the churn assist model for propane call-center retention. Keep responses concise, grounded in supplied knowledge, and safe for QA review."
                },
                new
                {
                    role = "user",
                    content = BuildPrompt(transcriptEvent, cards)
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri())
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };

        var token = await _credential.GetTokenAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var client = _httpClientFactory.CreateClient(nameof(AzureAiFoundryReasoningClient));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Foundry reasoning returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var responseDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!TryGetMessageContent(responseDocument.RootElement, out var content))
        {
            throw new InvalidOperationException("Foundry reasoning response did not include choices[0].message.content.");
        }

        return content;
    }

    private Uri BuildRequestUri()
    {
        var raw = _options.FoundryChatCompletionsUrl.Trim();
        if (raw.Contains("api-version=", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_options.FoundryApiVersion))
        {
            return new Uri(raw, UriKind.Absolute);
        }

        var separator = raw.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri($"{raw}{separator}api-version={Uri.EscapeDataString(_options.FoundryApiVersion)}", UriKind.Absolute);
    }

    private static string BuildPrompt(TranscriptEvent transcriptEvent, IReadOnlyList<KiraContentPackEntry> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Return strict JSON with this shape:");
        sb.AppendLine("{\"churnRisk\":{\"riskLevel\":\"low|moderate|high\",\"riskScore\":0.0,\"rationale\":\"...\"},\"nextBestAction\":{\"action\":\"...\",\"confidence\":0.0,\"reasoning\":\"...\"}}");
        sb.AppendLine("Constraints:");
        sb.AppendLine("- Keep rationale and reasoning under 180 characters each.");
        sb.AppendLine("- Include confidence/risk scores between 0 and 1.");
        sb.AppendLine("- Ground only in customer utterance + provided Kira content pack context.");
        sb.AppendLine();
        sb.AppendLine($"CallId: {transcriptEvent.CallId}");
        sb.AppendLine($"UtteranceId: {transcriptEvent.UtteranceId}");
        sb.AppendLine($"Transcript text: {transcriptEvent.Text}");
        sb.AppendLine("Grounding cards:");
        foreach (var card in cards)
        {
            sb.AppendLine($"- [{card.Id}] {card.Title}: {card.Snippet}");
        }

        return sb.ToString();
    }

    private static bool TryGetMessageContent(JsonElement responseRoot, out string content)
    {
        content = string.Empty;
        if (!responseRoot.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentValue))
        {
            return false;
        }

        content = contentValue.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(content);
    }

    private static ParsedReasoning ParseReasoning(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var churn = root.TryGetProperty("churnRisk", out var churnRisk) ? churnRisk : default;
            var nba = root.TryGetProperty("nextBestAction", out var nextBestAction) ? nextBestAction : default;

            return new ParsedReasoning
            {
                RiskLevel = TryGetString(churn, "riskLevel"),
                RiskScore = TryGetDouble(churn, "riskScore"),
                Rationale = TryGetString(churn, "rationale"),
                Action = TryGetString(nba, "action"),
                Confidence = TryGetDouble(nba, "confidence"),
                Reasoning = TryGetString(nba, "reasoning")
            };
        }
        catch (JsonException)
        {
            return new ParsedReasoning();
        }
    }

    private static string NormalizeRiskLevel(string? riskLevel, double riskScore)
    {
        if (!string.IsNullOrWhiteSpace(riskLevel))
        {
            var normalized = riskLevel.Trim().ToLowerInvariant();
            if (normalized is "low" or "moderate" or "high")
            {
                return normalized;
            }
        }

        return riskScore switch
        {
            >= 0.75 => "high",
            >= 0.45 => "moderate",
            _ => "low"
        };
    }

    private static string BuildRationale(string? reasoning, string groundingContext)
    {
        var baseReasoning = string.IsNullOrWhiteSpace(reasoning)
            ? "Model response provided no explicit rationale."
            : reasoning.Trim();

        return $"{baseReasoning} Grounding: {groundingContext}.";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetDouble(out var parsed) ? parsed : null;
    }

    private sealed record ParsedReasoning
    {
        public string? RiskLevel { get; init; }
        public double? RiskScore { get; init; }
        public string? Rationale { get; init; }
        public string? Action { get; init; }
        public double? Confidence { get; init; }
        public string? Reasoning { get; init; }
    }
}
