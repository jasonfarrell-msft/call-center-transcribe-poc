using CallCenterTranscription.Api.Services;

namespace CallCenterTranscription.Tests;

// Rep Call Control — xUnit test stubs derived from yzak-rep-call-control-tests.md.
//
// Automatable unit tests against ActiveCallStore and LiveSentimentStore seams are
// marked [Fact] and should pass today. Scenarios blocked on a not-yet-implemented
// production feature (e.g., the "Call Pending" badge / stream.callIncoming event)
// are marked [Fact(Skip = "...")] so the suite stays green while the work is in flight.
//
// Integration + manual tests are documented in yzak-rep-call-control-tests.md and are
// NOT represented here — they require live ACS or a running SignalR hub.

public sealed class RepCallControlTests
{
    // ── TC-10: ActiveCallStore — Incoming claim released after cancel ───────────────────
    // Rep rejects or caller abandons before answer. The incoming claim must be released so
    // the next call can claim it.

    [Fact]
    public void ActiveCallStore_IncomingClaim_ReleasedAfterCancel()
    {
        var store = new ActiveCallStore();

        var first = store.TryBeginIncomingClaim();
        Assert.True(first, "first claim should succeed");

        var concurrent = store.TryBeginIncomingClaim();
        Assert.False(concurrent, "concurrent second claim should fail while first is in-progress");

        store.CancelIncomingClaim();

        var retry = store.TryBeginIncomingClaim();
        Assert.True(retry, "claim should succeed after cancel");
        Assert.Null(store.CallId);
    }

    // ── TC-15: ActiveCallStore — Fully reset between calls ──────────────────────────────

    [Fact]
    public void ActiveCallStore_Clear_ResetsAllStateMachines()
    {
        var store = new ActiveCallStore();

        store.SetCallId("call-abc");
        store.TryBeginAddRep();
        store.MarkRepAdded();
        store.TryBeginMediaClaim();
        // Simulate a complete call lifecycle then teardown.
        store.EndMediaClaim();
        store.Clear();

        Assert.Null(store.CallId);
        Assert.False(store.RepAdded);
        Assert.True(store.TryBeginIncomingClaim(), "incoming claim must be available after Clear");
        Assert.True(store.TryBeginMediaClaim(), "media claim must be available after Clear");
        Assert.True(store.TryBeginAddRep(), "rep-add claim must be available after Clear");
    }

    // ── TC-16: ActiveCallStore — MediaClaim released on teardown ────────────────────────
    // Two sub-cases: (a) EndMediaClaim then Clear (test-sequence), (b) Clear then EndMediaClaim
    // (production-sequence — see AcsEndpoints.HandleMediaStreamAsync finally-block order).

    [Fact]
    public void ActiveCallStore_MediaClaim_ReleasedAfterEndAndClear()
    {
        var store = new ActiveCallStore();
        store.SetCallId("call-media-1");

        Assert.True(store.TryBeginMediaClaim());
        Assert.False(store.TryBeginMediaClaim(), "cannot claim media twice concurrently");

        store.EndMediaClaim();
        Assert.True(store.TryBeginMediaClaim(), "should be claimable again after EndMediaClaim");

        store.EndMediaClaim();
        store.Clear();
        Assert.True(store.TryBeginMediaClaim(), "should be claimable after Clear");
    }

    // ── TC-16b: MediaClaim — production teardown sequence (Clear THEN EndMediaClaim) ─────
    // AcsEndpoints.HandleMediaStreamAsync finally-block calls Clear() BEFORE EndMediaClaim().
    // Verify the claim is still properly released in that order.

    [Fact]
    public void ActiveCallStore_MediaClaim_ReleasedWhenClearCalledBeforeEnd_ProductionSequence()
    {
        var store = new ActiveCallStore();
        store.SetCallId("call-media-2");

        Assert.True(store.TryBeginMediaClaim(), "claim before teardown");
        Assert.False(store.TryBeginMediaClaim(), "still locked mid-call");

        // Production teardown order: Clear() then EndMediaClaim()
        store.Clear();
        // MediaClaim is STILL in-progress here (Clear does not reset it)
        Assert.False(store.TryBeginMediaClaim(), "media still claimed after Clear before EndMediaClaim");

        store.EndMediaClaim();
        Assert.True(store.TryBeginMediaClaim(), "media available after EndMediaClaim completes teardown");
    }

    // ── TC-18: ActiveCallStore — Concurrent answer-race blocked ────────────────────────
    // Two ACS IncomingCall webhook deliveries can race to answer the same call. Only one
    // handler should win. CompleteIncomingClaim() clears the claim state (it covers the
    // answer race window only; the single-call POC constraint is enforced at the ACS
    // network level, not in this store). See TC-18 in yzak-rep-call-control-tests.md for
    // the JavaScript double-ring auto-reject that is the actual second-call guard.

