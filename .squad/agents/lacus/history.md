# Lacus — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** The intelligence layer — diarization, translation, continual sentiment, churn-risk agent, RAG over mocked knowledge, and next-best-action generation.
- **Model policy:** Mid-tier, **MAI models preferred**, via latest GA Azure AI Foundry. Ground outputs; explain churn scores.
- **Domain grounding:** Propane retention. Source playbook + churn signals from Kira.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

### 2026-06-05 — Translation trigger should hang off detected language

- Azure AI Speech can surface detected language when ConversationTranscriber is configured with auto language identification and a candidate-language list, so `transcript` can carry `detectedLanguage` even if UI translation is deferred. For this POC, keep backend translation for non-English AI processing, but let the rep UI request/show translation on demand.

### 2026-06-05 — Speech diarization + translation constraint

- Azure AI Speech **ConversationTranscriber** gives us real-time STT with speaker attribution, but Azure speech-to-text translation runs through **TranslationRecognizer**, which is a separate recognizer path. So for this POC, we keep diarized STT in the live stream and, when an utterance is non-English, call **Azure Translator Text** on the recognized utterance text to emit a paired translation event without losing diarization.

### 2026-06-05 — AI Pipeline Design Decisions

1. **Model choice: MAI-DS-R1** — Aligned with Athrun's proposal. Mid-tier MAI reasoning model via Azure AI Foundry Inference API. Used for churn-risk scoring (chain-of-thought), next-best-action generation, and RAG synthesis. NOT used for STT/diarization/sentiment (those are deterministic Azure AI Speech services).

2. **Deterministic vs. LLM split** — Critical latency decision: Azure AI Speech SDK handles real-time STT + diarization, and Azure Translator Text handles per-utterance translation when needed. Only churn/NBA/RAG-synthesis hit the LLM, and those are debounced (not every utterance).

3. **Sentiment: Azure AI Language (Text Analytics)** — Deterministic service call, not LLM. Runs per-utterance. Returns document/sentence-level sentiment + confidence. Cheaper and faster than an LLM call for this.

4. **Churn explainability** — Score 0-100 with top-3 contributing signals. Each signal cites a specific transcript moment (utterance index + speaker). Signal taxonomy defined with Kira: price_complaint, competitor_mention, service_issue, delivery_issue, contract_end_language, negative_sentiment_trend, escalation_request.

5. **RAG approach** — In-memory vector search over ~20 mocked propane knowledge articles (embedded at startup). No Azure AI Search needed for POC — keeps infra minimal. Articles authored by Kira in JSON. Embedding via Azure AI Foundry text-embedding model.

