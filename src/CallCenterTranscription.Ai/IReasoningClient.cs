using CallCenterTranscription.Shared.Events;

namespace CallCenterTranscription.Ai;

public interface IReasoningClient
{
    IAsyncEnumerable<IRealtimeEvent> ProcessTranscriptAsync(
        TranscriptEvent transcriptEvent,
        CancellationToken cancellationToken = default);
}
