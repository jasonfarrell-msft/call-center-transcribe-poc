# SKILL: Agent-Desktop Grid Layout for Real-Time Call-Center UIs

**Category:** Frontend / Layout  
**Scope:** Razor Pages / vanilla CSS (no build tooling)  
**Extracted by:** Lunamaria, 2026-06-08

---

## When to use this skill

Any time you need to build a live-assist dashboard for a call-center agent (or similar high-stakes real-time monitoring UI) — where the agent is on a call and must absorb status + transcript + recommendations in ≤2 seconds.

---

## Pattern: Full-Viewport Multi-Screen (Two-Screen Split)

Use this when a single-page app needs two distinct operational modes (e.g., live agent assist vs. supervisor/mission overview) that each require the full viewport — no scroll, no centering, no wasted margins.

```
┌──────────────────────────────────────────────────────────────┐
│  SCREEN NAV BAR  (dark, full-width, ~3rem tall)               │
│  [● Agent Console]  [Mission Control]                         │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│  ACTIVE SCREEN (fills remaining height × full width)         │
│  Only one screen visible at a time                           │
│  (hidden + aria-hidden on inactive)                          │
└──────────────────────────────────────────────────────────────┘
```

### CSS Recipe

```css
/* Shell — zero padding, full viewport */
.console-page-shell {
  display: flex;
  height: 100dvh;
  padding: 0;        /* no wrapper margins */
}

/* Root container — no max-width centering */
.rep-console {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 0;
  height: 100%;
  min-height: 0;
  width: 100%;
  /* NO max-width, NO margin: auto */
}

/* Persistent nav strip */
.screen-nav {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
  padding: 0.5rem 1rem;
  background: var(--cc-hdr-from);   /* dark navy — matches call context bar */
  border-bottom: 1px solid rgba(255,255,255,.1);
}

.screen-nav-btn {
  padding: 0.35rem 1rem;
  border: 1px solid transparent;
  border-radius: 999px;
  background: transparent;
  font-size: 0.82rem;
  font-weight: 700;
  color: var(--cc-hdr-text);   /* always full contrast — WCAG AA */
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease;
}

.screen-nav-btn[aria-current="page"] {
  background: rgba(255,255,255,.15);
  border-color: rgba(255,255,255,.35);
}

.screen-nav-btn:focus-visible {
  outline: 2px solid rgba(255,255,255,.7);
  outline-offset: 2px;
}

/* Each screen fills remaining height; internal padding managed per-screen */
.console-view {
  display: flex;
  flex: 1;
  min-height: 0;
}

.console-view--screen-a {
  flex-direction: column;
  gap: 0.75rem;
  padding: clamp(0.6rem, 1.2vw, 1.1rem);
}

.console-view--screen-b {
  padding: clamp(0.6rem, 1.2vw, 1.1rem);
}
```

### HTML Recipe

```html
<!-- Two nav buttons use existing data-console-nav-toggle hook -->
<nav class="screen-nav" aria-label="Switch screen">
  <button type="button"
          class="screen-nav-btn"
          data-console-nav-toggle="true"
          data-console-nav-target="screen-a"
          aria-current="page"
          aria-controls="screen-a">
    Screen A
  </button>
  <button type="button"
          class="screen-nav-btn"
          data-console-nav-toggle="true"
          data-console-nav-target="screen-b"
          aria-controls="screen-b">
    Screen B
  </button>
</nav>

<!-- Default active screen — no hidden attr -->
<section id="screen-a" class="console-view console-view--screen-a"
         data-console-nav-view="true" aria-labelledby="screen-a-heading">
  ...
</section>

<!-- Inactive screen — both hidden AND aria-hidden -->
<section id="screen-b" class="console-view console-view--screen-b"
         data-console-nav-view="true" aria-labelledby="screen-b-heading"
         hidden aria-hidden="true">
  ...
</section>
```

### JS Extension (extend setActiveView)

