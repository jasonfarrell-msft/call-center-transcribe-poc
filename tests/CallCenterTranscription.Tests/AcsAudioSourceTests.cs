using System.Text;
using System.Text.Json;
using CallCenterTranscription.Telephony;
using Microsoft.Extensions.Logging.Abstractions;

namespace CallCenterTranscription.Tests;

public sealed class AcsAudioSourceTests
{
    [Fact]
    public async Task ReadAsync_WhenOnlyCommunicationUserFramesPresent_DoesNotDropAudio()
    {
        var source = new AcsAudioSource(NullLogger<AcsAudioSource>.Instance);
        source.BeginSession();

        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("8:acs:test-caller"));
        source.CompleteStream();

        var frames = await ReadAllAsync(source);

        Assert.Single(frames);
    }

    [Fact]
    public async Task ReadAsync_WhenNonCommunicationParticipantObserved_DropsCommunicationUserFrames()
    {
        var source = new AcsAudioSource(NullLogger<AcsAudioSource>.Instance);
        source.BeginSession();

        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("4:+12065550100"));
        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("8:acs:test-rep"));
        source.CompleteStream();

        var frames = await ReadAllAsync(source);

        Assert.Single(frames);
    }

    [Fact]
    public async Task ReadAsync_WhenCommunicationUserSpeaksBeforePstn_DropsBufferedCommunicationFrames()
    {
        var source = new AcsAudioSource(NullLogger<AcsAudioSource>.Instance);
        source.BeginSession();

        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("8:acs:test-rep"));
        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("4:+12065550100"));
        source.CompleteStream();

        var frames = await ReadAllAsync(source);

        Assert.Single(frames);
    }

    [Fact]
    public async Task ReadAsync_WhenUnknownParticipantObserved_CommunicationFramesAreNotRebufferedAndDropped()
    {
        var source = new AcsAudioSource(NullLogger<AcsAudioSource>.Instance);
        source.BeginSession();

        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("1:unknown-customer"));
        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("8:acs:test-rep"));
        await source.HandleWebSocketMessageAsync(BuildAudioDataMessage("4:+12065550100"));
        source.CompleteStream();

        var frames = await ReadAllAsync(source);

        Assert.Equal(3, frames.Count);
    }

    private static async Task<List<AudioFrame>> ReadAllAsync(AcsAudioSource source)
    {
        var frames = new List<AudioFrame>();
        await foreach (var frame in source.ReadAsync())
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static byte[] BuildAudioDataMessage(string participantRawId)
    {
        var payload = new
        {
            kind = "AudioData",
            audioData = new
            {
                data = Convert.ToBase64String([1, 2, 3, 4]),
                silent = false,
                participantRawId,
                timestamp = "2026-06-09T17:00:00Z"
            }
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
    }
}
