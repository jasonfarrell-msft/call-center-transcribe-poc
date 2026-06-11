namespace CallCenterTranscription.Api.Services;

/// <summary>
/// Per-call two-slot speaker attribution state machine for ConversationTranscriber diarization.
///
/// Problem addressed (2026-06-10):
///   ConversationTranscriber assigns opaque Guest IDs (Guest-1, Guest-2, …) by diarization
///   cluster, NOT chronological arrival order. The previous single-slot "first speaker = customer"
///   heuristic failed when the customer was silent on hold and the rep said the first complete
///   utterance after accepting the invite — the rep's greeting was latched as Customer, corrupting
///   all subsequent speaker labels and customer-only sentiment scoring.
///
/// Fix — phase-aware two-slot attribution:
///
///   Phase 1 | PRE-ACCEPT (RepAccepted = false):
///     The rep is physically absent from the Mixed audio stream (they have not yet accepted the
///     ACS AddParticipant invite). Any non-Unknown speaker heard before accept is DEFINITIVELY
///     the customer. → latch CustomerSpeakerId.
///
///   Phase 2A | POST-ACCEPT, customer already latched:
///     First new distinct speaker = rep. → latch RepSpeakerId.
///     (Normal path when customer spoke before accept or spoke before the rep greeted.)
///
///   Phase 2B | POST-ACCEPT, NEITHER slot latched yet:
///     Caller-order rule for this inbound flow: customer initiates the call, rep joins second.
///     Therefore first observed speaker = CUSTOMER, second distinct speaker = REP.
///
///   "Unknown" / empty SpeakerIds are never latched (IsSpeakerKnown = false).
///   Once a slot is latched it is NEVER changed for the call lifetime.
///
/// Usage:
///   Call <see cref="Observe"/> from each ConversationTranscriber Transcribed handler.
///   Check <see cref="IsCustomer"/> to determine sentiment routing.
///   Reset by discarding the instance (one instance per call session).
/// </summary>
internal sealed class SpeakerAttributionState
{
    /// <summary>Guest ID latched as the customer, or null if not yet resolved.</summary>
    public string? CustomerSpeakerId { get; private set; }

    /// <summary>Guest ID latched as the rep, or null if not yet resolved.</summary>
    public string? RepSpeakerId { get; private set; }

    /// <summary>
    /// Observes a speaker ID from a Transcribed event and advances the state machine.
    /// </summary>
    /// <param name="speakerId">The SpeakerId from ConversationTranscriptionResult. May be null/empty/"Unknown".</param>
    /// <param name="repAccepted">Current value of <see cref="ActiveCallStore.RepAccepted"/>.</param>
    /// <returns>
    /// A description string of the transition that occurred (for structured logging), or null if
    /// the speaker was unknown / both slots were already latched.
    /// </returns>
    public string? Observe(string? speakerId, bool repAccepted)
    {
        if (!IsSpeakerKnown(speakerId))
            return null;

        var id = speakerId!;

        if (CustomerSpeakerId is null)
        {
            // First observed known speaker is customer for this inbound call topology:
            // customer calls in first, rep joins second.
            CustomerSpeakerId = id;
            return repAccepted
                ? $"customer latched POST-ACCEPT (call-order first) SpeakerId={id}"
                : $"customer latched PRE-ACCEPT (definitive) SpeakerId={id}";
        }

        if (CustomerSpeakerId is not null && RepSpeakerId is null
            && !string.Equals(id, CustomerSpeakerId, StringComparison.Ordinal))
        {
            // Second distinct known speaker is rep.
            RepSpeakerId = id;
            return $"rep latched (second distinct speaker) SpeakerId={id}";
        }

        return null; // already fully resolved, or same-speaker repeat
    }

    /// <summary>
    /// Returns true when <paramref name="speakerId"/> is a known, clearly-attributed speaker
    /// AND matches the latched customer slot. False for rep, Unknown, or unresolved speakers.
    /// </summary>
    public bool IsCustomer(string? speakerId) =>
        CustomerSpeakerId is not null &&
        IsSpeakerKnown(speakerId) &&
        string.Equals(speakerId, CustomerSpeakerId, StringComparison.Ordinal);

    /// <summary>Returns true if the SpeakerId is a clear attribution (not null/empty/"Unknown").</summary>
    public static bool IsSpeakerKnown(string? speakerId) =>
        !string.IsNullOrWhiteSpace(speakerId) &&
        !string.Equals(speakerId, "Unknown", StringComparison.OrdinalIgnoreCase);
}
