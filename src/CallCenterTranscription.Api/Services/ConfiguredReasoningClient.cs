using CallCenterTranscription.Ai;
using CallCenterTranscription.Shared.Events;
using Microsoft.Extensions.Options;

namespace CallCenterTranscription.Api.Services;

public sealed class ConfiguredReasoningClient : IReasoningClient
{
    private readonly ReasoningOptions _options;
    private readonly MockReasoningClient _mockReasoningClient;
    private readonly AzureAiFoundryReasoningClient _foundryReasoningClient;
    private readonly ILogger<ConfiguredReasoningClient> _logger;

    public ConfiguredReasoningClient(
        IOptions<ReasoningOptions> options,
        MockReasoningClient mockReasoningClient,
        AzureAiFoundryReasoningClient foundryReasoningClient,
        ILogger<ConfiguredReasoningClient> logger)
    {
        _options = options.Value;
        _mockReasoningClient = mockReasoningClient;
        _foundryReasoningClient = foundryReasoningClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<IRealtimeEvent> ProcessTranscriptAsync(
        TranscriptEvent transcriptEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var mode = (_options.Mode ?? "Mock").Trim();
        var events = new List<IRealtimeEvent>();

        if (mode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            events = await CollectAsync(_mockReasoningClient.ProcessTranscriptAsync(transcriptEvent, cancellationToken), cancellationToken);
        }
        else
        {
            var fallbackEnabled = _options.FallbackToMock || mode.Equals("Hybrid", StringComparison.OrdinalIgnoreCase);

            try
            {
                events = await CollectAsync(_foundryReasoningClient.ProcessTranscriptAsync(transcriptEvent, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!fallbackEnabled)
                {
                    throw;
                }

                _logger.LogWarning("Foundry reasoning timed out for transcript {TranscriptEventId}; falling back to mock reasoning.", transcriptEvent.EventId);
                events = await CollectAsync(_mockReasoningClient.ProcessTranscriptAsync(transcriptEvent, cancellationToken), cancellationToken);
                events = events.Select(RewriteFallbackSource).ToList();
            }
            catch (Exception ex)
            {
                if (!fallbackEnabled)
                {
                    throw;
                }

                _logger.LogWarning(ex, "Foundry reasoning failed for transcript {TranscriptEventId}; falling back to mock reasoning.", transcriptEvent.EventId);
                events = await CollectAsync(_mockReasoningClient.ProcessTranscriptAsync(transcriptEvent, cancellationToken), cancellationToken);
                events = events.Select(RewriteFallbackSource).ToList();
            }
        }

        foreach (var evt in events)
        {
            yield return evt;
        }
    }

    private static IRealtimeEvent RewriteFallbackSource(IRealtimeEvent evt) =>
        evt switch
        {
            SentimentEvent sentimentEvent => sentimentEvent with { Source = "mock-reasoning-fallback" },
            ChurnRiskEvent churnRiskEvent => churnRiskEvent with { Source = "mock-reasoning-fallback" },
            KnowledgeCardEvent knowledgeCardEvent => knowledgeCardEvent with { Source = "mock-reasoning-fallback" },
            NextBestActionEvent nextBestActionEvent => nextBestActionEvent with { Source = "mock-reasoning-fallback" },
            _ => evt
        };

    private static async Task<List<IRealtimeEvent>> CollectAsync(
        IAsyncEnumerable<IRealtimeEvent> source,
        CancellationToken cancellationToken)
    {
        var events = new List<IRealtimeEvent>();
        await foreach (var evt in source.WithCancellation(cancellationToken))
        {
            events.Add(evt);
        }

        return events;
    }
}
