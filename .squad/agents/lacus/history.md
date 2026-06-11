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
