using CallCenterTranscription.Ai;
using CallCenterTranscription.Telephony;

namespace CallCenterTranscription.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCallCenterServices(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IAudioSource, MockAudioSource>();
        services.AddSingleton<IReasoningClient, MockReasoningClient>();
        services.AddSingleton<IScriptedScenarioFeed, ScriptedPropaneRetentionScenarioFeed>();

        return services;
    }
}
