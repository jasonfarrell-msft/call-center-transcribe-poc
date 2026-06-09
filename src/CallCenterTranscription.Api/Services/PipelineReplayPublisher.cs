using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.SignalR;

namespace CallCenterTranscription.Api.Services;

public sealed class PipelineReplayPublisher
{
    public async Task ReplayForCallAsync(
        IClientProxy client,
        PipelineCurrentStateResponse snapshot,
        string callId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(snapshot.Call.CallId, callId, StringComparison.Ordinal))
        {
            return;
        }

        await ReplayAsync(client, snapshot, cancellationToken);
    }

    public async Task ReplayForSessionAsync(
        IClientProxy client,
        PipelineCurrentStateResponse snapshot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(snapshot.Call.SessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        await ReplayAsync(client, snapshot, cancellationToken);
    }

    private static async Task ReplayAsync(
        IClientProxy client,
        PipelineCurrentStateResponse snapshot,
        CancellationToken cancellationToken)
    {
        await client.SendCoreAsync(PipelineContract.StreamNames.CurrentState, [snapshot], cancellationToken);

        foreach (var evt in snapshot.TranscriptEvents.OrderBy(static item => item.Sequence))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.Transcript, [evt], cancellationToken);
        }

        foreach (var evt in snapshot.TranslationEvents.OrderBy(static item => item.Sequence))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.Translation, [evt], cancellationToken);
        }

        foreach (var evt in snapshot.SentimentEvents.OrderBy(static item => item.TimestampUtc))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.Sentiment, [evt], cancellationToken);
        }

        foreach (var evt in snapshot.ChurnRiskEvents.OrderBy(static item => item.Sequence))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.ChurnRisk, [evt], cancellationToken);
        }

        foreach (var evt in snapshot.KnowledgeCardEvents.OrderBy(static item => item.Sequence))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.KnowledgeCards, [evt], cancellationToken);
        }

        foreach (var evt in snapshot.NextBestActionEvents.OrderBy(static item => item.Sequence))
        {
            await client.SendCoreAsync(PipelineContract.StreamNames.NextBestAction, [evt], cancellationToken);
        }
    }
}
