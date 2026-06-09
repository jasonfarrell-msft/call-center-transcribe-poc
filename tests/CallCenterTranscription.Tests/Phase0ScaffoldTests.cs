using System.Text.Json;
using CallCenterTranscription.Ai;
using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Telephony;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenterTranscription.Tests;

public sealed class Phase0ScaffoldTests
{
    [Fact]
    public void TranscriptEvent_UsesDetectedLanguageJsonContract()
    {
        var transcript = new TranscriptEvent
        {
            CallId = "call-1",
            EventId = "evt-1",
            Sequence = 1,
            UtteranceId = "utt-1",
            SpeakerId = "customer",
            Text = "Hola, necesito ayuda con mi factura.",
            DetectedLanguage = "es"
        };

        var json = JsonSerializer.Serialize(transcript);

        Assert.Contains("\"detectedLanguage\":\"es\"", json);
        Assert.DoesNotContain("\"DetectedLanguage\":", json);
    }

    [Fact]
    public void TranslationEvent_UsesCorrelationJsonContract()
    {
        var translation = new TranslationEvent
        {
            CallId = "call-1",
            EventId = "evt-2",
            Sequence = 2,
            UtteranceId = "utt-3",
            RelatedTranscriptEventId = "evt-1",
            RelatedTranscriptSequence = 1,
            SourceLanguage = "es",
            TargetLanguage = "en",
            OriginalText = "Hola",
            TranslatedText = "Hi"
        };

        var json = JsonSerializer.Serialize(translation);

        Assert.Contains("\"relatedTranscriptEventId\":\"evt-1\"", json);
        Assert.Contains("\"utteranceId\":\"utt-3\"", json);
        Assert.DoesNotContain("\"RelatedTranscriptEventId\":", json);
    }

    [Fact]
    public void ApiServiceRegistration_ResolvesCorePipelineAbstractions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // No configuration → AudioSource:Mode defaults to "Acs"; live answering still waits on Acs:Endpoint.
        services.AddCallCenterServices(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        var audioSource = provider.GetRequiredService<IAudioSource>();
        var reasoningClient = provider.GetRequiredService<IReasoningClient>();

        Assert.IsType<AcsAudioSource>(audioSource);
        Assert.IsType<ConfiguredReasoningClient>(reasoningClient);
    }
}