```javascript
function setActiveView(targetId, shouldFocusHeading) {
    getConsoleViews().forEach((view) => {
        if (view.id === targetId) {
            view.removeAttribute("hidden");
            view.removeAttribute("aria-hidden");
            // ... focus heading if needed
        } else {
            view.setAttribute("hidden", "");
            view.setAttribute("aria-hidden", "true");
        }
    });

    // Sync aria-current on all nav-toggle buttons
    document.querySelectorAll("[data-console-nav-toggle='true']").forEach((btn) => {
        if (!(btn instanceof HTMLElement)) { return; }
        if (btn.getAttribute("data-console-nav-target") === targetId) {
            btn.setAttribute("aria-current", "page");
        } else {
            btn.removeAttribute("aria-current");
        }
    });
}
```

### Key Rules

- **Never use `max-width` + `margin: auto` on the root container** — this letterboxes screens on wide displays.
- **Padding belongs inside the screen, not on the shell** — moves to `.console-view--*` so each screen is independently inset.
- **`hidden` + `aria-hidden="true"` on inactive screens** — belt-and-suspenders: `hidden` → `display:none` removes from layout; `aria-hidden` removes from accessibility tree.
- **Nav button text always uses full contrast color** — differentiate active/inactive with background/border only, never by making inactive text low-contrast.
- **`flex-shrink: 0` on the nav strip** — prevents it from being crushed when screens need all available height.

---

## Pattern: Three-Zone Agent Desktop

```
┌──────────────────────────────────────────────────────────┐
│  CALL CONTEXT BAR   (dark, full-width, ≤90px tall)        │
│  caller · status · timer · key metric at-a-glance         │
└──────────────────────────────────────────────────────────┘
┌───────────────────────────────┬──────────────────────────┐
│  LIVE TRANSCRIPT (flex:1)     │  ASSIST RAIL (fixed px)  │
│  speaker-turn cards           │  sentiment meter         │
│  inline translation           │  knowledge cards         │
│  scrolls to bottom            │  next-best-action        │
└───────────────────────────────┴──────────────────────────┘
```

### CSS Layout Recipe (Razor Pages)

```css
/* Page shell — fills viewport, no scroll */
.console-page-shell {
  display: flex;
  height: 100dvh;
  padding: 0;
}

.rep-console {
  display: flex;
  flex-direction: column;
  flex: 1;
  gap: 0;
  height: 100%;
  min-height: 0;
  /* NO max-width, NO margin: auto */
}

/* Full-width call context bar — must NOT flex-shrink */
.console-header {
  background: linear-gradient(130deg, #0c1e4a 0%, #1a3380 100%);
  border-radius: 1rem;
  padding: 1rem 1.25rem;
  flex-shrink: 0;   /* ← critical: prevents header compression */
}

/* Two-column content area — 80/20 focus split */
/* Use 4fr 1fr, NOT fixed px — keeps ratio intact across all viewport widths */
.console-columns {
  display: grid;
  grid-template-columns: 4fr 1fr;   /* 80% transcript / 20% metadata */
  gap: 1rem;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

/* Transcript main column */
.console-main-column {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-height: 0;
}

/* Scrollable transcript within the main column */
.transcript-scroller {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  scroll-behavior: smooth;
}

/* Metadata rail — scrollable so stacked panels work */
.console-side-column {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow-y: auto;   /* ← scrolls when knowledge cards / churn / next-step stacks */
  gap: 0.75rem;
}
```

**Critical height-chain rule:** The representative view must have `overflow: hidden` to prevent page scroll bleed-through:

```css
.console-view--representative {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;   /* ← without this, inner flex children can force page scroll */
  padding: clamp(0.6rem, 1.2vw, 1.1rem);
}
```

### Responsive Breakpoints

```css
/* Keep 80/20 at all desktop/tablet sizes */
@media (max-width: 991.98px) {
  .console-columns { grid-template-columns: 4fr 1fr; }
}

/* Mobile: slightly wider metadata column for readability */
@media (max-width: 767.98px) {
  .console-columns { grid-template-columns: 3fr 1fr; }
}
```

### Design Token Recipe

