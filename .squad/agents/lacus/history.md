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