6. **Event-driven pipeline** — All pipeline outputs are JSON events emitted over WebSocket. Meyrin's backend streams them; Lunamaria's UI renders progressively. Events: `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, `next_best_action`.

7. **Fallback/resilience** — If any LLM call exceeds 5s timeout, emit a "computing…" placeholder and retry once. If STT stalls, fall back to scripted transcript from mock data. Demo never hard-fails.

8. **Key files (planned):**
   - `src/ai/pipeline.ts` — orchestrator
   - `src/ai/speech.ts` — Azure AI Speech STT/diarization/translation wrapper
   - `src/ai/sentiment.ts` — Text Analytics wrapper
   - `src/ai/churn-agent.ts` — churn-risk reasoning agent
   - `src/ai/rag.ts` — in-memory vector search + synthesis
   - `src/ai/nba.ts` — next-best-action generator
   - `src/ai/events.ts` — event type definitions (contracts)
   - `data/knowledge-articles.json` — Kira's mocked KB
   - `data/customer-profiles.json` — mocked customer history

### 2026-06-06 — Sweden Central AI resource floor

- The current POC can stay on three AI resources: Speech in `swedencentral`, a Foundry resource + project + one reasoning deployment in `swedencentral`, and Translator only as a **Global** single-service resource if we want keyless translation auth. Regional Translator endpoints still block Microsoft Entra auth.
- Speech keyless auth needs a custom domain plus `Cognitive Services User`, and the same RBAC pattern applies to the Foundry reasoning resource.
- Low Foundry quota assumptions only hold if `IReasoningClient` receives final/coalesced transcript turns instead of every interim STT hypothesis.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Contributed the intelligence-layer view for transcript diarization, ad hoc translation, sentiment, and mission-control health.

### 2026-06-08 — Speech Consumer BackgroundService (audio→transcript, managed-identity auth)

**What was built:**

`SpeechTranscriptionService : BackgroundService` in `src/CallCenterTranscription.Api/Services/`.

- **Pattern:** Reads `IAsyncEnumerable<AudioFrame>` from `IAudioSource` (injected — resolves MockAudioSource or AcsAudioSource depending on `AudioSource:Mode`). Writes PCM payload bytes into a `PushAudioInputStream` (format: PCM 16-bit, 16,000 Hz, mono — exact match to `AudioFrame` contract). Feeds a `SpeechRecognizer` in continuous recognition mode.
- **SignalR output:** `Recognizing` events → `TranscriptEvent{IsFinal=false}`, `Recognized` events → `TranscriptEvent{IsFinal=true}`. Both pushed via `IHubContext<PipelineHub>.Clients.Group("call:{callId}").SendAsync(PipelineContract.StreamNames.Transcript, ...)`. Exact same method (`"stream.transcript"`) and DTO (`TranscriptEvent`) the UI already consumes — zero frontend changes required.
- **Auth:** `DefaultAzureCredential` → AAD token for `https://cognitiveservices.azure.com/.default`. Token formatted as `"aad#{speechResourceId}#{aadToken}"` for custom-domain keyless auth. Set on `SpeechConfig` via `FromEndpoint(uri)` + `AuthorizationToken`. **NO subscription key anywhere.** Token refreshed every 9 minutes via `PeriodicTimer` (AAD tokens live ~1 hour). Refresh failure is logged as warning but non-fatal; existing token remains valid.
- **Config keys:** `Speech:Endpoint` (custom domain URL), `Speech:Region`, `Speech:ResourceId` (ARM resource ID for `aad#` token), `Speech:CandidateLanguages` (optional; defaults `en-US`), `Speech:DefaultCallId` (fallback group name if no live ACS call).
- **Graceful degradation:** Missing `Speech:Endpoint`/`Speech:Region` → log warning, return immediately (no crash). AAD token failure → log warning, return immediately. Scripted feed REST endpoints and SignalR UI path are unaffected in both cases.
- **Call ID routing:** `ActiveCallStore` singleton captures the ACS `CallConnectionId` when `AnswerCallAsync` succeeds in `AcsEndpoints.cs`. `SpeechTranscriptionService` reads it to route transcript events to `"call:{callId}"` group. Cleared when the WebSocket media stream ends.
- **DemoSafety guard removed:** The `DemoSafety:DataMode` startup throw in `Program.cs` was removed per Athrun's go-live spec — it was a Phase 1 safety net now superseded by the `AudioSource:Mode` DI swap.
- **Package added:** `Microsoft.CognitiveServices.Speech` v1.50.0 (latest GA as of 2026-06-08) added to `CallCenterTranscription.Api.csproj`.
- **Coexistence:** Scripted feed REST endpoints (`/api/events/*`) and the scripted scenario service are untouched. `SpeechTranscriptionService` is independent — it pushes over SignalR only when speech recognition produces results. In Mock mode (default), `MockAudioSource` yields one zero-frame then completes; the service starts/stops silently with no UI impact.
- **Build result:** `dotnet build CallCenterTranscription.sln -c Release --nologo` → **succeeded, 0 errors**.

