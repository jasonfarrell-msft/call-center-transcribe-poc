# Lunamaria — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** The live agent-assist dashboard — transcript w/ speaker labels + translation, sentiment gauge, knowledge cards.
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

## 2026-06-10 — Remove Churn Risk & Next Best Action Cards (Commit f3cccf0)

**Requested by:** Jason

### Removed
- **Index.cshtml (live-mode branch):** Entire `<section data-live-churn-panel>` and `<section data-live-nba-panel>` markup.
- **live-transcript.js:** Six DOM selector consts (`churnEmptyEl`, `churnBodyEl`, `churnLevelEl`, `churnRationaleEl`, `churnUpdatedEl`, `nbaEmptyEl`, `nbaBodyEl`, `nbaActionEl`, `nbaReasoningEl`, `nbaConfidenceEl`, `nbaUpdatedEl`), `onChurnRisk()` function, `onNextBestAction()` function, and the `connection.on("stream.churnRisk", ...)` / `connection.on("stream.nextBestAction", ...)` SignalR registrations.
- **site.css:** `.assist-kicker`, `.assist-copy`, `.assist-meta` rules (exclusive to those two cards). Updated section comment from "churn / knowledge / NBA" to "knowledge cards".
- **WebConsoleTests.cs:** Removed `Assert.Contains("data-live-churn-panel", ...)` and `Assert.Contains("data-live-nba-panel", ...)`.

### Deliberately Kept
- **Sentiment gauge** (`data-live-sentiment-panel`, `onSentiment`, related selectors) — untouched.
- **Knowledge cards** (`data-live-knowledge-panel`, `onKnowledgeCards`, `.assist-panel`, `.assist-list`) — untouched.
- **All transcript, translation, badge, call-lifecycle JS** — untouched.
- **Backend pipeline** — Lacus's churn/NBA model logic and the SignalR event contract (`stream.churnRisk`, `stream.nextBestAction`) were NOT touched. The backend still emits these events; the frontend now simply ignores them. **Flag for future backend cleanup** by Lacus/Meyrin to stop generating churn/NBA events if they are confirmed permanently removed from the UI.

### Test Results
- `dotnet build` ✅ succeeded.
- `dotnet test`: **76 pass, 0 fail, 3 skip** — all green.

## 2026-06-11 — Demo assist UI for scripted conversations

- **Requested by:** Jason
- **What shipped:** The rep console now surfaces **knowledge guidance** in both modes: live SignalR rendering now shows rank, citation/source section, and matched evidence; scripted/mock mode now server-renders a grouped knowledge timeline by **customer turn** so the 3 approved demo scripts are immediately presentable without needing a live call.
- **Important wiring note:** `KnowledgeCardEvent` is now consumed from `/api/events/knowledge-cards` for server-rendered scripted demos, while live mode keeps using `stream.knowledgeCards` and the same enriched card shape.
- **State hygiene lesson:** For real-time side rails, transcript cleanup is not enough — sentiment and assist panels must also reset on pending/end/close/reconnect, and late events must be guarded by `callId` plus call-active state to avoid leaking prior-call guidance into the next conversation.
- **Testing:** `WebConsoleTests` now verify scripted knowledge guidance loads with translated evidence, and `dotnet test --no-restore` stayed green (**113 total, 110 passed, 3 skipped**).

## 2026-06-12 — Early Accept UI + speech-only lower badge

- **Requested by:** Jason
- **What shipped:** The rep softphone header now surfaces the incoming-call affordance as soon as `stream.callPending` lands, even before the local ACS `incomingCall` object is ready. The Accept button becomes visible immediately, stays temporarily disabled until the browser receives the real invite, and the rep can still decline during that early pending window.
- **UI contract lesson:** Split **call lifecycle** from **speech-service lifecycle**. The lower transcript badge now reports only speech-service connectivity/transcribing state, while the header call bar owns pending/accept/connected affordances.
- **Resync lesson:** `/api/calls/active` only proves that a call exists. To avoid falsely promoting pending calls to accepted on reload/reconnect, the UI now re-enters pending first and only promotes to accepted when a stronger signal arrives (`stream.callAccepted` or transcript replay). Meyrin still needs an authoritative live accepted-vs-pending contract for perfect reload semantics.
- **Testing:** `dotnet build --no-restore` and `dotnet test tests/CallCenterTranscription.Tests/CallCenterTranscription.Tests.csproj --filter "FullyQualifiedName~WebConsoleTests|FullyQualifiedName~RepCallControlTests" --no-restore` passed.
