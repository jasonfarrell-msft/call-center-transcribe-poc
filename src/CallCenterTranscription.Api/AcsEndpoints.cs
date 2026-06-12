using Azure.Communication;
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
            LiveSentimentStore liveSentiment,
            IHubContext<PipelineHub> hub,
            ILoggerFactory loggerFactory) =>
        {
            await HandleMediaStreamAsync(ctx, acsSource, callStore, liveSentiment, hub, loggerFactory.CreateLogger("AcsEndpoints"));
        }).AllowAnonymous();

        // ── Active call query — lets a late-joining/reconnecting browser resync state ───────────
        var activeCallRoute = app.MapGet("/api/calls/active", (PipelineCurrentStateStore currentStateStore) =>
            Results.Ok(currentStateStore.GetActiveCall()));
        if (app.Configuration.GetValue<bool>("Security:RequireAuth"))
        {
            activeCallRoute.RequireAuthorization("AgentAssistAccess");
        }
        else
        {
            activeCallRoute.AllowAnonymous();
        }

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
                    var liveAcsMode = string.Equals(
                        ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue<string>("AudioSource:Mode"),
                        "Acs",
                        StringComparison.OrdinalIgnoreCase);
                    if (!liveAcsMode)
                    {
                        logger.LogInformation(
                            "IncomingCall ignored because AudioSource:Mode is not Acs; scripted/mock demo remains authoritative.");
                        return Results.Ok();
                    }

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
                    if (!string.IsNullOrWhiteSpace(callStore.CallId))
                    {
                        logger.LogInformation(
                            "IncomingCall ignored because call {CallId} is already active.",
                            callStore.CallId);
                        return Results.Ok();
                    }

                    if (!callStore.TryBeginIncomingClaim())
                    {
                        logger.LogInformation("IncomingCall ignored because another call answer claim is already in progress.");
                        return Results.Ok();
                    }

                    var publicBaseUri = ResolvePublicBaseUri(
                        ctx,
                        ctx.RequestServices.GetRequiredService<IConfiguration>(),
                        logger);
                    var callbackUri = BuildPublicUri(publicBaseUri, ctx.Request.PathBase, "/api/events/acs/callbacks");
                    var mediaStreamUri = BuildPublicUri(publicBaseUri, ctx.Request.PathBase, "/api/calls/media-stream", "wss");
                    logger.LogInformation(
                        "IncomingCall received from {From} to {To}; answering via callback {CallbackUri} and media stream {MediaUri}.",
                        DescribeIdentifier(callData, "from"),
                        DescribeIdentifier(callData, "to"),
                        callbackUri,
                        mediaStreamUri);

                    try
                    {
                        // SDK 1.5.1 API: ctor takes (audioChannelType, streamingTransport);
                        // TransportUri and MediaStreamingContent are set as properties.
                        // Keep Mixed audio so both customer (PSTN) and rep (CommunicationUser)
                        // are combined into one stream for the single Speech recognizer pipeline.
                        var answerOptions = new AnswerCallOptions(incomingCallContext, callbackUri)
                        {
                            MediaStreamingOptions = new MediaStreamingOptions(
                                MediaStreamingAudioChannel.Mixed,
                                StreamingTransport.Websocket)
                            {
                                TransportUri           = mediaStreamUri,
                                MediaStreamingContent  = MediaStreamingContent.Audio,
                                AudioFormat            = AudioFormat.Pcm16KMono,
                                StartMediaStreaming    = true  // start streaming when call connects
                            }
                        };

                        var result = await callClient.AnswerCallAsync(answerOptions, ct);

                        // Capture the ACS call connection ID so SpeechTranscriptionService can
                        // route transcript events to the correct SignalR group.
                        var answeredCallId = result.Value.CallConnection.CallConnectionId;
                        callStore.CompleteIncomingClaim(answeredCallId);

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

    // Broadcasts stream.callPending once ACS has created a real rep invitation (AddParticipant
    // accepted the invite request). This keeps the visible Accept state correlated to an actual
    // browser-routable ACS incoming-call leg instead of a synthetic "answered server-side only"
    // placeholder that the rep cannot accept yet.
    internal static async Task EmitCallPendingAsync(
        string callId,
        IClientProxy broadcastClient,
        CancellationToken ct,
        DateTimeOffset? timestampUtc = null)
    {
        await broadcastClient.SendAsync(
            PipelineContract.StreamNames.CallPending,
            new CallLifecycleEvent
            {
                CallId = callId,
                Status = "pending",
                TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow
            },
            ct);
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Mid-call callbacks handler (CallConnected fallback reconverge for rep AddParticipant)
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

                    if (!IsCurrentActiveCall(callStore.CallId, connected.CallConnectionId))
                    {
                        logger.LogInformation(
                            "Ignoring CallConnected for stale call {CallbackCallId}; active call is {ActiveCallId}.",
                            connected.CallConnectionId,
                            callStore.CallId ?? "<none>");
                        break;
                    }

                    callStore.MarkConnected(connected.CallConnectionId);

                    if (callClient is null)
                    {
                        logger.LogWarning("CallConnected received but CallAutomationClient is not registered.");
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

                    var invitedCallId = await RepEndpoints.TryAddRepToCallAsync(callClient, callStore, registry, logger, ct);
                    if (!string.IsNullOrWhiteSpace(invitedCallId))
                    {
                        ctx.RequestServices.GetRequiredService<PipelineCurrentStateStore>()
                            .MarkPending(invitedCallId);
                        var connectedHub = ctx.RequestServices.GetRequiredService<IHubContext<PipelineHub>>();
                        await EmitCallPendingAsync(
                            invitedCallId,
                            connectedHub.Clients.All,
                            ct);
                    }
                    break;

                case AddParticipantSucceeded ok:
                    // Rep clicked Accept on their softphone — ACS confirmed the join.
                    // Mark the store so Lacus can read RepAccepted, and broadcast CallAccepted
                    // so the UI transitions Pending → Live and begins showing transcript lines.
                    var acceptedStore = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    if (!IsCurrentActiveCall(acceptedStore.CallId, ok.CallConnectionId))
                    {
                        logger.LogInformation(
                            "Ignoring AddParticipantSucceeded for stale call {CallbackCallId}; active call is {ActiveCallId}.",
                            ok.CallConnectionId,
                            acceptedStore.CallId ?? "<none>");
                        break;
                    }

                    logger.LogInformation(
                        "ACS AddParticipant succeeded (rep answered) for call {CallId}.", ok.CallConnectionId);
                    acceptedStore.MarkAccepted(ok.CallConnectionId);
                    ctx.RequestServices.GetRequiredService<PipelineCurrentStateStore>().MarkAccepted(ok.CallConnectionId);
                    var acceptedHub = ctx.RequestServices.GetRequiredService<IHubContext<PipelineHub>>();
                    await acceptedHub.Clients.All.SendAsync(
                        PipelineContract.StreamNames.CallAccepted,
                        new CallLifecycleEvent
                        {
                            CallId = ok.CallConnectionId,
                            Status = "accepted",
                            TimestampUtc = DateTimeOffset.UtcNow
                        },
                        ct);
                    break;

                case AddParticipantFailed failed:
                    // Rep declined / timed out → FULL TEARDOWN.
                    // Hang up the ACS call so the customer leg drops and the media-stream
                    // WebSocket closes. The existing finally-block in HandleMediaStreamAsync
                    // then runs: CompleteStream → callStore.Clear → liveSentiment.Clear → CallEnded.
                    var failedStore = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    var isActiveCallFailure = IsCurrentActiveCall(failedStore.CallId, failed.CallConnectionId);

                    logger.LogWarning(
                        "ACS AddParticipant failed for call {CallId}: {Code} {Message} — hanging up.",
                        failed.CallConnectionId, failed.ResultInformation?.Code, failed.ResultInformation?.Message);
                    if (!isActiveCallFailure)
                    {
                        logger.LogInformation(
                            "Skipping rep-add reset for stale AddParticipantFailed callback call {CallbackCallId}; active call is {ActiveCallId}.",
                            failed.CallConnectionId,
                            failedStore.CallId ?? "<none>");
                    }

                    var failedCallClient = ctx.RequestServices.GetService<CallAutomationClient>();
                    if (failedCallClient is not null && !string.IsNullOrEmpty(failed.CallConnectionId))
                    {
                        try
                        {
                            await failedCallClient.GetCallConnection(failed.CallConnectionId)
                                .HangUpAsync(forEveryone: true, ct);
                            logger.LogInformation(
                                "Hung up call {CallId} after rep decline/timeout; media stream will close and teardown will fire.",
                                failed.CallConnectionId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "Failed to hang up call {CallId} after AddParticipantFailed; customer may remain connected.",
                                failed.CallConnectionId);
                        }
                    }
                    break;

                case ParticipantsUpdated updated:
                    // When the PSTN customer hangs up while the rep is still in the call, ACS
                    // fires ParticipantsUpdated with a list that no longer includes a
                    // PhoneNumberIdentifier. The call stays alive on the rep's VoIP leg, so
                    // CallDisconnected does NOT fire yet. Hang up for everyone here to trigger
                    // the media-stream WebSocket close → finally-block teardown path.
                    if (updated.Participants.Count > 0 &&
                        !updated.Participants.Any(p => p.Identifier is PhoneNumberIdentifier))
                    {
                        var storeForPU = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                        var callIdForPU = storeForPU.CallId;
                        if (!IsCurrentActiveCall(callIdForPU, updated.CallConnectionId))
                        {
                            logger.LogInformation(
                                "Ignoring ParticipantsUpdated for stale call {CallbackCallId}; active call is {ActiveCallId}.",
                                updated.CallConnectionId,
                                callIdForPU ?? "<none>");
                        }
                        else if (!string.IsNullOrEmpty(callIdForPU))
                        {
                            logger.LogInformation(
                                "ParticipantsUpdated: no PSTN party in call {CallId} — customer hung up; " +
                                "hanging up for everyone to trigger full teardown.",
                                callIdForPU);
                            var hangupClient = ctx.RequestServices.GetService<CallAutomationClient>();
                            if (hangupClient is not null)
                            {
                                try
                                {
                                    await hangupClient.GetCallConnection(callIdForPU)
                                        .HangUpAsync(forEveryone: true, ct);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex,
                                        "Failed to hang up call {CallId} after PSTN party left.",
                                        callIdForPU);
                                }
                            }
                        }
                    }
                    else
                    {
                        logger.LogDebug(
                            "ParticipantsUpdated: {Count} participants; PSTN party still present or list empty — no action.",
                            updated.Participants.Count);
                    }
                    break;

                case CallDisconnected disconnected:
                    // Belt-and-suspenders: the call ended at the ACS platform level.
                    // Normally the media-stream WebSocket close fires first (triggering the
                    // finally-block teardown). This handler covers the case where the WebSocket
                    // close is delayed or never arrives cleanly (e.g., abrupt PSTN drop with
                    // no prior ParticipantsUpdated handling).
                    //
                    // TryBeginTeardown() is an atomic claim — exactly ONE path (this callback OR
                    // the WebSocket finally-block) wins the claim and runs the full teardown;
                    // the other path is a no-op. Clear() resets the claim for the next call.
                    logger.LogInformation(
                        "ACS CallDisconnected for call {CallId}.",
                        disconnected.CallConnectionId);
                    var storeForDisc = ctx.RequestServices.GetRequiredService<ActiveCallStore>();
                    if (!IsCurrentActiveCall(storeForDisc.CallId, disconnected.CallConnectionId))
                    {
                        logger.LogInformation(
                            "Ignoring CallDisconnected for stale call {CallbackCallId}; active call is {ActiveCallId}.",
                            disconnected.CallConnectionId,
                            storeForDisc.CallId ?? "<none>");
                        break;
                    }

                    if (!storeForDisc.TryBeginTeardown(disconnected.CallConnectionId))
                    {
                        logger.LogDebug(
                            "CallDisconnected: teardown already claimed (WebSocket finally ran first); no-op.");
                        break;
                    }
                    var callIdForDisc = storeForDisc.CallId;
                    if (string.IsNullOrEmpty(callIdForDisc))
                    {
                        // No active call to tear down (store was already cleared, e.g. the
                        // WebSocket closed before the claim check above).
                        break;
                    }
                    var acsSourceForDisc = ctx.RequestServices.GetRequiredService<AcsAudioSource>();
                    acsSourceForDisc.ForceCompleteCurrentSession();
                    ctx.RequestServices.GetRequiredService<PipelineCurrentStateStore>().ClearActiveCall(callIdForDisc);
                    storeForDisc.Clear(callIdForDisc);
                    ctx.RequestServices.GetRequiredService<LiveSentimentStore>().Clear();
                    var hubForDisc = ctx.RequestServices.GetRequiredService<IHubContext<PipelineHub>>();
                    await hubForDisc.Clients.All.SendAsync(
                        PipelineContract.StreamNames.CallEnded,
                        new CallLifecycleEvent
                        {
                            CallId = callIdForDisc,
                            Status = "ended",
                            TimestampUtc = DateTimeOffset.UtcNow
                        },
                        ct);
                    logger.LogInformation(
                        "CallDisconnected: full teardown complete for call {CallId}.", callIdForDisc);
                    break;

                default:
                    logger.LogDebug("Unhandled ACS callback event '{Type}'.", evt.GetType().Name);
                    break;
            }
        }
    }

    internal static bool IsCurrentActiveCall(string? activeCallId, string? callbackCallId) =>
        !string.IsNullOrEmpty(activeCallId) &&
        !string.IsNullOrEmpty(callbackCallId) &&
        string.Equals(activeCallId, callbackCallId, StringComparison.Ordinal);

    internal static Uri ResolvePublicBaseUri(HttpContext ctx, IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["Acs:PublicBaseUrl"];
        Uri? baseUri = null;
        if (!string.IsNullOrWhiteSpace(configured) &&
            Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
        {
            baseUri = configuredUri;
        }

        if (baseUri is null)
        {
            if (!ctx.Request.Host.HasValue)
            {
                throw new InvalidOperationException(
                    "Cannot resolve ACS public base URI. Configure Acs:PublicBaseUrl or ensure the inbound request includes a Host header.");
            }

            baseUri = new UriBuilder(Uri.UriSchemeHttps, ctx.Request.Host.Host, ctx.Request.Host.Port ?? -1).Uri;
        }

        if (string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(baseUri.Host, "::1", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "ACS public base URI resolved to {BaseUri}. ACS callbacks and media streaming require a publicly reachable HTTPS/WSS host. Configure Acs:PublicBaseUrl for live Azure-resource demos.",
                baseUri);
        }

        return new UriBuilder(Uri.UriSchemeHttps, baseUri.Host, baseUri.IsDefaultPort ? -1 : baseUri.Port)
        {
            Path = NormalizeBasePath(baseUri.AbsolutePath)
        }.Uri;
    }

    internal static Uri BuildPublicUri(Uri baseUri, PathString pathBase, string relativePath, string scheme = "https")
    {
        var configuredBasePath = NormalizeBasePath(baseUri.AbsolutePath);
        var requestBasePath = NormalizeBasePath(pathBase.Value);
        var combinedBasePath = configuredBasePath;
        if (!string.IsNullOrEmpty(requestBasePath) &&
            !string.Equals(requestBasePath, configuredBasePath, StringComparison.OrdinalIgnoreCase))
        {
            combinedBasePath = $"{configuredBasePath}{requestBasePath}";
        }

        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
            Path = $"{combinedBasePath}{relativePath}"
        };
        return builder.Uri;
    }

    private static string DescribeIdentifier(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return "<unknown>";
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return MaskValue(property.GetString());
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            if (property.TryGetProperty("rawId", out var rawId) && rawId.ValueKind == JsonValueKind.String)
            {
                return $"raw:{MaskValue(rawId.GetString())}";
            }

            if (property.TryGetProperty("phoneNumber", out var phoneNumber))
            {
                if (phoneNumber.ValueKind == JsonValueKind.String)
                {
                    return $"phone:{MaskValue(phoneNumber.GetString())}";
                }

                if (phoneNumber.ValueKind == JsonValueKind.Object &&
                    phoneNumber.TryGetProperty("value", out var phoneValue) &&
                    phoneValue.ValueKind == JsonValueKind.String)
                {
                    return $"phone:{MaskValue(phoneValue.GetString())}";
                }
            }

            if (property.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
            {
                return kind.GetString() ?? "<unknown>";
            }
        }

        return property.ToString();
    }

    private static string NormalizeBasePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return string.Empty;
        }

        return path.TrimEnd('/');
    }

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<unknown>";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 4 ? "****" : $"***{trimmed[^4..]}";
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Media-stream WebSocket handler
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task HandleMediaStreamAsync(
        HttpContext ctx,
        AcsAudioSource acsSource,
        ActiveCallStore callStore,
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

        if (!callStore.TryBeginMediaClaim())
        {
            logger.LogWarning("ACS media-stream WebSocket rejected: another media stream is already active.");
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsync("Media stream is already active.");
            return;
        }

        WebSocket? ws = null;
        AcsAudioSource.Session? audioSession = null;
        string? mediaSessionCallId = null;
        var buffer = new byte[8192]; // ACS sends 640-byte frames; 8 KB covers a few frames per read.
        using var ms = new MemoryStream();

        try
        {
            ws = await ctx.WebSockets.AcceptWebSocketAsync();
            logger.LogInformation("ACS media-stream WebSocket connection established.");

            // Start a fresh per-call audio session so the transcription consumer builds a new
            // recognizer for this call (and stays alive for the next one after it ends).
            audioSession = acsSource.BeginSession();
            mediaSessionCallId = callStore.CallId;

            // Start a clean rolling-sentiment session for this call so the meter resets to
            // "Waiting for sentiment" and then tracks the new conversation.
            liveSentiment.Reset(callStore.CallId);

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
                    await acsSource.HandleWebSocketMessageAsync(audioSession, ms.ToArray(), ctx.RequestAborted);
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
            // TryBeginTeardown() is atomic: exactly ONE path (this finally-block OR the
            // CallDisconnected callback) wins the claim and runs the full teardown.
            // The loser path still cleans up the audio session (idempotent) and always
            // releases the media claim so the next call can start.
            if (callStore.TryBeginTeardown(mediaSessionCallId))
            {
                // Capture the callId BEFORE Clear() so the CallEnded broadcast carries it.
                var endedCallId = mediaSessionCallId;

                // Signal end-of-stream to all ReadAsync consumers regardless of how the loop ended.
                if (audioSession is not null)
                    acsSource.CompleteStream(audioSession);
                callStore.Clear(endedCallId);
                liveSentiment.Clear();

                if (!string.IsNullOrEmpty(endedCallId))
                {
                    ctx.RequestServices.GetRequiredService<PipelineCurrentStateStore>().ClearActiveCall(endedCallId);
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
            }
            else
            {
                // CallDisconnected callback already ran the full teardown (broadcast + Clear).
                // Still complete the audio session (TryComplete is idempotent) so the
                // transcription consumer doesn't hang waiting for frames.
                if (audioSession is not null)
                    acsSource.CompleteStream(audioSession);
                logger.LogInformation(
                    "ACS media-stream WebSocket closed; teardown already claimed by CallDisconnected callback.");
            }

            logger.LogInformation("ACS media-stream handler ended; audio Channel completed.");
            callStore.EndMediaClaim();
        }
    }
}