**Residual TODOs (deferred per Athrun's spec):**
- Diarization / speaker attribution — currently emits `"unknown"` speaker. Will require `ConversationTranscriber` (separate recognizer path) in a future sprint.
- Language auto-detect via `AutoDetectSourceLanguageConfig` candidate list — currently uses first language from `Speech:CandidateLanguages`. Full multi-language auto-detect deferred.
- Confidence score parsing — Speech SDK returns confidence in JSON result property; not wired up for POC.
- `BackgroundService` does not auto-restart if `ExecuteAsync` returns after Mock completes. In ACS mode this is fine (AcsAudioSource channel stays open across calls). For Mock mode, the service exits after the single zero-frame; restart requires app restart. Acceptable for POC.

## 2026-06-08T19:24:21Z — Orchestration Log & Session Completion

**Decision committed to decisions.md:**
- `lacus-speech-consumer-built` — SpeechTranscriptionService consumer (commit 7426ebe) (merged to decisions.md)

**Orchestration log created:**
- `.squad/orchestration-log/2026-06-08T19-24-21Z-lacus.md` (Speech Consumer BackgroundService)

**Session log created:**
- `.squad/log/2026-06-08T19-24-21Z-acs-go-live-build.md` (PENDING: 6-step go-live sequence + fallback)

**Inbox files merged & deleted:**
- 6 inbox files merged into decisions.md (decisions.md: 120583 → 131795 bytes)
- `.squad/decisions/inbox/` cleared

**All .squad/ files committed to git** (staged via surgical `git add` per policy).

## Learnings

### 2026-06-10 — Sentiment Stream Analysis (Mixed vs Customer-Only)

- **Industry standard confirmed:** In production CCaaS (Genesys, NICE, Amazon Connect Contact Lens, Verint) rep and customer sentiment are *always* scored separately. Customer score → CX/churn. Agent score → coaching/QA. No platform collapses them into one metric.
- **Lexicons are most vulnerable to speaker pollution.** "Sorry," "frustrated," "terrible" all score negative regardless of speaker role context. A rep apology ("I'm so sorry to hear that") will drive the meter down in a pure lexicon — this is the exact moment the customer is being well-served.
- **The EMA (α=0.4) provides some buffer** against single rep utterances dominating, but it does not fix the structural problem. A rep who speaks with emotional language across multiple turns will persistently bias the rolling score.
- **ConversationTranscriber is the correct upgrade path**, not Unmixed audio (which failed in R1 and is out of scope). ConversationTranscriber operates on Mixed audio with diarization enabled — no topology risk, just a recognizer class swap.
- **For a retention/churn POC specifically:** the sentiment meter's *only* purpose is the customer's emotional trajectory. Any rep voice in the input degrades the signal's interpretability and trustworthiness for downstream NBA/churn agents. Do not let it ship permanently without the customer-only filter.
- **Practical POC reality:** Customer speaks ~70% of the words in a 2-party retention call. Rep scripted phrases mostly score neutral. For a live demo, the mixed signal is "directionally correct." Label it explicitly as a known compromise, not a design choice.

## Learnings

### 2026-06-10 — ConversationTranscriber swap: API surface, heuristic, and accept-gate

- **ConversationTranscriber is a drop-in recognizer swap on Mixed audio.** Same `SpeechConfig`, same `AutoDetectSourceLanguageConfig`, same `AudioStreamFormat.GetWaveFormatPCM(16000,16,1)` push stream. Only the class name, start/stop method names (`StartTranscribingAsync`/`StopTranscribingAsync`), and event names (`Transcribing`/`Transcribed`) change. Namespace: `Microsoft.CognitiveServices.Speech.Transcription`. `ConversationTranscriptionResult` inherits `SpeechRecognitionResult` so `AutoDetectSourceLanguageResult.FromResult(result)` continues to work with no signature change.

- **First-speaker-is-customer heuristic works for ACS Option A topology.** Because the customer is on the stream before the rep is added via `AddParticipant`, the first non-"Unknown" SpeakerId in a `Transcribed` event is always the customer. This is a closure variable (`customerSpeakerId`) per call session — latched on first observation, never changed. The heuristic is deterministic, explainable, and requires zero extra infrastructure. Document it explicitly; it is a POC shortcut (production should use ACS participant role mapping).

- **"Unknown" SpeakerIds must never be scored.** ConversationTranscriber emits `"Unknown"` for audio frames where diarization is uncertain (overlap, silence, background noise). Scoring those utterances would pollute the customer signal unpredictably. Always gate on `IsSpeakerKnown(speakerId)` before sentiment.

- **Accept-gate placement: inside event handler, not in ResolveGroup.** `RepAccepted` is a volatile bool that changes mid-call. Checking it at the top of each `Transcribing`/`Transcribed` handler is the correct place — the transcriber must warm up and latch the customer SpeakerId even before accept, so gating the entire recognizer start is wrong. Only the SignalR *emission* is gated.

- **Transcript events cover both speakers; sentiment is customer-only at the call site.** The correct design is to emit `stream.transcript` for all attributions (so the UI shows the full conversation with speaker labels), and call `_liveSentiment.Append(...)` only in the `isCustomer == true` branch. Do not change `LiveSentimentStore` — the customer filter belongs in the orchestration layer.

- **`TranscriptEvent` already has `SpeakerId`, `SpeakerDisplayLabel`, `SpeakerRole`, `SpeakerLabelSource` fields.** No shared schema change was needed. `SpeakerLabelSource = "conversation-transcriber-diarization"` replaces the previous `"acs-unmixed-customer"` placeholder.

## 2026-06-10 — Rep Call-Control: ConversationTranscriber + Customer-Only Sentiment (Commit 17a18c0)

**Shipped in parallel with Dyakka/Lunamaria/Athrun/Yzak.**

- **ConversationTranscriber swap complete:** Swapped SpeechRecognizer → ConversationTranscriber on Mixed stream. R1 topology (16kHz mono push) preserved. No regressions (build 0 errors).
- **Customer-only sentiment gated by RepAccepted:** First non-Unknown speaker latched as customer. All sentiment scoring filtered to customer utterances only via `IsCustomerSpeaker()` gate in event handlers.
- **Accept-gate placed in event handler:** `RepAccepted` bool check at top of `Transcribing`/`Transcribed` handlers. Transcriber warms up and latches customer SpeakerId pre-accept; only emission is gated.
- **Decision documented:** `lacus-conversationtranscriber-impl.md` + `lacus-sentiment-stream-analysis.md` (merged to decisions.md).
- **Test result:** 51 pass, 3 skip, 0 fail. No regression.

## Learnings

### 2026-06-10 — Speaker Label Flip Bug: Root Cause and Fix

**Bug:** Rep and Customer transcript labels were consistently swapped in live testing. Customer-only sentiment was scoring the rep's audio.

**Root cause:** `ConversationTranscriber` assigns Guest IDs by diarization cluster — NOT chronological audio arrival. The "first `Transcribed` event = Customer" heuristic failed when the customer was silent on hold and the rep said the first complete utterance after accepting. The rep's greeting became the first final result, latching the rep as Customer.

**New approach — two-slot phase-aware attribution (`SpeakerAttributionState`):**
- `RepAccepted` (set by `MarkAccepted()` on `AddParticipantSucceeded`) is the phase boundary
- Pre-accept speakers = Customer (rep physically absent from Mixed stream — definitive)
- Post-accept, customer already latched → new speaker = Rep
- Post-accept, neither latched (Phase 2B): first speaker = Rep (greeting), second = Customer
- Slots are write-once per call lifetime; no flip after resolution

**Key files/regions:**
- `src/CallCenterTranscription.Api/Services/SpeakerAttributionState.cs` — new testable state machine (internal sealed)
- `src/CallCenterTranscription.Api/Services/SpeechTranscriptionService.cs` — `WireTranscriberHandlers()` — uses `SpeakerAttributionState`; old `IsSpeakerKnown`/`IsCustomerSpeaker` static helpers removed
- `tests/CallCenterTranscription.Tests/SpeakerAttributionStateTests.cs` — 14 unit tests; `Phase2B_RepSpeaksFirstPostAccept_IsLatchedAsRep` encodes the exact flip scenario
- `InternalsVisibleTo` added to Api.csproj to expose `SpeakerAttributionState` to test project

**Decision note:** `.squad/decisions/inbox/lacus-speaker-label-fix.md`  
**Commit:** `cf3694e`  
**Test result:** 75 pass, 3 skip, 0 fail (all new tests pass)

**Production path:** Replace with deterministic ACS participant identity mapping — Unmixed audio (per-frame `participantRawID`) or ACS participant role API. POC heuristic is pragmatic and correct for the rep-greets-first call pattern.
