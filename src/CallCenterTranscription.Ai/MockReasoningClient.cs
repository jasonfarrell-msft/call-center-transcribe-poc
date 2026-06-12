using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public sealed class MockReasoningClient : IReasoningClient
{
    private readonly DemoAssistMatcher _matcher;

    public MockReasoningClient()
        : this(new DemoAssistMatcher())
    {
    }

    public MockReasoningClient(DemoAssistMatcher matcher)
    {
        _matcher = matcher;
    }

    public async IAsyncEnumerable<IRealtimeEvent> ProcessTranscriptAsync(
        TranscriptEvent transcriptEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        var match = _matcher.Match(transcriptEvent);
        if (!match.HasMatches)
        {
            yield break;
        }

        var now = DateTimeOffset.UtcNow;
        yield return match.CreateChurnRiskEvent("demo_trigger_matcher", now);
        yield return match.CreateKnowledgeCardEvent("demo_trigger_matcher", now);
        yield return match.CreateNextBestActionEvent("demo_trigger_matcher", now);
    }
}
