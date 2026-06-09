using Azure.Communication.CallAutomation;
using CallCenterTranscription.Api.Hubs;
using CallCenterTranscription.Api.Services;
using CallCenterTranscription.Shared.Events;
using CallCenterTranscription.Telephony;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text.Json;

namespace CallCenterTranscription.Api;

/// <summary>
/// Maps ACS event-webhook and media-stream WebSocket routes.
///
/// Auth posture:
///   These routes are EXCLUDED from the AgentAssistAccess JWT policy — Event Grid and the
///   ACS media-streaming connector cannot present Bearer tokens. They are placed in a separate
///   AllowAnonymous route group.
///
///   TODO (next round — Event Grid wiring): When Meyrin wires the Event Grid subscription,
///   Microsoft Entra delivery authentication MUST be added to the subscription before going
///   live with a real phone number. This is a blocking security prerequisite.
///
/// Dormancy:
///   Routes are always present. When AudioSource:Mode=Mock, no real calls are answered so
///   the IncomingCall handler is never triggered in practice, and the WebSocket endpoint
///   receives no connections.
/// </summary>
internal static class AcsEndpoints
{
    private static readonly TimeSpan DisconnectedCleanupDelay = TimeSpan.FromSeconds(8);

    internal static WebApplication MapAcsRoutes(this WebApplication app)
    {
        // ── ACS Event Webhooks (AllowAnonymous — Event Grid uses its own delivery auth) ───────
        var acsEvents = app.MapGroup("/api/events/acs").AllowAnonymous();

        acsEvents.MapPost("/incoming-call", async (
            HttpContext ctx,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            return await HandleIncomingCallAsync(ctx, loggerFactory.CreateLogger("AcsEndpoints"), ct);
        });
        // Receives mid-call ACS Call Automation events (CallConnected, AddParticipant results, etc.).
        // On CallConnected we AddParticipant the registered rep so their browser rings (Accept gate).
        // Always returns 200 OK so ACS does not retry on transient handler errors.
        acsEvents.MapPost("/callbacks", async (
            HttpContext ctx,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AcsEndpoints");
            await HandleCallbacksAsync(ctx, logger, ct);
            return Results.Ok();
        });

        // ── ACS Media-Stream WebSocket ─────────────────────────────────────────────────────────
        // ACS connects here after AnswerCall. AllowAnonymous — ACS does not send Bearer tokens.
        app.Map("/api/calls/media-stream", async (
            HttpContext ctx,
            AcsAudioSource acsSource,
            ActiveCallStore callStore,
            PipelineCurrentStateStore currentStateStore,
            LiveSentimentStore liveSentiment,
            IHubContext<PipelineHub> hub,
            ILoggerFactory loggerFactory) =>
        {
            await HandleMediaStreamAsync(ctx, acsSource, callStore, currentStateStore, liveSentiment, hub, loggerFactory.CreateLogger("AcsEndpoints"));
        }).AllowAnonymous();

        // ── Active call query — lets a late-joining/reconnecting browser resync state ───────────
        app.MapGet("/api/calls/active", (ActiveCallStore callStore) =>
            Results.Ok(new { callId = callStore.CallId })).AllowAnonymous();

        return app;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // IncomingCall handler
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleIncomingCallAsync(
        HttpContext ctx,
        ILogger logger,
        CancellationToken ct)
    {
        string body;
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            body = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read ACS incoming-call request body.");
            return Results.BadRequest();
        }

        if (string.IsNullOrWhiteSpace(body))
            return Results.BadRequest("Empty request body.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ACS incoming-call request body is not valid JSON.");
            return Results.BadRequest("Invalid JSON.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            // Event Grid delivers events as a JSON array; guard against single-object edge case.
            IEnumerable<JsonElement> events = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : new[] { root };

            foreach (var evt in events)
            {
                if (!evt.TryGetProperty("eventType", out var typeProp))
                    continue;

                var eventType = typeProp.GetString();

                // ── Event Grid Subscription Validation Handshake ──────────────────────────────
                // REQUIRED: Event Grid verifies endpoint ownership before activating delivery.
                if (string.Equals(eventType, "Microsoft.EventGrid.SubscriptionValidationEvent",
                        StringComparison.Ordinal))
                {
                    if (evt.TryGetProperty("data", out var valData) &&
                        valData.TryGetProperty("validationCode", out var valCode))
                    {
                        logger.LogInformation(
                            "Event Grid SubscriptionValidationEvent: echoing validation code.");
                        return Results.Ok(new { validationResponse = valCode.GetString() });
                    }

                    logger.LogWarning("SubscriptionValidationEvent missing data.validationCode.");
                    return Results.BadRequest("Missing validationCode.");
                }

                // ── Incoming Call ─────────────────────────────────────────────────────────────
                if (string.Equals(eventType, "Microsoft.Communication.IncomingCall",
                        StringComparison.Ordinal))
                {
                    // CallAutomationClient is registered only when Acs:Endpoint is configured.
                    // When Mode=Mock or Acs:Endpoint is absent, log and return 200 — don't cause
                    // Event Grid to retry (the call has already rung; a retry won't recover it).
                    var callClient = ctx.RequestServices.GetService<CallAutomationClient>();
                    if (callClient is null)
                    {
                        logger.LogWarning(
                            "IncomingCall received but CallAutomationClient is not registered " +
                            "(Acs:Endpoint not configured). Call not answered. " +
                            "Set Acs__Endpoint + AudioSource__Mode=Acs to enable live answering.");
                        return Results.Ok();
                    }

                    if (!evt.TryGetProperty("data", out var callData) ||
                        !callData.TryGetProperty("incomingCallContext", out var callCtxProp))
                    {
                        logger.LogWarning("IncomingCall event missing data.incomingCallContext; skipping.");
                        continue;
                    }

                    var incomingCallContext = callCtxProp.GetString();
                    if (string.IsNullOrEmpty(incomingCallContext))
                    {
                        logger.LogWarning("IncomingCall event has empty incomingCallContext; skipping.");
                        continue;
                    }

                    var callStore = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    // Single-call POC: atomically claim incoming-call answering so concurrent webhook
                    // deliveries cannot answer multiple calls at once.
                    if (!callStore.TryBeginIncomingClaim())
                    {
                        logger.LogWarning(
                            "Ignoring IncomingCall while active call/claim is already in progress. Active call={ActiveCallId}.",
                            callStore.CallId);
                        return Results.Ok();
                    }

                    // ACA external ingress provides HTTPS/WSS — always use secure schemes.
                    var callbackUri    = new Uri($"https://{ctx.Request.Host}/api/events/acs/callbacks");
                    var mediaStreamUri = new Uri($"wss://{ctx.Request.Host}/api/calls/media-stream");

                    try
                    {
                        // SDK 1.5.1 API: ctor takes (audioChannelType, streamingTransport);
                        // TransportUri and MediaStreamingContent are set as properties.
                        // Unmixed (per-participant audio) so the rep's mic — once they join — can be
                        // dropped from the transcript/sentiment pipeline, keeping the sentiment meter
                        // CUSTOMER-only. Before the rep joins there is only the caller, so behaviour
                        // is identical to the prior Mixed stream.
                        var answerOptions = new AnswerCallOptions(incomingCallContext, callbackUri)
                        {
                            MediaStreamingOptions = new MediaStreamingOptions(
                                MediaStreamingAudioChannel.Unmixed,
                                StreamingTransport.Websocket)
                            {
                                TransportUri           = mediaStreamUri,
                                MediaStreamingContent  = MediaStreamingContent.Audio,
                                StartMediaStreaming    = true  // start streaming when call connects
                            }
                        };

                        var result = await callClient.AnswerCallAsync(answerOptions, ct);

                        // Capture the ACS call connection ID so SpeechTranscriptionService can
                        // route transcript events to the correct SignalR group.
                        var repRegistry = ctx.RequestServices.GetRequiredService<RepRegistry>();
                        var answeredCallId = result.Value.CallConnection.CallConnectionId;
                        callStore.CompleteIncomingClaim(answeredCallId);
                        ctx.RequestServices
                            .GetRequiredService<PipelineCurrentStateStore>()
                            .ResetForCall(answeredCallId);

                        // Broadcast call-started so every console client transitions Disconnected → Connecting
                        // and subscribes to call:{answeredCallId}. Broadcast group == publish group (reviewer fix).
                        var hub = ctx.RequestServices.GetRequiredService<IHubContext<PipelineHub>>();
                        await hub.Clients.All.SendAsync(
                            PipelineContract.StreamNames.CallStarted,
                            new CallLifecycleEvent
                            {
                                CallId = answeredCallId,
                                Status = "started",
                                TimestampUtc = DateTimeOffset.UtcNow
                            },
                            ct);

                        // Best-effort fast path: invite the currently registered rep immediately.
                        // Callback-driven and heartbeat-driven paths remain as convergent retries.
                        if (!string.IsNullOrWhiteSpace(repRegistry.CurrentUserId))
                        {
                            await RepEndpoints.TryAddRepToCallAsync(
                                callClient,
                                callStore,
                                repRegistry,
                                logger,
                                ct);
                        }

                        logger.LogInformation(
                            "ACS call answered; callId={CallId} media streaming directed to {MediaUri}",
                            answeredCallId, mediaStreamUri);
                    }
                    catch (Exception ex)
                    {
                        callStore.CancelIncomingClaim();
                        // Log and return 200 — retrying the IncomingCall event won't recover a
                        // missed call. Investigate in logs then restart the call for the demo.
                        logger.LogError(ex, "Failed to answer ACS incoming call.");
                    }

                    return Results.Ok();
                }

                logger.LogDebug("Unhandled ACS event type '{EventType}'; ignoring.", eventType);
            }
        }

        return Results.Ok();
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Mid-call callbacks handler (CallConnected → AddParticipant the rep)
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task HandleCallbacksAsync(HttpContext ctx, ILogger logger, CancellationToken ct)
    {
        string body;
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            body = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ACS callback body; ignoring.");
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
            return;

        CallAutomationEventBase[] events;
        try
        {
            events = CallAutomationEventParser.ParseMany(BinaryData.FromString(body));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse ACS callback events; ignoring.");
            return;
        }

        foreach (var evt in events)
        {
            switch (evt)
            {
                case CallConnected connected:
                    logger.LogInformation(
                        "ACS CallConnected for call {CallId}; attempting to add registered rep.",
                        connected.CallConnectionId);

                    var callClient = ctx.RequestServices.GetService<CallAutomationClient>();
                    var callStore = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    var registry = ctx.RequestServices.GetRequiredService<RepRegistry>();

                    if (callClient is null)
                    {
                        logger.LogWarning("CallConnected received but CallAutomationClient is not registered.");
                        break;
                    }

                    if (callStore.TrySetCallIdIfEmpty(connected.CallConnectionId))
                    {
                        ctx.RequestServices
                            .GetRequiredService<PipelineCurrentStateStore>()
                            .ResetForCall(connected.CallConnectionId);
                    }

                    var trackedCallId = callStore.CallId;
                    if (string.IsNullOrWhiteSpace(trackedCallId))
                    {
                        logger.LogInformation(
                            "Deferring CallConnected processing for call {CallbackCallId} while incoming call claim is still in progress.",
                            connected.CallConnectionId);
                        break;
                    }

                    if (!string.Equals(trackedCallId, connected.CallConnectionId, StringComparison.Ordinal))
                    {
                        logger.LogWarning(
                            "Ignoring stale CallConnected callback for call {CallbackCallId}; active tracked call is {TrackedCallId}.",
                            connected.CallConnectionId,
                            trackedCallId);
                        break;
                    }

                    if (string.IsNullOrEmpty(registry.CurrentUserId))
                    {
                        // No rep ready yet — leave the add PENDING. The next /register reconverges
                        // and adds the rep (bounded by the AddParticipant invitation timeout).
                        logger.LogInformation(
                            "CallConnected but no rep registered yet; deferring AddParticipant until /register.");
                        break;
                    }

                    await RepEndpoints.TryAddRepToCallAsync(callClient, callStore, registry, logger, ct);
                    break;

                case CallDisconnected disconnected:
                    await HandleCallDisconnectedAsync(ctx.RequestServices, disconnected.CallConnectionId, logger, ct);
                    break;

                case AddParticipantSucceeded ok:
                    if (string.Equals(
                            ctx.RequestServices.GetRequiredService<ActiveCallStore>().CallId,
                            ok.CallConnectionId,
                            StringComparison.Ordinal))
                    {
                        logger.LogInformation(
                            "ACS AddParticipant succeeded (rep answered) for call {CallId}.", ok.CallConnectionId);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Ignoring AddParticipantSucceeded for stale call {CallbackCallId}; active tracked call is {TrackedCallId}.",
                            ok.CallConnectionId,
                            ctx.RequestServices.GetRequiredService<ActiveCallStore>().CallId ?? "(none)");
                    }
                    break;

                case AddParticipantFailed failed:
                    // Rep declined / didn't answer in time / error → release the claim so a later
                    // /register (e.g., rep retries) can re-invite within the call's lifetime.
                    var activeStore = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    if (string.Equals(activeStore.CallId, failed.CallConnectionId, StringComparison.Ordinal))
                    {
                        logger.LogWarning(
                            "ACS AddParticipant failed for call {CallId}: {Code} {Message}",
                            failed.CallConnectionId, failed.ResultInformation?.Code, failed.ResultInformation?.Message);
                        activeStore.ResetAddRep();
                    }
                    else
                    {
                        logger.LogWarning(
                            "Ignoring AddParticipantFailed for stale call {CallbackCallId}; active tracked call is {TrackedCallId}.",
                            failed.CallConnectionId,
                            activeStore.CallId ?? "(none)");
                    }
                    break;

                default:
                    logger.LogDebug("Unhandled ACS callback event '{Type}'.", evt.GetType().Name);
                    break;
            }
        }
    }