    [Fact]
    public void ActiveCallStore_IncomingClaim_BlocksConcurrentAnswerRaceForSameCall()
    {
        var store = new ActiveCallStore();

        Assert.True(store.TryBeginIncomingClaim(), "first handler claims the answer slot");
        Assert.False(store.TryBeginIncomingClaim(), "concurrent second handler is blocked");

        // First handler wins and completes the answer — releases the claim.
        store.CompleteIncomingClaim("call-xyz");
        Assert.Equal("call-xyz", store.CallId);

        // After answer completes, the claim window is closed. A new IncomingCall event
        // for a future call can begin a claim (POC: ACS won't deliver one while active).
        Assert.True(store.TryBeginIncomingClaim(), "claim available after CompleteIncomingClaim");

        // Full teardown releases everything.
        store.CancelIncomingClaim();
        store.Clear();
        Assert.True(store.TryBeginIncomingClaim(), "claim available after Clear");
    }

    // ── TC-12: LiveSentimentStore — Starts clean on every Accept ────────────────────────

    [Fact]
    public void LiveSentimentStore_Reset_StartsCleanForNewCall()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-1");
        store.Append("call-1", "this is terrible, I want to cancel");
        Assert.NotEmpty(store.GetFeed().Events);

        // New call accepted — Reset() must wipe the previous call's state.
        store.Clear();
        store.Reset("call-2");

        var feed = store.GetFeed();
        Assert.Empty(feed.Events);
        Assert.Equal(string.Empty, feed.CallId);

