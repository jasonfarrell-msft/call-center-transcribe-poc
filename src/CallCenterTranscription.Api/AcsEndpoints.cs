using Azure.Communication.CallAutomation;
using CallCenterTranscription.Telephony;
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

        // Receives mid-call ACS events (CallConnected, MediaStreamingStarted, etc.).
        // Returns 200 OK; full event dispatch is out of scope for this round.
        acsEvents.MapPost("/callbacks", (ILoggerFactory loggerFactory) =>
        {
            loggerFactory.CreateLogger("AcsEndpoints").LogDebug("ACS mid-call callback received.");
            return Results.Ok();
        });

        // ── ACS Media-Stream WebSocket ─────────────────────────────────────────────────────────
        // ACS connects here after AnswerCall. AllowAnonymous — ACS does not send Bearer tokens.
        app.Map("/api/calls/media-stream", async (
            HttpContext ctx,
            AcsAudioSource acsSource,
            ILoggerFactory loggerFactory) =>
        {
            await HandleMediaStreamAsync(ctx, acsSource, loggerFactory.CreateLogger("AcsEndpoints"));
        }).AllowAnonymous();

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

                    // ACA external ingress provides HTTPS/WSS — always use secure schemes.
                    var callbackUri    = new Uri($"https://{ctx.Request.Host}/api/events/acs/callbacks");
                    var mediaStreamUri = new Uri($"wss://{ctx.Request.Host}/api/calls/media-stream");

                    try
                    {
                        // SDK 1.5.1 API: ctor takes (audioChannelType, streamingTransport);
                        // TransportUri and MediaStreamingContent are set as properties.
                        var answerOptions = new AnswerCallOptions(incomingCallContext, callbackUri)
                        {
                            MediaStreamingOptions = new MediaStreamingOptions(
                                MediaStreamingAudioChannel.Mixed,
                                StreamingTransport.Websocket)
                            {
                                TransportUri           = mediaStreamUri,
                                MediaStreamingContent  = MediaStreamingContent.Audio,
                                StartMediaStreaming    = true  // start streaming when call connects
                            }
                        };

                        await callClient.AnswerCallAsync(answerOptions, ct);

                        logger.LogInformation(
                            "ACS call answered; media streaming directed to {MediaUri}", mediaStreamUri);
                    }
                    catch (Exception ex)
                    {
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
    // Media-stream WebSocket handler
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task HandleMediaStreamAsync(
        HttpContext ctx,
        AcsAudioSource acsSource,
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
            // Signal end-of-stream to all ReadAsync consumers regardless of how the loop ended.
            acsSource.CompleteStream();
            logger.LogInformation("ACS media-stream handler ended; audio Channel completed.");
        }
    }
}
