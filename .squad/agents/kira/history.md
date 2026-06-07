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