    private static async Task HandleCallDisconnectedAsync(
        IServiceProvider services,
        string? disconnectedCallId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var callStore = services.GetRequiredService<ActiveCallStore>();
        var trackedCallId = callStore.CallId;
        var endedCallId = string.IsNullOrWhiteSpace(disconnectedCallId) ? trackedCallId : disconnectedCallId;
        var matchesTrackedCall = !string.IsNullOrWhiteSpace(trackedCallId) &&
            (string.IsNullOrWhiteSpace(disconnectedCallId) ||
             string.Equals(trackedCallId, disconnectedCallId, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(endedCallId))
        {
            var hub = services.GetRequiredService<IHubContext<PipelineHub>>();
            await hub.Clients.All.SendAsync(
                PipelineContract.StreamNames.CallEnded,
                new CallLifecycleEvent
                {
                    CallId = endedCallId,
                    Status = "ended",
                    TimestampUtc = DateTimeOffset.UtcNow
                },
                cancellationToken);
        }

        logger.LogInformation(
            "ACS CallDisconnected observed for call {CallId}; awaiting media-stream close to finalize call state (matchedTrackedCall={MatchedTrackedCall}).",
            endedCallId ?? "(unknown)",
            matchesTrackedCall);

        if (matchesTrackedCall && !string.IsNullOrWhiteSpace(endedCallId))
        {
            var delayedCallStore = services.GetRequiredService<ActiveCallStore>();
            var delayedAudioSource = services.GetRequiredService<AcsAudioSource>();
            var delayedCurrentStateStore = services.GetRequiredService<PipelineCurrentStateStore>();
            var delayedLiveSentimentStore = services.GetRequiredService<LiveSentimentStore>();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DisconnectedCleanupDelay, CancellationToken.None);

                    if (!string.Equals(delayedCallStore.CallId, endedCallId, StringComparison.Ordinal))
                    {
                        return; // call already cleared or replaced by a newer call
                    }

                    delayedAudioSource.CompleteStream();
                    delayedCallStore.Clear();
                    delayedCurrentStateStore.ClearLiveState();
                    delayedLiveSentimentStore.Clear();

                    logger.LogWarning(
                        "Forced delayed cleanup for disconnected call {CallId} after media-stream close was not observed within {Seconds}s.",
                        endedCallId,
                        DisconnectedCleanupDelay.TotalSeconds);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Delayed cleanup failed for disconnected call {CallId}.",
                        endedCallId);
                }
            });
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Media-stream WebSocket handler
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task HandleMediaStreamAsync(
        HttpContext ctx,
        AcsAudioSource acsSource,
        ActiveCallStore callStore,
        PipelineCurrentStateStore currentStateStore,
        LiveSentimentStore liveSentiment,
        IHubContext<PipelineHub> hub,
        ILogger logger)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("This endpoint accepts WebSocket connections only.");
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        logger.LogInformation("ACS media-stream WebSocket connection established.");

        // Start a fresh per-call audio session so the transcription consumer builds a new
        // recognizer for this call (and stays alive for the next one after it ends).
        acsSource.BeginSession();

        // Start a clean rolling-sentiment session for this call so the meter resets to
        // "Waiting for sentiment" and then tracks the new conversation.
        liveSentiment.Reset(callStore.CallId);

        var buffer = new byte[8192]; // ACS sends 640-byte frames; 8 KB covers a few frames per read.
        using var ms = new MemoryStream();

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                // Accumulate a complete (possibly fragmented) WebSocket message.
                ms.SetLength(0);
                WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ctx.RequestAborted);

                    if (receiveResult.Count > 0)
                        ms.Write(buffer, 0, receiveResult.Count);
                }
                while (!receiveResult.EndOfMessage && ws.State == WebSocketState.Open);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    // Acknowledge the close handshake.
                    if (ws.State == WebSocketState.CloseReceived)
                    {
                        await ws.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }
                    logger.LogInformation("ACS media-stream WebSocket closed gracefully.");
                    break;
                }

                if (receiveResult.MessageType == WebSocketMessageType.Text && ms.Length > 0)
                {
                    // AcsAudioSource parses the JSON, decodes the PCM payload, and writes
                    // an AudioFrame to its internal Channel. Malformed frames are skipped.
                    await acsSource.HandleWebSocketMessageAsync(ms.ToArray(), ctx.RequestAborted);
                }
            }
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("ACS media-stream request cancelled (host shutting down or client disconnected).");
        }
        catch (WebSocketException ex)
        {
            // A dropped/malformed stream is a cardinal sin — always log it prominently.
            logger.LogWarning(
                "ACS media-stream WebSocket error (code={Code}): {Message}",
                ex.WebSocketErrorCode, ex.Message);
        }
        finally
        {
            // Capture the callId BEFORE Clear() so the CallEnded broadcast carries it (reviewer fix).
            var endedCallId = callStore.CallId;

            // Signal end-of-stream to all ReadAsync consumers regardless of how the loop ended.
            acsSource.CompleteStream();
            callStore.Clear();  // call is over; new calls start fresh
            currentStateStore.ClearLiveState();  // clear replay/current-state history between calls
            liveSentiment.Clear();  // drop rolling sentiment so the panel returns to waiting

            if (!string.IsNullOrEmpty(endedCallId))
            {
                // Broadcast call-ended so every console client transitions back to Disconnected.
                await hub.Clients.All.SendAsync(
                    PipelineContract.StreamNames.CallEnded,
                    new CallLifecycleEvent
                    {
                        CallId = endedCallId,
                        Status = "ended",
                        TimestampUtc = DateTimeOffset.UtcNow
                    },
                    CancellationToken.None);
            }

            logger.LogInformation("ACS media-stream handler ended; audio Channel completed.");
        }
    }
}
