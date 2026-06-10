namespace CallCenterTranscription.Shared.Events;

public static class PipelineContract
{
    public static class HubMethods
    {
        public const string SubscribeToCall = "SubscribeToCall";
        public const string UnsubscribeFromCall = "UnsubscribeFromCall";
        public const string SubscribeToSession = "SubscribeToSession";
        public const string UnsubscribeFromSession = "UnsubscribeFromSession";
    }

    public static class StreamNames
    {
        public const string Transcript = "stream.transcript";
        public const string Translation = "stream.translation";
        public const string Sentiment = "stream.sentiment";
        public const string ChurnRisk = "stream.churnRisk";
        public const string KnowledgeCards = "stream.knowledgeCards";
        public const string NextBestAction = "stream.nextBestAction";
        public const string CurrentState = "stream.currentState";

        // Call lifecycle — broadcast to all console clients so the dashboard can drive its
        // Disconnected → Pending → Accepted → Live state machine without pre-knowing the ACS callId.
        //
        // Sequence:
        //   CallPending  — fired when ACS answers and rep softphone starts ringing (rep has NOT accepted)
        //   CallAccepted — fired when AddParticipantSucceeded (rep clicked Accept); transcription is live
        //   CallEnded    — fired when media-stream WebSocket finally-block runs (any disconnect path)
        //
        // CallStarted is kept for backward compatibility but is no longer broadcast in normal flow.
        public const string CallStarted  = "stream.callStarted";
        public const string CallPending  = "stream.callPending";
        public const string CallAccepted = "stream.callAccepted";
        public const string CallEnded    = "stream.callEnded";
    }

    public static class GroupNames
    {
        public static string ForCall(string callId) => $"call:{callId}";
        public static string ForSession(string sessionId) => $"session:{sessionId}";
    }
}
