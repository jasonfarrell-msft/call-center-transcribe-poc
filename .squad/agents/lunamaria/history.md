# Lunamaria — History

## Project Seed

- **Project:** CallCenterTranscription — real-time AI agent-assist POC for a propane call center.
- **My focus:** The live agent-assist dashboard — transcript w/ speaker labels + translation, sentiment gauge, churn-risk meter, knowledge cards, next-step suggestions.
- **Constraints:** POC may be scripted; mocked data is fine. Real-time feel is the point. Accessibility required.
- **Requested by:** local user (git user.name not set).
- **Created:** 2026-06-05.

## Learnings

(append component patterns, streaming approach, and key file paths here)

- **2026-06-08 — Two-screen full-viewport split (Mission Control):**
  - **Architecture:** Two `.console-view` sections (`#representative-view`, `#mission-control-view`) remain siblings inside `.rep-console` (flex column). Only one is visible at a time via `hidden` + `aria-hidden="true"` on the inactive screen. The visible view gets `flex: 1` and fills all remaining height.
  - **Full-viewport sizing:** `.console-page-shell` padding is now `0`. `.rep-console` loses `max-width` and `margin: 0 auto` (was `1920px`/centered). Internal padding (`clamp(0.6rem, 1.2vw, 1.1rem)`) moved into `.console-view--representative` and `.console-view--mission` so each screen manages its own inset. Mission control's `.mission-control-panel` gains `flex: 1` so the card-shell fills the full view height.
  - **Shared screen nav:** A `<nav class="screen-nav">` strip sits at the top of `.rep-console` (before the views). Two `<button>` elements carry `data-console-nav-toggle="true"` (same selector the existing JS click handler uses). Default active button gets `aria-current="page"`.
  - **JS extension (`setActiveView`):** Extended to: (1) set `aria-hidden="true"` on hidden views and remove it from the shown view; (2) iterate all `[data-console-nav-toggle]` buttons and set `aria-current="page"` on the one pointing to the active view, removing it from others.
  - **Removed:** Per-view "Mission" and "Back to console" pill buttons — the persistent nav bar makes them redundant. All other content/hooks preserved.
  - **New IDs/classes:** `.screen-nav`, `.screen-nav-btn`, `aria-current="page"` on active nav btn.
  - **Responsive:** `@media (max-width: 767.98px)` padding override moved from `.console-page-shell` to `.console-view--representative, .console-view--mission`; `.screen-nav` and `.screen-nav-btn` get compact padding overrides.
  - **Layout architecture:** Two-zone grid (`minmax(0,1fr) 295px`) inside a flex-column rep-console. Dark navy header card (call-context bar, `linear-gradient(130deg, #0c1e4a, #1a3380)`) sits above the columns as its own flex item. Sentiment panel is a `card-shell` that fills the right column naturally — no `border-left` separator needed.
  - **Design tokens:** CSS custom properties on `:root` — prefix `--cc-` for colors/text/semantic, `--s1…s6` for spacing, `--r/r-sm/r-lg` for radius, `--sh-sm/sh/sh-lg` for elevation. Token names: `--cc-bg`, `--cc-surface`, `--cc-surface-2`, `--cc-border`, `--cc-border-strong`, `--cc-text-primary/secondary/muted`, `--cc-accent/accent-hover/accent-light`, `--cc-ok/warn/danger` with `-light/-text` variants, `--cc-hdr-from/to/text/muted/tile-bg/tile-border`.
  - **Speaker turn differentiation:** Added `data-speaker-role="@item.SpeakerRoleLabel.ToLowerInvariant()"` to each `<li class="transcript-item">` in `Index.cshtml`. CSS uses `[data-speaker-role="agent"]` (green left accent `#059669`, `#f0fdf4` bg) and `[data-speaker-role="customer"]` (blue left accent `#2563eb`, `#eff6ff` bg). Heading color also changes per role.
  - **Live pulse dot:** `.console-status::before` pseudo-element with `@keyframes cc-live-pulse` (opacity + scale oscillation, 2.2s, respects `prefers-reduced-motion`).
  - **Key JS selectors that must be preserved:**
    - `[data-console-refresh-root='true']` — `.rep-console` root div
    - `[data-console-refresh-region]` — values: `header`, `columns`, `mission`
    - `[data-console-nav-view='true']` — `#representative-view`, `#mission-control-view`
    - `[data-console-nav-toggle='true']` — Mission / Back buttons
    - `[data-translation-toggle='true']` — translation expand/collapse
    - `[data-transcript-scroll='true']` — `.transcript-scroller`
    - `.mission-control-scroller` — JS scroll-state restore
    - `.translation-panel` — JS expand/collapse toggle
  - **Key file paths:**
    - `src/CallCenterTranscription.Web/Pages/Index.cshtml`
    - `src/CallCenterTranscription.Web/wwwroot/css/site.css`
    - `src/CallCenterTranscription.Web/wwwroot/js/site.js` (unchanged)
    - `src/CallCenterTranscription.Web/Pages/Shared/_Layout.cshtml` (unchanged)

- **Team update:** POC plan drafted; shared WebSocket event contracts cover `transcript`, `translation`, `sentiment`, `churn_risk`, `knowledge_cards`, and `next_best_action`; real-time loop uses GPT-4o, with MAI-DS-R1 reserved for optional post-call analysis.
- **Translation UX (2026-06-05):** Recommended Option C (auto-translate inline + language badge + toggle to show original). Stronger demo beat, zero rep friction. Needs `detectedLanguage` field added to `transcript` event. Settings toggle deferred post-POC.
- **Team update (2026-06-05):** Frontend moved to Razor Pages/Blazor-capable UI over SignalR on App Service; translate-on-click is the chosen rep flow, and `transcript.detectedLanguage` now drives the badge/affordance.
- **2026-06-07T00:18:14Z — Frontend mission-control planning pass:** Helped shape the rep-facing surface for transcript diarization, ad hoc translation, sentiment, and mission-control health.
- **2026-06-07T01:06:00Z — First-pass rep console implementation:** Replaced scaffold homepage with a semantic call-center console in `Pages/Index.cshtml` + `Index.cshtml.cs`, wired `PipelineApiClient` to session/transcript/translation/sentiment/mission-control endpoints, used query-driven per-utterance translation reveal, and added web-facing tests in `tests/CallCenterTranscription.Tests/WebConsoleTests.cs`.
- **2026-06-07T06:29:29.980-04:00 — Frontend deploy pipeline:** Added `.github/workflows/deploy-frontend.yml` to publish `src/CallCenterTranscription.Web` on .NET 9 and deploy only the App Service web frontend. The final workflow uses GitHub OIDC federation with `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` secrets plus non-secret GitHub variables for the resource group and Web App name.
