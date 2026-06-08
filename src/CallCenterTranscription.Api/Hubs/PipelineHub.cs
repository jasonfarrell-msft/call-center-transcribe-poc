using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.SignalR;

namespace CallCenterTranscription.Api.Hubs;

public sealed class PipelineHub : Hub
{
    public Task SubscribeToCall(string callId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForCall(ValidateIdentifier(callId, "callId")));

    public Task UnsubscribeFromCall(string callId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForCall(ValidateIdentifier(callId, "callId")));

    public Task SubscribeToSession(string sessionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, PipelineContract.GroupNames.ForSession(ValidateIdentifier(sessionId, "sessionId")));

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
