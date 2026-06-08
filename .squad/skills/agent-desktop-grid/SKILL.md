# SKILL: Agent-Desktop Grid Layout for Real-Time Call-Center UIs

**Category:** Frontend / Layout  
**Scope:** Razor Pages / vanilla CSS (no build tooling)  
**Extracted by:** Lunamaria, 2026-06-08

---

## When to use this skill

Any time you need to build a live-assist dashboard for a call-center agent (or similar high-stakes real-time monitoring UI) — where the agent is on a call and must absorb status + transcript + recommendations in ≤2 seconds.

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
  padding: clamp(0.6rem, 1.2vw, 1.1rem);
}

.rep-console {
  display: flex;
  flex-direction: column;
  flex: 1;
  gap: 0.75rem;
  min-height: 0;
  max-width: 1920px;
  margin: 0 auto;
}

/* Full-width call context bar */
.console-header {
  /* dark nav gradient = "live call" signal */
  background: linear-gradient(130deg, #0c1e4a 0%, #1a3380 100%);
  border-radius: 1rem;
  padding: 1rem 1.25rem;
  flex-shrink: 0;
}

/* Two-column content area */
.console-columns {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 295px;
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

/* Assist rail */
.console-side-column {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
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

---

## Gotchas

- `.console-side-column` should be `display: flex; flex-direction: column; min-height: 0` — the card inside fills the column, not the wrapper.
- `data-console-refresh-region` attributes are JS hooks for DOM-swap refresh — never rename them without updating `site.js`.
- `aria-live="polite"` on the transcript scroller is required for screen readers.
