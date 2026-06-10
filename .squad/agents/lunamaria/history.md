# Lunamaria — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** The live agent-assist dashboard — transcript w/ speaker labels + translation, sentiment gauge, churn-risk meter, knowledge cards, next-step suggestions.
- **Constraints:** POC may be scripted; mocked data is fine. Real-time feel is the point. Accessibility required.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Prior Work Summary (2026-06-05 to 2026-06-09)

- **2026-06-08:** Two-screen full-viewport split (Index + MissionControl Razor page), 4fr 1fr grid (80/20 transcript/metadata), flex height chain, design tokens (`--cc-*` prefix), speaker-role attribution via `data-speaker-role`, persistent nav bar.
- **2026-06-08:** Mission Control promoted to separate Razor page at `/MissionControl`. JS toggle code removed. NAV bindings preserved (`data-console-nav-toggle`, `screen-nav-btn`, `aria-current="page"`).
- **2026-06-08:** 80/20 column layout with 100dvh full remaining height. Selectors: `[data-console-refresh-root]`, `[data-console-nav-view]`, `[data-transcript-scroll]`, `[data-translation-toggle]`, `.mission-control-scroller`, `.translation-panel`.
- **Frontend deploy pipeline added:** `.github/workflows/deploy-frontend.yml` (GitHub OIDC + App Service).

## 2026-06-10 — Rep Call-Control Lifecycle (Commit 17a18c0)

**Shipped with Dyakka/Lacus/Athrun/Yzak.**

- **Badge state machine:** Disconnected → Pending (amber, `stream.callPending`) → Live (green, `stream.callAccepted`) → Ended → Disconnected. Old `connecting` class retired for backend events; reserved for SignalR reconnect only. New `conn-status--pending` + `@keyframes conn-ring` animation.
- **Transcript gating via `isCallActive`:** Module-scoped bool set true in `onCallAccepted`, false in `onCallPending`/`onCallEnded`. Defense-in-depth: server + client both gate. Scroller DOM wiped on pending to prevent stale content.
- **Pending placeholder:** `data-live-pending` node shown during ring. Incoming call message until accept.
- **Decline→backend hangup:** `declineBtn` now POSTs `/rep/hangup` after `currentIncoming.reject()`. Prevents orphaning customer on answered call.
- **`rep.callEnded` CustomEvent:** Cross-module teardown bus. `live-transcript.js` dispatches on `onCallEnded`; `rep-phone.js` listens and calls `currentCall.hangUp()` if needed. Idempotent across all paths (rep/customer/reject hangup).
- **`resync()` on SignalR reconnect:** Calls `onCallPending` then `onCallAccepted` to re-subscribe and assume accepted if reconnect mid-call.
- **`stream.callStarted` retired:** No longer registered; backend doesn't emit.
- **Files changed:** `live-transcript.js`, `rep-phone.js`, `Index.cshtml`, `site.css`.
- **Test result:** 53 pass (up from 51), 0 fail, 3 skip. Badge + teardown paths fully verified.
