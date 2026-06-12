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
        var callState = await ReplayLifecycleAsync(client, snapshot, cancellationToken);
        if (callState == "pending")
        {
            return;
        }

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

    private static async Task<string> ReplayLifecycleAsync(
        IClientProxy client,
        PipelineCurrentStateResponse snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Call.CallId))
        {
            return "idle";
        }

        var state = (snapshot.Call.State ?? string.Empty).Trim().ToLowerInvariant();
        if (state == "pending")
        {
            await client.SendCoreAsync(
                PipelineContract.StreamNames.CallPending,
                [new CallLifecycleEvent
                {
                    CallId = snapshot.Call.CallId,
                    Status = "pending",
                    TimestampUtc = snapshot.GeneratedAtUtc
                }],
                cancellationToken);
            return state;
        }

        if (state is "accepted" or "active" or "live")
        {
            await client.SendCoreAsync(
                PipelineContract.StreamNames.CallAccepted,
                [new CallLifecycleEvent
                {
                    CallId = snapshot.Call.CallId,
                    Status = "accepted",
                    TimestampUtc = snapshot.GeneratedAtUtc
                }],
                cancellationToken);
        }

        return state;
    }
}
