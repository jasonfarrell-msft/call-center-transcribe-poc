# Lunamaria — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** The live agent-assist dashboard — transcript w/ speaker labels + translation, sentiment gauge, churn-risk meter, knowledge cards, next-step suggestions.
- **Constraints:** POC may be scripted; mocked data is fine. Real-time feel is the point. Accessibility required.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(empty — append component patterns, streaming approach, and key file paths here)

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Translation UX (2026-06-05):** Recommended Option C (auto-translate inline + language badge + toggle to show original). Stronger demo beat, zero rep friction. Needs `detectedLanguage` field added to `transcript` event. Settings toggle deferred post-POC.
- **Team update (2026-06-05):** Frontend moved to Razor Pages/Blazor-capable UI over SignalR on App Service; translate-on-click is the chosen rep flow, and `transcript.detectedLanguage` now drives the badge/affordance.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Helped shape the rep-facing surface for transcript diarization, ad hoc translation, sentiment, and mission-control health.
- **2026-06-07T01:06:00Z — First-pass rep console implementation:** Replaced scaffold homepage with a semantic call-center console in `Pages/Index.cshtml` + `Index.cshtml.cs`, wired `PipelineApiClient` to session/transcript/translation/sentiment/mission-control endpoints, used query-driven per-utterance translation reveal, and added web-facing tests in `tests/CallCenterTranscription.Tests/WebConsoleTests.cs`.
