using CallCenterTranscription.Api;
using CallCenterTranscription.Shared.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CallCenterTranscription.Tests;

public sealed class AcsEndpointsLatencyTests
{
    [Fact]
    public async Task EmitCallPendingAsync_EmitsPendingEvent()
    {
        var pendingPayloads = new List<CallLifecycleEvent>();
        var proxy = new RecordingClientProxy((method, args) =>
        {
            if (method == PipelineContract.StreamNames.CallPending)
            {
                var payload = Assert.IsType<CallLifecycleEvent>(Assert.Single(args));
                pendingPayloads.Add(payload);
            }
        });

        await AcsEndpoints.EmitCallPendingAsync(
            "call-latency-1",
            proxy,
            CancellationToken.None,
            DateTimeOffset.Parse("2026-06-12T17:00:00Z"));

        Assert.Single(pendingPayloads);
        Assert.Equal("call-latency-1", pendingPayloads[0].CallId);
        Assert.Equal("pending", pendingPayloads[0].Status);
        Assert.Equal(DateTimeOffset.Parse("2026-06-12T17:00:00Z"), pendingPayloads[0].TimestampUtc);
    }

    [Fact]
    public void ResolvePublicBaseUri_UsesConfiguredPublicBaseUrl()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("internal.example.local");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acs:PublicBaseUrl"] = "https://demo.example.com"
            })
            .Build();

        var uri = AcsEndpoints.ResolvePublicBaseUri(ctx, configuration, NullLogger.Instance);

        Assert.Equal("https://demo.example.com/", uri.ToString());
    }

    [Fact]
    public void ResolvePublicBaseUri_PreservesConfiguredBasePath()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("internal.example.local");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acs:PublicBaseUrl"] = "https://demo.example.com/edge"
            })
            .Build();

        var uri = AcsEndpoints.ResolvePublicBaseUri(ctx, configuration, NullLogger.Instance);
        var callbackUri = AcsEndpoints.BuildPublicUri(uri, PathString.Empty, "/api/events/acs/callbacks");

        Assert.Equal("https://demo.example.com/edge", uri.ToString());
        Assert.Equal("https://demo.example.com/edge/api/events/acs/callbacks", callbackUri.ToString());
    }

    [Fact]
    public void BuildPublicUri_PreservesPathBaseAndRequestedScheme()
    {
        var baseUri = new Uri("https://demo.example.com");

        var callbackUri = AcsEndpoints.BuildPublicUri(baseUri, new PathString("/edge"), "/api/events/acs/callbacks");
        var mediaUri = AcsEndpoints.BuildPublicUri(baseUri, new PathString("/edge"), "/api/calls/media-stream", "wss");

        Assert.Equal("https://demo.example.com/edge/api/events/acs/callbacks", callbackUri.ToString());
        Assert.Equal("wss://demo.example.com/edge/api/calls/media-stream", mediaUri.ToString());
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
