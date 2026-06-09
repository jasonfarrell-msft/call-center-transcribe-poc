# Scripted Demo Regression Baseline (Issue [040])

This baseline freezes the current deterministic **propane retention** scenario so future live-mode work can be compared against known-good behavior.

## Required validation gate

```bash
dotnet build CallCenterTranscription.sln
dotnet test CallCenterTranscription.sln
```

Current known baseline: `dotnet test` is green at 20/20 tests.

## Scripted scenario baseline (must stay stable unless intentionally revised)

Call metadata:
- `callId`: `call-propane-retention-0001`
- `sessionId`: `session-propane-retention-0001`
- `scenarioName`: `Propane retention - missed delivery save`
- `callStartUtc`: `2026-06-07T00:10:18Z`

Transcript turn order:

| Seq | Timestamp (UTC) | Speaker | Language | Expected content |
|-----|------------------|---------|----------|------------------|
| 1 | `2026-06-07T00:10:18Z` | Speaker 1 (agent) | en | Agent verifies customer identity and opens support call. |
| 2 | `2026-06-07T00:10:26Z` | Speaker 2 (customer) | en | Customer reports missed delivery and bill jump. |
| 3 | `2026-06-07T00:10:33Z` | Speaker 2 (customer) | es | Customer mentions NorthStar Propane lower-price flyer (Spanish utterance). |
| 4 | `2026-06-07T00:10:42Z` | Speaker 1 (agent) | en | Agent offers service credit + budget billing. |
| 5 | `2026-06-07T00:10:50Z` | Speaker 2 (customer) | en | Customer accepts credit/budget billing and agrees to stay with Valley Fuel. |

## Translation baseline (click-to-reveal behavior)

- Only the non-English utterance (`utt-0003`, transcript sequence 3) exposes a translation action in the transcript timeline.
- Translation panel is hidden by default and only appears after user interaction with the **Show English translation** control.
- Revealed translation text is:  
  `"Also, I got a flyer from NorthStar Propane with a much lower price."`

## Sentiment + save outcome baseline

- Sentiment events progress as:
  - sequence 2 context: `negative` / `worsening` (`score: -0.74`)
  - sequence 3 context: `negative` / `steady` (`score: -0.78`)
  - sequence 5 context: `mixed` / `improving` (`score: 0.28`)
- Final sentiment summary remains:
  - `overallLabel: cooling_down`
  - `trend: improving`
  - summary describes de-escalation after service credit + budget billing acceptance.
- Save outcome is explicit in final customer turn: customer stays with Valley Fuel if credit and budget billing are applied.

## Mission Control baseline

- Mission Control overall state remains `degraded`.
- `IsMockFeedActive = true`.
- `AcsMediaRoutesLiveReady = false`.
- Summary must continue to communicate that mock feed is active and ACS callback/media routes are deferred/not live-ready.
- Component expectations include:
  - `mock-feed` status `mock`
  - `acs-media-routes` status `deferred`

## Minimum manual smoke path

### Local validation

- API:
  - `GET /healthz` returns OK.
  - `GET /api/session/current` returns `call-propane-retention-0001`.
  - `GET /api/events/transcript` returns 5 ordered scripted turns.
  - `GET /api/events/translation` returns 1 translation correlated to transcript sequence 3.
  - `GET /api/events/sentiment` returns improving summary.
  - `GET /api/mission-control/health` reports degraded/mock/deferred status.
- Web:
  - Homepage renders **Call-Center Representative Console** (no scaffold placeholder).
  - Transcript shows diarized turns in order.
  - Spanish turn has a reveal control and reveals translation only after click.
  - Sentiment card shows tone/trend as non-color-only text.
  - Mission Control shows **Mock feed active** and ACS deferred/not-live-ready state.

### Disconnected/degraded behavior checks

- Disconnected: run Web with missing/empty `BackendApi:BaseUrl`; UI reports backend disconnected and shows feed warnings.
- Degraded: simulate partial feed failure; UI reports backend degraded and preserves explicit warning copy.

### Deployed validation

- Verify deployed Web and API `/healthz` endpoints.
- Recheck the same scripted-call, translation, sentiment, and Mission Control expectations against deployed endpoints/UI.

## Future regression additions (explicit place for new issues)

When a future issue changes expected behavior, append a new checklist item here before implementation starts:

- [ ] `[ISSUE-ID]` Add/adjust scripted baseline step(s): _describe the new expected behavior and validation proof_.