```css
:root {
  /* Call-context header (dark zone) */
  --cc-hdr-from:        #0c1e4a;
  --cc-hdr-to:          #1a3380;
  --cc-hdr-text:        #e8f0fd;
  --cc-hdr-muted:       rgba(210,225,255,.65);
  --cc-hdr-tile-bg:     rgba(255,255,255,.08);
  --cc-hdr-tile-border: rgba(255,255,255,.14);

  /* Light content zone */
  --cc-bg:        #eaeff7;
  --cc-surface:   #ffffff;
  --cc-surface-2: #f6f8fc;
  --cc-border:    #dde3ed;

  /* Status semantics — ALWAYS paired with text + shape, never color alone */
  --cc-ok:      #059669;  --cc-ok-light:    #ecfdf5;  --cc-ok-text:    #065f46;
  --cc-warn:    #d97706;  --cc-warn-light:  #fffbeb;  --cc-warn-text:  #92400e;
  --cc-danger:  #dc2626;  --cc-danger-light:#fef2f2;  --cc-danger-text:#991b1b;
}
```

### Speaker Turn Differentiation

Add a `data-speaker-role` attribute to each transcript item in server-rendered HTML:

```html
<!-- Razor -->
<li class="transcript-item" data-speaker-role="@item.SpeakerRoleLabel.ToLowerInvariant()">
```

```css
/* CSS — left border accent only, no background that fights readability */
.transcript-item[data-speaker-role="agent"] {
  background: #f0fdf4;
  border-left: 3px solid #059669;
}
.transcript-item[data-speaker-role="customer"] {
  background: #eff6ff;
  border-left: 3px solid #2563eb;
}
```

### Live Call Pulse (CSS only, no JS)

```css
.console-status::before {
  content: '';
  display: inline-block;
  width: 0.5rem; height: 0.5rem;
  border-radius: 50%;
  background: #4ade80;
  animation: cc-live-pulse 2.2s ease-in-out infinite;
}
@keyframes cc-live-pulse {
  0%, 100% { opacity: 1;   transform: scale(1);   }
  50%       { opacity: .45; transform: scale(.75); }
}
@media (prefers-reduced-motion: reduce) {
  .console-status::before { animation: none; }
}
```

---

## Key Rules for This Pattern

1. **Header is dark, content is light** — the color contrast immediately tells the agent they're on a live call.
2. **Speaker turns are color + left-border + heading color** — never color alone (WCAG AA).
3. **Status is token-based** — ok/warn/danger always get color + icon + text, never just a color.
4. **Sentiment score must be unmissable** — `font-size: clamp(2.4rem, 5.5vw, 3.6rem)` minimum; it's the single most actionable live metric.
5. **`prefers-reduced-motion` guard** on all animations — accessibility non-negotiable.
6. **No external CDN dependencies** — system font stack, inline SVG or Unicode for icons.
7. **80/20 split, not fixed px** — use `grid-template-columns: 4fr 1fr`, never a fixed `295px` sidebar. Fixed px breaks the ratio at every viewport size.
8. **Transcript column is the focus** — 80% width, full remaining height, scrolls internally. Nothing else competes with it.
9. **Metadata rail stacks, not fights** — side column is `overflow-y: auto` + `gap`; panels have natural content height (no `height: 100%`), ready for future additions.

---

## Gotchas

- `.console-side-column` should be `display: flex; flex-direction: column; min-height: 0; overflow-y: auto; gap: …` — the cards inside take natural content height and stack; the column scrolls when overflowed.
- **Do NOT use `height: 100%` on stacked cards in the side column** — it fights the flex layout and prevents future panels from being added below.
- **`overflow: hidden` on `.console-view--representative` is required** — without it, inner flex children can bleed out and force the page body to scroll.
- **`flex-shrink: 0` on `.console-header`** — without it, the dark header bar can be crushed when columns need all available height.
- `data-console-refresh-region` attributes are JS hooks for DOM-swap refresh — never rename them without updating `site.js`.
- `aria-live="polite"` on the transcript scroller is required for screen readers.
- **Mission Control nav button stays a `<button>`, not an `<a>`** — the JS click handler uses `target.closest("[data-console-nav-toggle='true']")` which works on any element. Switching to `<a>` would require `event.preventDefault()` in site.js.
