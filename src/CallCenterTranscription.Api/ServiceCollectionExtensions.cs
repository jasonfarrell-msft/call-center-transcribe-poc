using Azure.Communication.CallAutomation;
using Azure.Identity;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Telephony;

namespace CallCenterTranscription.Api;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all call-center services.
    ///
    /// DI swap — AudioSource:Mode (env: AudioSource__Mode):
    ///   "Mock" (DEFAULT) → MockAudioSource (no ACS dependency; nothing breaks when unset)
    ///   "Acs"            → AcsAudioSource  (Channel-backed; needs Acs:Endpoint + RBAC)
    ///
    /// AcsAudioSource is ALWAYS registered as a singleton so the media-stream WebSocket
    /// handler can inject it regardless of mode (it stays dormant/empty when Mode=Mock).
    ///
    /// CallAutomationClient is registered when Acs:Endpoint is configured; uses
    /// DefaultAzureCredential (managed identity on ACA). No connection strings.
    /// </summary>
    public static IServiceCollection AddCallCenterServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSignalR();
        services.AddSingleton<IReasoningClient, MockReasoningClient>();
        services.AddSingleton<IScriptedScenarioFeed, ScriptedPropaneRetentionScenarioFeed>();

        // ActiveCallStore: holds the current ACS call ID so SpeechTranscriptionService can
        // route transcript events to the correct SignalR group ("call:{callId}").
        services.AddSingleton<ActiveCallStore>();

        // LiveSentimentStore: turns finalized live transcript into a rolling sentiment score so
        // the rep console's sentiment meter moves with the conversation (live/Acs mode only).
        services.AddSingleton<LiveSentimentStore>();

        // SpeechTranscriptionService: reads IAudioSource → Azure AI Speech → PipelineHub.
        // Self-gates: exits cleanly if Speech:Endpoint / Speech:Region are not configured,
        // or if AAD token acquisition fails (e.g., running locally without a managed identity).
        services.AddHostedService<SpeechTranscriptionService>();

        // Always register AcsAudioSource as a concrete singleton so the WebSocket handler
        // can inject it directly (see AcsEndpoints.MapAcsRoutes). In Mock mode this instance
        // is dormant — its Channel stays empty because no calls are answered.
        services.AddSingleton<AcsAudioSource>();

        var audioSourceMode = configuration.GetValue<string>("AudioSource:Mode") ?? "Mock";
        if (string.Equals(audioSourceMode, "Acs", StringComparison.OrdinalIgnoreCase))
        {
            // Forward the same singleton via the interface contract.
            services.AddSingleton<IAudioSource>(sp => sp.GetRequiredService<AcsAudioSource>());
        }
        else
        {
            // Default: MockAudioSource — safe with no ACS provisioning.
            services.AddSingleton<IAudioSource, MockAudioSource>();
        }

        // CallAutomationClient: managed identity (DefaultAzureCredential), NO connection strings.
        // Registered only when Acs:Endpoint is configured. The RBAC role assignment
        // (Communication Services Contributor on the ACS resource) is Meyrin's Bicep deliverable.
        var acsEndpoint = configuration["Acs:Endpoint"];
        if (!string.IsNullOrEmpty(acsEndpoint))
        {
            services.AddSingleton(new CallAutomationClient(
                new Uri(acsEndpoint),
                new DefaultAzureCredential()));
        }

        return services;
    }
}