        // Old call's late utterance is rejected.
        var lateSentiment = store.Append("call-1", "still angry about the last call");
        Assert.Null(lateSentiment);
        Assert.Empty(store.GetFeed().Events);
    }

    // ── TC-14: LiveSentimentStore — Late utterances dropped after call ends ─────────────
    // Regression: we've seen the Speech SDK deliver a final utterance after the call's
    // media stream closes. The store's _active guard must drop it silently.

    [Fact]
    public void LiveSentimentStore_DropsLateUtteranceAfterClearPreventsNextCallPoisoning()
    {
        var store = new LiveSentimentStore();
        store.Reset("call-ended");
        store.Append("call-ended", "I am very unhappy with this service");
        store.Clear();

        // Late flush from the Speech SDK — must be a no-op.
        var late = store.Append("call-ended", "still unhappy");
        Assert.Null(late);

        // Next call starts fresh.
        store.Reset("call-next");
        Assert.Empty(store.GetFeed().Events);
        Assert.Equal(string.Empty, store.GetFeed().CallId);
    }

    // ── TC-08: LiveSentimentStore — Reject path never calls Reset ───────────────────────
    // When a call is rejected, Reset() should NOT have been called. Confirm the store
    // remains in a clean post-Clear state (not an active-but-empty state).

    [Fact]
    public void LiveSentimentStore_RejectPath_NeverCallsReset_StoreStaysClean()
    {
        var store = new LiveSentimentStore();
        // Simulate system startup: store is untouched (no Reset, no Clear).
        // A ring arrives, rep declines, Clear() is called defensively.
        store.Clear();

        Assert.Empty(store.GetFeed().Events);
        Assert.Equal(string.Empty, store.GetFeed().CallId);

        // After defensive Clear, a late utterance with any call ID must be silently dropped.
        var dropped = store.Append("call-rejected", "thank you for calling");
        Assert.Null(dropped);
    }

    // ── TC-04 (partial): ActiveCallStore — Clean after reject (no callId set) ──────────

    [Fact]
    public void ActiveCallStore_RejectPath_CallIdRemainsNullAfterCancelledClaim()
    {
        var store = new ActiveCallStore();

        store.TryBeginIncomingClaim();
        // Rep declines → no CompleteIncomingClaim, just cancel.
        store.CancelIncomingClaim();

        Assert.Null(store.CallId);
        Assert.False(store.RepAdded);
    }

    // ── TC-07 (partial): endedTimer cleared by new callStarted ──────────────────────────
    // This is a JavaScript behaviour; no C# equivalent. Documented here as a reminder that
    // the frontend test must verify clearTimeout() is called on new stream.callStarted.
    // Skip: requires a JS test runner (Playwright or Jasmine).

    [Fact(Skip = "Frontend JS behaviour — verify in Playwright: endedTimer cleared by new stream.callStarted before 4s expires")]
    public void Frontend_EndedTimer_ClearedOnNewCallStarted()
    {
        // Covered by manual TC-07. A Playwright test should:
        // 1. Trigger stream.callEnded.
        // 2. Within 2 seconds, trigger stream.callStarted with a new callId.
        // 3. Assert badge goes to "connecting", not "disconnected".
    }

    // ── TC-02: pending call offer shows before transcript starts ─────────────────────────
    // Lower transcript badge now reflects speech-service connectivity only; the Accept offer
    // must appear in the header call bar as soon as stream.callPending arrives.

    [Fact(Skip = "Frontend JS behaviour — validate rep-callbar exposes Accept on stream.callPending while the lower speech badge stays connected, once Playwright coverage exists")]
    public void Frontend_AcceptOffer_ShowsOnPendingCall_WithoutSwitchingSpeechBadge()
    {
        // When stream.callPending fires:
        // - [data-rep-accept] must be visible immediately (disabled until ACS incomingCall arrives).
        // - [data-conn-label] must continue to reflect speech-service connectivity, not call pending.
        // - Transcript content area must contain zero .transcript-line elements.
        // Implement once Playwright coverage is available for the live browser path.
    }

    // ── TC-11: Customer-only sentiment ──────────────────────────────────────────────────
    // Contract is now defined (resolved Q2): SpeechTranscriptionService calls Append() ONLY
    // when IsCustomerSpeaker() returns true (latched speaker ID matches AND RepAccepted is true).
    // A direct unit test of the filtering seam requires mocking ConversationTranscriber (Speech
    // SDK sealed class — not feasible without an integration wrapper). The LiveSentimentStore
    // _active guard (tested in TC-12 and TC-14) is the last-resort firewall. Skip retained
    // because the filtering lives in SpeechTranscriptionService, not in the store itself.

    [Fact(Skip = "Service-layer filtering verified by code review (SpeechTranscriptionService.IsCustomerSpeaker); unit test blocked by Speech SDK sealed ConversationTranscriber — needs Playwright/integration test for end-to-end validation")]
    public void LiveSentimentStore_OnlyCustomerUtterancesScored_RepUtterancesDropped()
    {
        // When speaker filtering is in place at the SpeechTranscriptionService layer:
        // - Append() should only be called with customer utterances.
        // - Add a test here once the filtering seam is confirmed.
    }

    // ── TC-19: ActiveCallStore — TryBeginTeardown idempotency ──────────────────────────
    // Exactly ONE teardown path must win per call (media-stream WebSocket finally-block OR
    // ACS CallDisconnected callback, whichever fires first). The loser must get false and
    // skip its teardown work entirely. Clear() resets the latch so the next call lifecycle
    // can claim teardown.
    //
    // FINDING: There is no EndTeardown() / CancelTeardown() — unlike MediaClaim, teardown
    // is intentionally terminal per call. The latch can only be reset by Clear() (or
    // CompleteIncomingClaim()). This is correct design: if teardown started it should not
    // be unwound; the next call begins cleanly via Clear().

    [Fact]
    public void ActiveCallStore_Teardown_FirstCallReturnsTrue()
    {
        var store = new ActiveCallStore();

        var first = store.TryBeginTeardown();
        Assert.True(first, "first caller must claim teardown");
    }

    [Fact]
    public void ActiveCallStore_Teardown_SubsequentCallsReturnFalse()
    {
        var store = new ActiveCallStore();

        Assert.True(store.TryBeginTeardown(), "first caller claims teardown");
        Assert.False(store.TryBeginTeardown(), "second concurrent caller is blocked");
        Assert.False(store.TryBeginTeardown(), "third caller is also blocked — no double-teardown");
    }

    [Fact]
    public void ActiveCallStore_Teardown_ClaimResetsAfterClear_NewCallCanClaim()
    {
        var store = new ActiveCallStore();

        store.SetCallId("call-tear-1");
        Assert.True(store.TryBeginTeardown(), "first call teardown claimed");
        Assert.False(store.TryBeginTeardown(), "teardown still locked before Clear");

        // Teardown completes — Clear() resets the latch for the next call lifecycle.
        store.Clear();
        Assert.Null(store.CallId);

        // A new call can now claim teardown.
        store.SetCallId("call-tear-2");
        Assert.True(store.TryBeginTeardown(), "teardown claim available after Clear for new call");
        Assert.False(store.TryBeginTeardown(), "new call teardown also locks after first claim");
    }

    // ── TC-03 (partial): ActiveCallStore — RepAccepted state machine ────────────────────
    // MarkAccepted() fires on AddParticipantSucceeded. Clear() and CompleteIncomingClaim()
    // both reset it so the flag cannot bleed from one call to the next.

    [Fact]
    public void ActiveCallStore_RepAccepted_StateTransitions()
    {
        var store = new ActiveCallStore();

        // Initial state: not accepted.
        Assert.False(store.RepAccepted, "should start not-accepted");

        // Simulate call answer flow.
        store.TryBeginIncomingClaim();
        store.CompleteIncomingClaim("call-accept-1");
        Assert.False(store.RepAccepted, "CompleteIncomingClaim must not set RepAccepted");

        // Rep clicks Accept → ACS fires AddParticipantSucceeded → MarkAccepted().
        store.MarkAccepted();
        Assert.True(store.RepAccepted, "MarkAccepted should set RepAccepted to true");

        // Teardown: Clear() resets the flag.
        store.Clear();
        Assert.False(store.RepAccepted, "Clear must reset RepAccepted to false");

        // Second call: CompleteIncomingClaim also resets (guards against stale accept bleed-over).
        store.TryBeginIncomingClaim();
        store.MarkAccepted(); // simulate late stale MarkAccepted call
        store.CompleteIncomingClaim("call-accept-2");
        Assert.False(store.RepAccepted, "CompleteIncomingClaim must reset RepAccepted for the new call window");
    }
}
