using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace CallCenterTranscription.Api.Hubs;

public sealed class PipelineHub(
    PipelineCurrentStateStore currentStateStore,
    PipelineReplayPublisher replayPublisher) : Hub
{
    public async Task SubscribeToCall(string callId)
    {
        var normalizedCallId = ValidateIdentifier(callId, "callId");
        await Groups.AddToGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForCall(normalizedCallId));

        await replayPublisher.ReplayForCallAsync(
            Clients.Caller,
            currentStateStore.GetSnapshot(),
            normalizedCallId,
            Context.ConnectionAborted);
    }

    public Task UnsubscribeFromCall(string callId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForCall(ValidateIdentifier(callId, "callId")));

    public async Task SubscribeToSession(string sessionId)
    {
        var normalizedSessionId = ValidateIdentifier(sessionId, "sessionId");
        await Groups.AddToGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForSession(normalizedSessionId));

        await replayPublisher.ReplayForSessionAsync(
            Clients.Caller,
            currentStateStore.GetSnapshot(),
            normalizedSessionId,
            Context.ConnectionAborted);
    }

    public Task UnsubscribeFromSession(string sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForSession(ValidateIdentifier(sessionId, "sessionId")));

    private static string ValidateIdentifier(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HubException($"{argumentName} must be provided.");
        }

        return value.Trim();
    }
}
