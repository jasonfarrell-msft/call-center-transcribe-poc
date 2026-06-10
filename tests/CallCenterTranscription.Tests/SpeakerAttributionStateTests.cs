using CallCenterTranscription.Api.Services;

namespace CallCenterTranscription.Tests;

/// <summary>
/// Unit tests for <see cref="SpeakerAttributionState"/> — the two-slot phase-aware speaker
/// attribution state machine introduced to fix the Rep/Customer label flip bug (2026-06-10).
///
/// BUG: When the customer was silent on hold and the rep said the first complete utterance
/// after accepting, the old single-slot "first speaker = customer" heuristic latched the rep
/// as Customer, flipping all labels and corrupting customer-only sentiment scoring.
///
/// FIX: RepAccepted is used as a phase boundary. Pre-accept speakers are definitively Customer.
/// Post-accept (both in stream): if neither slot is latched yet, the FIRST speaker is the REP
/// (greeting scenario), not the customer. Second distinct speaker = Customer.
/// </summary>
public sealed class SpeakerAttributionStateTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static SpeakerAttributionState Advance(
        SpeakerAttributionState state,
        IEnumerable<(string speakerId, bool repAccepted)> observations)
    {
        foreach (var (id, accepted) in observations)
            state.Observe(id, accepted);
        return state;
    }

    // ── Phase 1: pre-accept → customer ───────────────────────────────────────────────────────

    [Fact]
    public void Phase1_FirstSpeakerPreAccept_IsCustomer()
    {
        var s = new SpeakerAttributionState();
        s.Observe("Guest-1", repAccepted: false);

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Null(s.RepSpeakerId);
        Assert.True(s.IsCustomer("Guest-1"));
        Assert.False(s.IsCustomer("Guest-2"));
    }

    [Fact]
    public void Phase1_SecondDistinctSpeakerAfterCustomerLatched_IsRep()
    {
        var s = new SpeakerAttributionState();
        s.Observe("Guest-1", repAccepted: false); // customer
        s.Observe("Guest-2", repAccepted: true);  // rep joins and speaks

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Equal("Guest-2", s.RepSpeakerId);
        Assert.True(s.IsCustomer("Guest-1"));
        Assert.False(s.IsCustomer("Guest-2"));
    }

    [Fact]
    public void Phase1_SameSpeakerRepeated_NoChangeToSlots()
    {
        var s = new SpeakerAttributionState();
        s.Observe("Guest-1", repAccepted: false);
        s.Observe("Guest-1", repAccepted: false); // repeat
        s.Observe("Guest-1", repAccepted: true);  // still same speaker after accept

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Null(s.RepSpeakerId);
    }

    // ── Phase 2B: both slots unlatched when rep accepts → rep speaks first (the flip case) ──

    [Fact]
    public void Phase2B_RepSpeaksFirstPostAccept_IsLatchedAsRep()
    {
        // THE BUG SCENARIO: customer was silent on hold, rep greets first.
        var s = new SpeakerAttributionState();

        // No pre-accept utterances (customer was silent).
        // Rep says "Hello, how can I help you?" — first Transcribed fires post-accept.
        s.Observe("Guest-1", repAccepted: true);

        // Before the fix: Guest-1 would be latched as Customer → flip.
        // After the fix:  Guest-1 is latched as Rep (Phase 2B).
        Assert.Null(s.CustomerSpeakerId);
        Assert.Equal("Guest-1", s.RepSpeakerId);
        Assert.False(s.IsCustomer("Guest-1"), "rep must NOT be flagged as customer");
    }

    [Fact]
    public void Phase2B_CustomerRespondsAfterRepGreeting_IsLatchedAsCustomer()
    {
        // THE BUG SCENARIO continued: customer responds after rep's greeting.
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true);  // rep greeting — Phase 2B
        s.Observe("Guest-2", repAccepted: true);  // customer responds — Phase 2B resolution

        Assert.Equal("Guest-2", s.CustomerSpeakerId);
        Assert.Equal("Guest-1", s.RepSpeakerId);
        Assert.True(s.IsCustomer("Guest-2"), "customer must be correctly identified after Phase 2B resolution");
        Assert.False(s.IsCustomer("Guest-1"), "rep must NOT be customer after Phase 2B resolution");
    }

    [Fact]
    public void Phase2B_CustomerSpeaksFirstPostAccept_NoPreAcceptSpeech()
    {
        // ── DOCUMENTED PHASE-2 LIMITATION ───────────────────────────────────────────────────────
        // When the customer speaks FIRST post-accept AND there is no pre-accept speech to establish
        // Phase 1 attribution, Phase 2B cannot distinguish who greeted first. The state machine
        // assumes the first post-accept speaker is the REP (greeting scenario), so a customer who
        // speaks first is incorrectly latched as Rep, and the actual rep who follows is latched as
        // Customer — a label flip.
        //
        // This test asserts CURRENT (known-incorrect) behavior so any future fix is immediately
        // visible as a failing test. This is NOT desired behavior; it is a demo-unlikely edge case
        // documented for Phase-2 visibility. See .squad/decisions/inbox/yzak-speaker-edge-test.md.
        // ────────────────────────────────────────────────────────────────────────────────────────

        var s = new SpeakerAttributionState();

        // No pre-accept speech — Phase 1 never fires.
        // Customer speaks first post-accept (e.g., impatient caller or locale where callers
        // don't wait for the rep greeting). Phase 2B has no way to detect this.
        s.Observe("Guest-1", repAccepted: true);  // actually the CUSTOMER — Phase 2B latches as REP
        s.Observe("Guest-2", repAccepted: true);  // actually the REP — Phase 2B resolution latches as CUSTOMER

        // Current (known-wrong) behavior: labels are flipped.
        Assert.Equal("Guest-2", s.CustomerSpeakerId); // WRONG: this is actually the rep
        Assert.Equal("Guest-1", s.RepSpeakerId);       // WRONG: this is actually the customer
        Assert.False(s.IsCustomer("Guest-1"),
            "known limitation: real customer is mislabeled as rep when they speak first post-accept with no pre-accept speech");
        Assert.True(s.IsCustomer("Guest-2"),
            "known limitation: real rep is mislabeled as customer in the same scenario");
    }

    [Fact]
    public void Phase2B_SlotsDoNotFlipAfterResolution()
    {
        // After both slots are latched, additional observations from the same speakers must
        // not change anything. The labels must remain stable for the call lifetime.
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true);  // rep
        s.Observe("Guest-2", repAccepted: true);  // customer

        // Multiple subsequent utterances must not flip the slots.
        s.Observe("Guest-1", repAccepted: true);
        s.Observe("Guest-2", repAccepted: true);
        s.Observe("Guest-1", repAccepted: true);

        Assert.Equal("Guest-2", s.CustomerSpeakerId);
        Assert.Equal("Guest-1", s.RepSpeakerId);
    }

    // ── Unknown / empty SpeakerIds must never be latched ─────────────────────────────────────

    [Fact]
    public void UnknownSpeakerId_NeverLatched()
    {
        var s = new SpeakerAttributionState();

        s.Observe("Unknown", repAccepted: false);
        s.Observe("", repAccepted: false);
        s.Observe(null, repAccepted: false);
        s.Observe("UNKNOWN", repAccepted: false); // case-insensitive

        Assert.Null(s.CustomerSpeakerId);
        Assert.Null(s.RepSpeakerId);
        Assert.False(s.IsCustomer("Unknown"));
        Assert.False(s.IsCustomer(""));
        Assert.False(s.IsCustomer(null));
    }

    [Fact]
    public void UnknownThenKnown_KnownSpeakerLatchedPreAccept_IsCustomer()
    {
        // Unknown utterances during warm-up must not interfere with Phase 1 latching.
        var s = new SpeakerAttributionState();

        s.Observe("Unknown", repAccepted: false); // warm-up noise
        s.Observe("Unknown", repAccepted: false); // more noise
        s.Observe("Guest-1", repAccepted: false); // first clear attribution → customer

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.True(s.IsCustomer("Guest-1"));
    }

    // ── IsCustomer edge cases ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsCustomer_BeforeAnyLatch_ReturnsFalse()
    {
        var s = new SpeakerAttributionState();
        Assert.False(s.IsCustomer("Guest-1"));
        Assert.False(s.IsCustomer("Guest-2"));
    }

    [Fact]
    public void IsCustomer_RepSpeakerId_ReturnsFalse()
    {
        var s = new SpeakerAttributionState();
        s.Observe("Guest-1", repAccepted: false); // customer
        s.Observe("Guest-2", repAccepted: true);  // rep

        Assert.False(s.IsCustomer("Guest-2"), "rep speaker ID must never return true for IsCustomer");
    }

    // ── IsSpeakerKnown static helper ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Guest-1", true)]
    [InlineData("Guest-2", true)]
    [InlineData("Unknown", false)]
    [InlineData("unknown", false)]
    [InlineData("UNKNOWN", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSpeakerKnown_ReturnsExpected(string? speakerId, bool expected)
    {
        Assert.Equal(expected, SpeakerAttributionState.IsSpeakerKnown(speakerId));
    }

    // ── Observe returns transition description for logging ────────────────────────────────────

    [Fact]
    public void Observe_ReturnsTransitionStringOnNewLatch()
    {
        var s = new SpeakerAttributionState();
        var t1 = s.Observe("Guest-1", repAccepted: false);
        Assert.NotNull(t1);
        Assert.Contains("Guest-1", t1);
    }

    [Fact]
    public void Observe_ReturnsNullForRepeatOrUnknown()
    {
        var s = new SpeakerAttributionState();
        s.Observe("Guest-1", repAccepted: false);

        var repeat = s.Observe("Guest-1", repAccepted: true); // same speaker again
        Assert.Null(repeat);

        var unknown = s.Observe("Unknown", repAccepted: true);
        Assert.Null(unknown);
    }
}
