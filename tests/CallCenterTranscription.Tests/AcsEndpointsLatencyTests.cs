using Azure.Communication.CallAutomation;
using Azure.Identity;
using CallCenterTranscription.Api;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace CallCenterTranscription.Tests;

public sealed class AcsEndpointsLatencyTests
{
    [Fact]
    public async Task EmitCallPendingAndTryAddRepAsync_EmitsPendingBeforeRepAddAttempt()
    {
        var callStore = new ActiveCallStore();
        callStore.CompleteIncomingClaim("call-latency-1");

        var registry = new RepRegistry();
        registry.Register("8:acs:rep");

        var callClient = new CallAutomationClient(
            new Uri("https://contoso.communication.azure.com"),
            new DefaultAzureCredential());

        var pendingPayloads = new List<CallLifecycleEvent>();
        var proxy = new RecordingClientProxy((method, args) =>
        {
            if (method == PipelineContract.StreamNames.CallPending)
            {
                var payload = Assert.IsType<CallLifecycleEvent>(Assert.Single(args));
                pendingPayloads.Add(payload);
            }
        });

        var sequence = 0;
        var pendingSequence = 0;
        var addSequence = 0;
        proxy.OnSend = (method, _) =>
        {
            if (method == PipelineContract.StreamNames.CallPending)
            {
                pendingSequence = ++sequence;
            }
        };

        await AcsEndpoints.EmitCallPendingAndTryAddRepAsync(
            "call-latency-1",
            proxy,
            callClient,
            callStore,
            registry,
            NullLogger.Instance,
            CancellationToken.None,
            (_, _, _, _, _) =>
            {
                addSequence = ++sequence;
                return Task.CompletedTask;
            });

        Assert.Single(pendingPayloads);
        Assert.Equal("call-latency-1", pendingPayloads[0].CallId);
        Assert.Equal("pending", pendingPayloads[0].Status);
        Assert.True(pendingSequence > 0, "callPending must be emitted.");
        Assert.True(addSequence > pendingSequence, "rep add attempt must happen after callPending emit.");
    }

    [Fact]
    public async Task EmitCallPendingAndTryAddRepAsync_EmitsPendingEvenWhenRepNotRegistered()
    {
        var callStore = new ActiveCallStore();
        callStore.CompleteIncomingClaim("call-latency-2");
        var registry = new RepRegistry();

        var callClient = new CallAutomationClient(
            new Uri("https://contoso.communication.azure.com"),
            new DefaultAzureCredential());

        var pendingCount = 0;
        var proxy = new RecordingClientProxy((method, _) =>
        {
            if (method == PipelineContract.StreamNames.CallPending)
            {
                pendingCount++;
            }
        });

        var addAttempted = false;
        await AcsEndpoints.EmitCallPendingAndTryAddRepAsync(
            "call-latency-2",
            proxy,
            callClient,
            callStore,
            registry,
            NullLogger.Instance,
            CancellationToken.None,
            (_, _, _, _, _) =>
            {
                addAttempted = true;
                return Task.CompletedTask;
            });

        Assert.Equal(1, pendingCount);
        Assert.False(addAttempted, "rep add should not be attempted when no rep is registered.");
    }

    [Fact]
    public async Task EmitCallPendingAndTryAddRepAsync_DoesNotFailWhenEarlyAddThrows()
    {
        var callStore = new ActiveCallStore();
        callStore.CompleteIncomingClaim("call-latency-3");
        var registry = new RepRegistry();
        registry.Register("8:acs:rep");
        var callClient = new CallAutomationClient(
            new Uri("https://contoso.communication.azure.com"),
            new DefaultAzureCredential());

        var methods = new List<string>();
        var proxy = new RecordingClientProxy((method, _) => methods.Add(method));

        await AcsEndpoints.EmitCallPendingAndTryAddRepAsync(
            "call-latency-3",
            proxy,
            callClient,
            callStore,
            registry,
            NullLogger.Instance,
            CancellationToken.None,
            (_, _, _, _, _) => throw new InvalidOperationException("expected test fault"));

        Assert.Single(methods);
        Assert.Equal(PipelineContract.StreamNames.CallPending, methods[0]);
    }

    [Theory]
    [InlineData("call-1", "call-1", true)]
    [InlineData("call-1", "call-2", false)]
    [InlineData("call-1", "", false)]
    [InlineData("", "call-1", false)]
    public void IsCurrentActiveCall_MatchesOnlyExactActiveCallId(
        string activeCallId,
        string callbackCallId,
        bool expected)
    {
        var actual = AcsEndpoints.IsCurrentActiveCall(activeCallId, callbackCallId);
        Assert.Equal(expected, actual);
    }

    private sealed class RecordingClientProxy(Action<string, IReadOnlyList<object?>> onSend) : IClientProxy
    {
        private readonly Action<string, IReadOnlyList<object?>> _onSend = onSend;

        public Action<string, IReadOnlyList<object?>>? OnSend { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            var payload = args.ToList().AsReadOnly();
            _onSend(method, payload);
            OnSend?.Invoke(method, payload);
            return Task.CompletedTask;
        }
    }
}
