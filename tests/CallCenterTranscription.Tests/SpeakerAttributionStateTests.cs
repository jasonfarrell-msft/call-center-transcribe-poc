using CallCenterTranscription.Api.Services;

namespace CallCenterTranscription.Tests;

/// <summary>
/// Unit tests for <see cref="SpeakerAttributionState"/> — the two-slot phase-aware speaker
/// attribution state machine introduced to fix the Rep/Customer label flip bug (2026-06-10).
///
/// BUG: The prior Phase-2B fallback latched the first post-accept speaker as Rep when no
/// pre-accept speaker had been observed. In inbound calls where the customer initiates and the
/// rep joins second, this mislabeled customer speech as Rep and could mark the timeline as all-rep.
///
/// FIX: Caller-order rule is now enforced for this flow:
/// first observed speaker = Customer, second distinct speaker = Rep.
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

    // ── Phase 2B: both slots unlatched when rep accepts ──────────────────────────────────────

    [Fact]
    public void Phase2B_FirstSpeakerPostAccept_IsLatchedAsCustomer()
    {
        var s = new SpeakerAttributionState();

        // No pre-accept utterances were finalized; first observed speaker after accept must still
        // map to customer for this inbound call flow.
        s.Observe("Guest-1", repAccepted: true);

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Null(s.RepSpeakerId);
        Assert.True(s.IsCustomer("Guest-1"));
    }

    [Fact]
    public void Phase2B_SecondDistinctSpeakerPostAccept_IsLatchedAsRep()
    {
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true);  // customer
        s.Observe("Guest-2", repAccepted: true);  // rep joins and speaks

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Equal("Guest-2", s.RepSpeakerId);
        Assert.True(s.IsCustomer("Guest-1"), "customer must remain customer");
        Assert.False(s.IsCustomer("Guest-2"), "rep must not be customer");
    }

    [Fact]
    public void Phase2B_SlotsDoNotFlipAfterResolution()
    {
        // After both slots are latched, additional observations from the same speakers must
        // not change anything. The labels must remain stable for the call lifetime.
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true);  // customer
        s.Observe("Guest-2", repAccepted: true);  // rep

        // Multiple subsequent utterances must not flip the slots.
        s.Observe("Guest-1", repAccepted: true);
        s.Observe("Guest-2", repAccepted: true);
        s.Observe("Guest-1", repAccepted: true);

        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Equal("Guest-2", s.RepSpeakerId);
    }

    [Fact]
    public void Phase2B_NewSpeakerIdAfterBothSlotsLatched_IsAmbiguousAfterRepTurn()
    {
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true); // customer
        s.Observe("Guest-2", repAccepted: true); // rep
        s.Observe("Guest-2", repAccepted: true); // rep spoke most recently
        var transition = s.Observe("Guest-3", repAccepted: true); // new diarization cluster

        Assert.False(s.IsCustomer("Guest-3"), "new speaker after both slots are latched must remain ambiguous");
        Assert.Null(transition);
        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Equal("Guest-2", s.RepSpeakerId);
    }

    [Fact]
    public void Phase2B_NewSpeakerIdAfterBothSlotsLatched_IsAmbiguousAfterCustomerTurn()
    {
        var s = new SpeakerAttributionState();

        s.Observe("Guest-1", repAccepted: true); // customer
        s.Observe("Guest-2", repAccepted: true); // rep
        s.Observe("Guest-1", repAccepted: true); // customer spoke most recently
        var transition = s.Observe("Guest-4", repAccepted: true); // new diarization cluster

        Assert.False(s.IsCustomer("Guest-4"), "new speaker after both slots are latched must remain ambiguous");
        Assert.Null(transition);
        Assert.Equal("Guest-1", s.CustomerSpeakerId);
        Assert.Equal("Guest-2", s.RepSpeakerId);
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
    [InlineData("   ", false)]
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
