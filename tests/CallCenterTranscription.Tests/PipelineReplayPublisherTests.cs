using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.SignalR;

namespace CallCenterTranscription.Tests;

public sealed class PipelineReplayPublisherTests
{
    [Fact]
    public async Task ReplayForCallAsync_PublishesCurrentStateAndAllEventTypesInOrder()
    {
        var publisher = new PipelineReplayPublisher();
        var snapshot = new ScriptedPropaneRetentionScenarioFeed().GetCurrentState();
        var client = new RecordingClientProxy();

        await publisher.ReplayForCallAsync(client, snapshot, snapshot.Call.CallId, CancellationToken.None);

        Assert.NotEmpty(client.Messages);
        Assert.Equal(PipelineContract.StreamNames.CurrentState, client.Messages[0].Method);
        Assert.Equal(PipelineContract.StreamNames.CallAccepted, client.Messages[1].Method);

        var transcript = client.Messages
            .Where(static message => message.Method == PipelineContract.StreamNames.Transcript)
            .Select(static message => message.Args.OfType<TranscriptEvent>().Single())
            .ToList();
        Assert.Equal(snapshot.TranscriptEvents.Count, transcript.Count);
        Assert.True(transcript.Zip(transcript.Skip(1), (left, right) => left.Sequence < right.Sequence).All(static item => item));

        Assert.Equal(snapshot.TranslationEvents.Count,
            client.Messages.Count(static message => message.Method == PipelineContract.StreamNames.Translation));
        Assert.Equal(snapshot.SentimentEvents.Count,
            client.Messages.Count(static message => message.Method == PipelineContract.StreamNames.Sentiment));
        Assert.Equal(snapshot.ChurnRiskEvents.Count,
            client.Messages.Count(static message => message.Method == PipelineContract.StreamNames.ChurnRisk));
        Assert.Equal(snapshot.KnowledgeCardEvents.Count,
            client.Messages.Count(static message => message.Method == PipelineContract.StreamNames.KnowledgeCards));
        Assert.Equal(snapshot.NextBestActionEvents.Count,
            client.Messages.Count(static message => message.Method == PipelineContract.StreamNames.NextBestAction));
    }

    [Fact]
    public async Task ReplayForCallAsync_WhenSnapshotIsPending_ReplaysPendingLifecycleBeforeTranscript()
    {
        var publisher = new PipelineReplayPublisher();
        var baseline = new ScriptedPropaneRetentionScenarioFeed().GetCurrentState();
        var snapshot = baseline with
        {
            Call = baseline.Call with
            {
                State = "pending"
            }
        };
        var client = new RecordingClientProxy();

        await publisher.ReplayForCallAsync(client, snapshot, snapshot.Call.CallId, CancellationToken.None);

        Assert.Equal(PipelineContract.StreamNames.CurrentState, client.Messages[0].Method);
        Assert.Equal(PipelineContract.StreamNames.CallPending, client.Messages[1].Method);
        Assert.DoesNotContain(client.Messages, message => message.Method == PipelineContract.StreamNames.Transcript);
    }

    [Fact]
    public async Task ReplayForCallAsync_PreservesTranscriptCorrelationForDerivedEvents()
    {
        var publisher = new PipelineReplayPublisher();
        var snapshot = new ScriptedPropaneRetentionScenarioFeed().GetCurrentState();
        var client = new RecordingClientProxy();

        await publisher.ReplayForCallAsync(client, snapshot, snapshot.Call.CallId, CancellationToken.None);

        var transcriptEventIds = client.Messages
            .Where(static message => message.Method == PipelineContract.StreamNames.Transcript)
            .Select(static message => message.Args.OfType<TranscriptEvent>().Single().EventId)
            .ToHashSet(StringComparer.Ordinal);

        var translation = client.Messages
            .Where(static message => message.Method == PipelineContract.StreamNames.Translation)
            .Select(static message => message.Args.OfType<TranslationEvent>().Single());
        Assert.All(translation, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.RelatedTranscriptEventId));
            Assert.Contains(item.RelatedTranscriptEventId, transcriptEventIds);
        });

        var churnRisk = client.Messages
            .Where(static message => message.Method == PipelineContract.StreamNames.ChurnRisk)
            .Select(static message => message.Args.OfType<ChurnRiskEvent>().Single());
        Assert.All(churnRisk, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.RelatedTranscriptEventId));
            Assert.Contains(item.RelatedTranscriptEventId, transcriptEventIds);
        });
    }

    [Fact]
    public async Task ReplayForSessionAsync_ReconnectReplaysFullSnapshot()
    {
        var publisher = new PipelineReplayPublisher();
        var snapshot = new ScriptedPropaneRetentionScenarioFeed().GetCurrentState();
        var client = new RecordingClientProxy();

        await publisher.ReplayForSessionAsync(client, snapshot, snapshot.Call.SessionId, CancellationToken.None);
        var firstReplayCount = client.Messages.Count;

        await publisher.ReplayForSessionAsync(client, snapshot, snapshot.Call.SessionId, CancellationToken.None);

        Assert.Equal(firstReplayCount * 2, client.Messages.Count);
        Assert.Equal(PipelineContract.StreamNames.CurrentState, client.Messages[0].Method);
        Assert.Equal(PipelineContract.StreamNames.CurrentState, client.Messages[firstReplayCount].Method);
    }

    [Fact]
    public async Task ReplayForCallAsync_WhenCallIdDoesNotMatch_DoesNotPublish()
    {
        var publisher = new PipelineReplayPublisher();
        var snapshot = new ScriptedPropaneRetentionScenarioFeed().GetCurrentState();
        var client = new RecordingClientProxy();

        await publisher.ReplayForCallAsync(client, snapshot, "call-does-not-exist", CancellationToken.None);

        Assert.Empty(client.Messages);
    }

    private sealed record SentMessage(string Method, IReadOnlyList<object?> Args);

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<SentMessage> Messages { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Messages.Add(new SentMessage(method, args));
            return Task.CompletedTask;
        }
    }
}
