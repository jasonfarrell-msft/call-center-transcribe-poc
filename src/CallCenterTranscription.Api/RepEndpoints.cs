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
            var trackedCallId = callStore.CallId;
            if (callClient is not null && !string.IsNullOrEmpty(trackedCallId) && !callStore.RepAdded)
            {
                logger.LogInformation(
                    "Rep register reconverge: activeCall={CallId} repAdded={RepAdded}; attempting AddParticipant for rep {UserId}.",
                    trackedCallId,
                    callStore.RepAdded,
                    body.UserId);
                await TryAddRepToCallAsync(callClient, callStore, registry, logger, ct);
            }
            else
            {
                logger.LogInformation(
                    "Rep register completed without AddParticipant attempt: callClientAvailable={CallClientAvailable} activeCall={CallId} repAdded={RepAdded}.",
                    callClient is not null,
                    trackedCallId ?? "(none)",
                    callStore.RepAdded);
            }

            return Results.Ok(new { registered = true, callActive = !string.IsNullOrEmpty(callStore.CallId) });
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
        {
            logger.LogInformation(
                "Skipping AddParticipant: activeCall={CallId} repUser={RepUser}.",
                callStore.CallId ?? "(none)",
                registry.CurrentUserId ?? "(none)");
            return;
        }

        if (!callStore.TryBeginAddRep())
        {
            logger.LogInformation(
                "Skipping AddParticipant claim: activeCall={CallId} repAdded={RepAdded}; another add is in-flight or already complete.",
                callStore.CallId ?? "(none)",
                callStore.RepAdded);
            return; // another path is adding / already added
        }

        // Re-snapshot INSIDE the claim: a concurrent Clear() (call ended) between the pre-check
        // and here would leave CallId null. Bail and release the claim so we never invite a rep
        // to a call that has already ended.
        var callId = callStore.CallId;
        var repUserId = registry.CurrentUserId;
        if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(repUserId))
        {
            callStore.ResetAddRep();
            logger.LogWarning(
                "AddParticipant claim released after state resnapshot: activeCall={CallId} repUser={RepUser}.",
                callId ?? "(none)",
                repUserId ?? "(none)");
            return;
        }

        try
        {
            var connection = callClient.GetCallConnection(callId);
            var invite = new CallInvite(new CommunicationUserIdentifier(repUserId));
            const string operationContext = "add-rep";
            var options = new AddParticipantOptions(invite)
            {
                InvitationTimeoutInSeconds = InvitationTimeoutSeconds,
                OperationContext = operationContext
            };

            logger.LogInformation(
                "AddParticipant request: callId={CallId} repUserId={UserId} operationContext={OperationContext} timeoutSeconds={TimeoutSeconds}.",
                callId,
                repUserId,
                operationContext,
                InvitationTimeoutSeconds);
            await connection.AddParticipantAsync(options, ct);
            logger.LogInformation(
                "AddParticipant accepted by ACS for call {CallId}; invite sent to rep {UserId} (Accept budget {Timeout}s, operationContext={OperationContext}). Waiting for AddParticipantSucceeded to mark rep connected.",
                callId,
                repUserId,
                InvitationTimeoutSeconds,
                operationContext);

            // Safety net: if ACS callback delivery is delayed/missed, release the in-flight add
            // claim after the invite budget so /register heartbeat can retry.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(InvitationTimeoutSeconds + 5)).ConfigureAwait(false);
                    if (string.Equals(callStore.CallId, callId, StringComparison.Ordinal))
                    {
                        callStore.ResetAddRep();
                        logger.LogInformation(
                            "AddParticipant claim timeout elapsed for call {CallId}; attempted claim release for retry if rep is still not connected.",
                            callId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to run AddParticipant claim-timeout recovery task.");
                }
            });
        }
        catch (Azure.RequestFailedException ex)
        {
            callStore.ResetAddRep(); // allow the next /register to retry
            logger.LogError(
                ex,
                "AddParticipant request failed for call {CallId} rep {UserId}: status={Status} errorCode={ErrorCode} message={Message}.",
                callId,
                repUserId,
                ex.Status,
                ex.ErrorCode,
                ex.Message);
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
