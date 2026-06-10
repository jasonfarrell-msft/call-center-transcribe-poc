using Azure.Communication;
using Azure.Communication.CallAutomation;
using CallCenterTranscription.Api.Services;

namespace CallCenterTranscription.Api;

/// <summary>
/// Maps the rep-softphone control-plane endpoints that let the rep's browser become a real ACS
/// call participant (local mic/speakers) with an explicit Accept gate:
///
///   GET  /api/rep/token?userId=   → mints a short-lived VoIP token (creates the identity if the
///                                   browser has none yet). Used for the SDK tokenRefresher too.
///   POST /api/rep/register        → records the browser's ACS userId as the rep ready to take a
///                                   call. If a call is already waiting (answered but no rep yet),
///                                   the rep is AddParticipant-ed immediately (pending reconverge).
///
/// Both are AllowAnonymous at the routing layer (Event-Grid/media routes share this posture) and
/// are reached via the Web app's server-to-server PROXY which injects a shared secret header so the
/// browser never holds it. When <c>Rep:AccessKey</c> is configured, the header is REQUIRED.
///
/// AddParticipant pattern: adding a VoIP CommunicationUser participant to a Call-Automation-answered
/// PSTN call makes ACS server-mix a true two-way caller↔rep audio bridge. The browser's native
/// incomingCall event (for this added leg) is the Accept gate. Invitation timeout is the Accept
/// budget; the caller is already answered (on hold), so the inbound PSTN leg won't drop meanwhile.
/// </summary>
internal static class RepEndpoints
{
    private const string AccessKeyHeader = "X-Rep-Key";

    /// <summary>Default AddParticipant invitation timeout — the rep's Accept budget.</summary>
    private const int InvitationTimeoutSeconds = 60;

    internal static WebApplication MapRepRoutes(this WebApplication app)
    {
        var rep = app.MapGroup("/api/rep").AllowAnonymous();

        rep.MapGet("/token", async (
            HttpContext ctx,
            IConfiguration config,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("RepEndpoints");
            if (!IsAuthorized(ctx, config))
                return Results.Unauthorized();

            var identity = ctx.RequestServices.GetService<RepIdentityService>();
            if (identity is null)
            {
                logger.LogWarning(
                    "Rep token requested but RepIdentityService is not registered (Acs:Endpoint not " +
                    "configured). Set Acs__Endpoint to enable the rep softphone.");
                return Results.Problem(
                    "Rep softphone is not configured (ACS endpoint missing).",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var userId = ctx.Request.Query["userId"].ToString();
            try
            {
                var token = await identity.IssueTokenAsync(userId, ct);
                return Results.Ok(new
                {
                    userId = token.UserId,
                    token = token.Token,
                    expiresOn = token.ExpiresOnUtc
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to issue rep VoIP token.");
                return Results.Problem("Failed to issue rep token.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        rep.MapPost("/register", async (
            HttpContext ctx,
            IConfiguration config,
            RepRegistry registry,
            ActiveCallStore callStore,
            ILoggerFactory loggerFactory,
            RepRegisterRequest? body,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("RepEndpoints");
            if (!IsAuthorized(ctx, config))
                return Results.Unauthorized();

            if (body is null || string.IsNullOrWhiteSpace(body.UserId))
                return Results.BadRequest(new { error = "userId is required." });

            registry.Register(body.UserId);
            logger.LogInformation("Rep registered: userId={UserId}.", body.UserId);

            // Pending-add reconverge: if a call is already answered but the rep wasn't added yet
            // (rep registered after CallConnected, or after an API restart), add them now.
            var callClient = ctx.RequestServices.GetService<CallAutomationClient>();
            if (callClient is not null && !string.IsNullOrEmpty(callStore.CallId) && !callStore.RepAdded)
            {
                await TryAddRepToCallAsync(callClient, callStore, registry, logger, ct);
            }

            return Results.Ok(new { registered = true, callActive = !string.IsNullOrEmpty(callStore.CallId) });
        });

        // ── Rep-initiated full teardown ──────────────────────────────────────────────────────────
        // Called by the rep's browser after currentCall.hangUp() so the entire ACS call is
        // terminated (forEveryone=true), not just the rep's leg. This closes the media-stream
        // WebSocket, which triggers the existing finally-block teardown in AcsEndpoints:
        //   CompleteStream → callStore.Clear → liveSentiment.Clear → CallEnded broadcast.
        rep.MapPost("/hangup", async (
            HttpContext ctx,
            IConfiguration config,
            ActiveCallStore callStore,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("RepEndpoints");
            if (!IsAuthorized(ctx, config))
                return Results.Unauthorized();

            var callId = callStore.CallId;
            if (string.IsNullOrEmpty(callId))
                return Results.Ok(new { hungUp = false, reason = "no-active-call" });

            var callClient = ctx.RequestServices.GetService<CallAutomationClient>();
            if (callClient is null)
                return Results.Ok(new { hungUp = false, reason = "no-client" });

            try
            {
                await callClient.GetCallConnection(callId).HangUpAsync(forEveryone: true, ct);
                logger.LogInformation("Rep-initiated HangUp for call {CallId}.", callId);
                return Results.Ok(new { hungUp = true });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to HangUp call {CallId} on rep request.", callId);
                return Results.Problem("HangUp failed.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return app;
    }

    /// <summary>
    /// Adds the currently registered rep's VoIP identity to the active call as a participant,
    /// EXACTLY ONCE. Safe to call from both the CallConnected callback and /register: the
    /// ActiveCallStore state machine guarantees a single in-flight attempt; a failed attempt is
    /// released so the next /register heartbeat retries it.
    /// </summary>
    internal static async Task TryAddRepToCallAsync(
        CallAutomationClient callClient,
        ActiveCallStore callStore,
        RepRegistry registry,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(callStore.CallId) || string.IsNullOrEmpty(registry.CurrentUserId))
            return;

        if (!callStore.TryBeginAddRep())
            return; // another path is adding / already added

        // Re-snapshot INSIDE the claim: a concurrent Clear() (call ended) between the pre-check
        // and here would leave CallId null. Bail and release the claim so we never invite a rep
        // to a call that has already ended.
        var callId = callStore.CallId;
        var repUserId = registry.CurrentUserId;
        if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(repUserId))
        {
            callStore.ResetAddRep();
            return;
        }

        try
        {
            var connection = callClient.GetCallConnection(callId);
            var invite = new CallInvite(new CommunicationUserIdentifier(repUserId));
            var options = new AddParticipantOptions(invite)
            {
                InvitationTimeoutInSeconds = InvitationTimeoutSeconds,
                OperationContext = "add-rep"
            };

            await connection.AddParticipantAsync(options, ct);
            callStore.MarkRepAdded();
            logger.LogInformation(
                "AddParticipant invited rep {UserId} to call {CallId} (Accept budget {Timeout}s).",
                repUserId, callId, InvitationTimeoutSeconds);
        }
        catch (Exception ex)
        {
            callStore.ResetAddRep(); // allow the next /register to retry
            logger.LogError(ex, "Failed to AddParticipant rep {UserId} to call {CallId}.", repUserId, callId);
        }
    }

    private static bool IsAuthorized(HttpContext ctx, IConfiguration config)
    {
        var expected = config["Rep:AccessKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return true; // POC: no key configured ⇒ anonymous (documented trade-off)

        var provided = ctx.Request.Headers[AccessKeyHeader].ToString();
        return !string.IsNullOrEmpty(provided) &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(provided),
                   System.Text.Encoding.UTF8.GetBytes(expected));
    }

    internal sealed record RepRegisterRequest(string UserId);
}
