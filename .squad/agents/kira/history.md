# Kira — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** Propane-retention domain — playbook, churn signal taxonomy, mocked knowledge articles + mock customer data, next-best-action framing, and the demo call script.
- **Churn definition:** Customer decides to stop buying propane from this company.
- **Constraints:** Keep data/knowledge mocked to the simplest case. Demo may be scripted.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

- The demo should feel like a real save, not a generic AI reveal: start calm, hit a missed-delivery complaint, then recover with a concrete retention offer.
- Churn risk should move with specific propane signals the rep would actually hear: competitor flyer, price shock, delivery failure, and contract-end language.
- The mock customer record can stay tiny and still be believable: tenure, plan type, delivery history, prior complaint, contract status, and value tier are enough for the churn agent.
- The knowledge layer should surface short, action-ready snippets: budget plan, apology/credit, competitor response, and auto-delivery benefits.
- Next-best-action guidance should reduce rep effort by giving one clear empathetic move plus the exact save offer to mention next.
- For live retention, translation is the bigger unlock when language is a barrier; diarization is mostly an analytics/compliance enabler unless the audio is truly mixed.
- If we must cut one in phase 1, cut diarization only if channel separation already gives attribution; otherwise keep it because customer-only signal attribution matters.
- **Team update (2026-06-05):** The propane-retention demo now rides the C#/.NET + Razor + SignalR stack, with real ACS Option A and `transcript.detectedLanguage` keeping language handling and churn signals coherent.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Helped align the transcript diarization, ad hoc translation, sentiment, and mission-control health story with the retention demo.

- **2026-06-11T15:36:11.935-04:00 — Synthetic answer dataset:** Added a service-agnostic JSONL format corpus at `src/CallCenterTranscription.Ai/Knowledge/synthetic-agent-assist-knowledge.v1.jsonl` so future search/RAG ingestion can index one standalone propane knowledge item per line.
- **2026-06-11T15:36:11.935-04:00 — Record shape:** The dataset stays flat and retrieval-friendly with answer content plus rep-only fields like `rep_guidance`, `next_best_action`, `customer_intents`, and `trigger_phrases`; the companion schema lives at `src/CallCenterTranscription.Ai/Knowledge/synthetic-agent-assist-knowledge.schema.json`.
- **2026-06-11T15:36:11.935-04:00 — User preference:** Keep synthetic CX data focused on realistic live-call answers for propane reps, not implementation wiring.

- **2026-06-11T16:49:37.710-04:00 — Demo script packaging:** Added `samples/agent-assist-demo-scripts.v1.json` with three deterministic propane support conversations that map transcript turns to knowledge-card IDs and rep-visible guidance for implementation.
- **2026-06-11T16:49:37.710-04:00 — Script design learning:** The cleanest agent-assist demo uses one broad retention save plus two narrower support flows, with primary knowledge triggers attached to customer turns and no more than one or two cards expected at once.
