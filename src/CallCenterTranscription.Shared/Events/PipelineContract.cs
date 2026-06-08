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
        // Disconnected → Connecting → Live state machine without pre-knowing the ACS-generated callId.
        public const string CallStarted = "stream.callStarted";
        public const string CallEnded = "stream.callEnded";
    }

    public static class GroupNames
    {
        public static string ForCall(string callId) => $"call:{callId}";
        public static string ForSession(string sessionId) => $"session:{sessionId}";
    }
}
