# Lacus — History [SUMMARY]

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center
- **Focus:** Intelligence layer — diarization, translation, sentiment, churn-risk agent, RAG, NBA generation
- **Model:** MAI models preferred via Azure AI Foundry
- **Created:** 2026-06-05

## Key Decisions & Work Completed

### Design Principles (2026-06-05)
- MAI-DS-R1 for reasoning (churn, NBA, RAG)
- Azure AI Speech + ConversationTranscriber for STT/diarization
- Azure Translator for per-utterance translation
- Azure AI Language for sentiment
- SignalR event-driven pipeline (transcript, translation, sentiment, churn_risk, knowledge_cards, next_best_action)

### Speech Consumer BackgroundService (2026-06-08)
- Built `SpeechTranscriptionService : BackgroundService`
- Reads `IAudioSource` (swappable Mock/ACS), writes PCM 16kHz mono to `PushAudioInputStream`
- Managed-identity auth: `DefaultAzureCredential` → AAD token formatted as `aad#{resourceId}#{token}`
- SignalR output on `"stream.transcript"` (same method/DTO as scripted feed)
- Removed `DemoSafety:DataMode` guard per go-live spec
- Build: 0 errors

### ConversationTranscriber + Customer-Only Sentiment (2026-06-10)
- Swapped `SpeechRecognizer` → `ConversationTranscriber` on Mixed stream
- Phase 1 heuristic: first non-Unknown speaker = Customer (rep physically absent pre-accept)
- Accept-gate placed in event handler (`RepAccepted` bool check)
- Customer-only sentiment via `IsCustomerSpeaker()` filter
- Commit 17a18c0 (merged with rep-control)

### Speaker Label Flip Fix (2026-06-10)
- **Root cause:** ConversationTranscriber assigns Guest IDs by diarization cluster, not arrival order. Customer silent on hold → rep greeting becomes first `Transcribed` event → rep latched as customer.
- **Fix:** Two-slot phase-aware attribution in `SpeakerAttributionState`
  - Pre-accept: any speaker = Customer
  - Post-accept, customer latched: new speaker = Rep
  - Post-accept, neither latched (Phase 2B): first = Rep, second = Customer
- Extracted testable state machine (internal sealed class)
- 14 unit tests including flip scenario
- `InternalsVisibleTo` added to Api.csproj
- Commit cf3694e

**Known limitation:** Phase 2B edge (customer speaks first post-accept, no pre-accept) remains (demo-unlikely, documented by Yzak's test).

## Production Path

- Customer-only sentiment: replace with deterministic ACS participant identity (Unmixed audio or participant role API)
- Speaker attribution: same → requires per-frame participant ID correlation (out of scope POC)

## Learnings

- **Date:** 2026-06-11T15:41:04.207-04:00
- **Architecture decision:** For this inbound ACS flow, enforce caller-order attribution: first observed known speaker is `Customer`; second distinct speaker is `Rep`.
- **Pattern:** Keep speaker-role attribution in a dedicated per-call state machine (`SpeakerAttributionState`) and assert role transitions with unit tests before wiring UI labels.
- **User preference:** Avoid confidence-only heuristics that can relabel customer speech as rep; favor deterministic, explainable mapping for surfaced roles/sentiment.
- **Key file paths:**
  - `src/CallCenterTranscription.Api/Services/SpeakerAttributionState.cs`
  - `src/CallCenterTranscription.Api/Services/SpeechTranscriptionService.cs`
  - `tests/CallCenterTranscription.Tests/SpeakerAttributionStateTests.cs`

- **Date:** 2026-06-11T15:36:11.935-04:00
- **Pattern:** Flat agent-assist JSONL ingests cleanly across future search stacks when every record carries both a stable chunk key (`id`) and a parent `document_id`, plus explicit `chunk_index`/`chunk_count`.
- **Grounding rule:** Include `retrieval_text`, `source_title`, `source_section`, `source_uri`, and a preformatted `citation_label` on each record so UI citations and retrievers do not need service-specific joins.
- **Validation:** Protect the corpus with a lightweight test that asserts ingestion metadata is present and IDs remain unique as Kira expands the dataset.

- **Date:** 2026-06-11T16:34:04.094-04:00
- **Next-step architecture:** The best service-agnostic path to live agent assist is an in-process transcript-window matcher over recent **customer** turns, not single-utterance lookup and not Azure Search yet.
- **Retrieval rule:** Score with explainable signals (trigger-phrase hit, keyword overlap, profile/intent match, recency, escalation priority) and require a threshold + winner margin before surfacing guidance.
- **UI contract learning:** Agent-assist events need ranked results, citations, and short evidence snippets tied to transcript-window boundaries so the rep can see *why* a card appeared during the conversation.
- **Safety note:** Avoid replaying broad transcript text in retrieval events; emit only minimal matched evidence and portable source metadata (`citation_label`, `source_title`, `source_section`, `source_uri`).

- **Date:** 2026-06-11T16:42:31.815-04:00
- **Sentiment routing guard:** After `CustomerSpeakerId` and `RepSpeakerId` are latched, any new diarization `SpeakerId` is treated as ambiguous (not auto-mapped by turn-taking) so rep speech cannot contaminate customer-only sentiment.
- **Bug root cause:** A new mid-call diarization cluster could be misattributed and stop/poison sentiment updates; strict ambiguity handling preserves trust over guessed confidence.
- **Verification:** Added tests to assert ambiguous post-latch speaker IDs do not become customer and do not move rolling sentiment.
- **Key file paths:**
  - `src/CallCenterTranscription.Api/Services/SpeakerAttributionState.cs`
  - `tests/CallCenterTranscription.Tests/SpeakerAttributionStateTests.cs`
  - `tests/CallCenterTranscription.Tests/LiveSentimentTests.cs`

- **Date:** 2026-06-11T16:49:37.710-04:00
- **Demo retrieval mapping:** Keep demo trigger expectations in a companion artifact beside the script set so narrative turns stay stable while Lacus can add ranked card/evidence expectations without rewriting Kira's transcript file.
- **Minimal contract:** For demoable agent assist, the existing `KnowledgeCardEvent` correlation fields are enough if each card also carries `citationLabel`, `sourceSection`, `rank`, and `matchedEvidence[]` (`kind`, short transcript excerpt, normalized text when translated, matched knowledge text, locale).
- **Evidence guardrail:** Cap surfaced evidence to short excerpts and prefer customer-turn snippets over full transcript replay, especially for translated competitor-price turns.
- **Key file paths:**
  - `samples/agent-assist-demo-scripts.v1.json`
  - `samples/agent-assist-demo-trigger-expectations.v1.json`
  - `tests/CallCenterTranscription.Tests/SyntheticCorpusTests.cs`

- **Date:** 2026-06-12T10:14:21.594-04:00
- **Demo sentiment tuning:** A short rolling window (2 recent customer signals) plus deterministic resolution/acceptance cues makes the meter move earlier and lets a successful save/upsell finish as `resolved` instead of staying anchored to opening frustration.
- **Deterministic cueing rule:** Only clearly-attributed rep turns may contribute recovery cues; ambiguous diarization IDs stay out of sentiment, and bilingual scripted demo turns should score from the translated English override when available so the Spanish competitor complaint still moves the timeline.
- **Known live limitation:** Rep-first post-accept diarization can still misattribute roles in ACS if the customer is silent before accept; acceptable for scripted demo, but live production trust still needs participant-identity-based attribution.
