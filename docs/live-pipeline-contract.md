# Live pipeline contract (Phase 0.20)

This document defines the canonical contract shared by REST polling and SignalR streaming so mock and live ACS paths remain swappable.

## Canonical stream names

The backend emits the following stream names (see `PipelineContract.StreamNames`):

- `stream.transcript` (`TranscriptEvent`)
- `stream.translation` (`TranslationEvent`)
- `stream.sentiment` (`SentimentEvent`)
- `stream.churnRisk` (`ChurnRiskEvent`)
- `stream.knowledgeCards` (`KnowledgeCardEvent`)
- `stream.nextBestAction` (`NextBestActionEvent`)
- `stream.currentState` (`PipelineCurrentStateResponse`)

## SignalR hub contract

Hub route: `/hubs/pipeline`

Client subscription methods (see `PipelineContract.HubMethods` and `PipelineHub`):

- `SubscribeToCall(callId)`
- `UnsubscribeFromCall(callId)`
- `SubscribeToSession(sessionId)`
- `UnsubscribeFromSession(sessionId)`

Group naming rules (see `PipelineContract.GroupNames`):

- Call group: `call:{callId}`
- Session group: `session:{sessionId}`

## REST and live payload mapping

REST endpoints map to the same shared DTOs used by SignalR events:

- `/api/events/transcript` → `IReadOnlyList<TranscriptEvent>`
- `/api/events/translation` → `IReadOnlyList<TranslationEvent>`
- `/api/events/sentiment` → `SentimentFeedResponse` (`summary` + `events`)
- `/api/events/churn-risk` → `IReadOnlyList<ChurnRiskEvent>`
- `/api/events/knowledge-cards` → `IReadOnlyList<KnowledgeCardEvent>`
- `/api/events/next-best-action` → `IReadOnlyList<NextBestActionEvent>`
- `/api/session/current` → `SessionCurrentResponse`
- `/api/session/current-state` → `PipelineCurrentStateResponse`

No adapter layer is required in Phase 0.20 because both transport shapes are shared DTOs.

## Ordering and correlation rules

All event streams use:

- `callId`: primary partition key for in-call ordering.
- `eventId`: immutable unique event identifier per stream item.
- `sequence`: monotonic order within each event stream type for a call.
- `utteranceId`: links derived outputs to transcript utterances when applicable.

Derived streams (`translation`, `sentiment`, `churn_risk`, `knowledge_cards`, `next_best_action`) also include transcript linkage (`relatedTranscriptEventId` and/or `relatedTranscriptSequence`) when available.

## Late-join and reconnect replay behavior

When an agent opens the console mid-call or reconnects:

1. Client requests `/api/session/current-state`.
2. Server returns `PipelineCurrentStateResponse` with:
   - current call/session metadata
   - `call.startedAtUtc` captured once when the active live call starts and reused for the life of that call
   - sentiment summary
   - accumulated events for each stream, including transcript, translation, churn risk, knowledge cards, and next-best-action already produced for the active live call
   - `streamReplayPolicy` (currently `full_history_for_active_call`)
   - in the default live (`AudioSource:Mode=Acs`) startup path, no scripted interaction is preloaded: before a real call starts the payload is a waiting snapshot (`callId=""`, `state="waiting"`, `source="acs-live"`) with empty event arrays
   - scripted interaction history is returned only when mock mode is explicitly enabled with `AudioSource:Mode=Mock`
3. Client then subscribes to `call:{callId}` and optionally `session:{sessionId}` on the hub for incremental updates.
4. On subscribe, `PipelineHub` immediately emits `stream.currentState` plus replay events for all stream types to the subscribing caller so late join/reconnect does not wait for the next live event.

## Empty-array semantics

For `/api/events/transcript`, `/api/events/translation`, `/api/events/churn-risk`, `/api/events/knowledge-cards`, and `/api/events/next-best-action`:

- Empty array means **no live interaction history or qualifying model output has been produced yet for the current call**.
- Empty array does **not** indicate transport failure.
- Consumers should keep the corresponding panel in a "waiting/no signal yet" state and replace it when new events arrive.

## Mission Control mock vs live exposure

- `isMockFeedActive` in `SessionCurrentResponse` and `PipelineCurrentStateResponse` indicates mock mode.
- `MissionControlHealthResponse` remains the source for component-by-component live readiness.
- UI components should read these fields to show mock/live badges without changing payload parsing logic.

## Mode toggle contract

- API live/default path: `AudioSource:Mode=Acs`
- API scripted fallback: `AudioSource:Mode=Mock`
- Web live console path: `Frontend:LiveMode=true`
- Web scripted fallback console: `Frontend:LiveMode=false`
- To avoid mixing live header state with scripted interaction data, switch the API and web settings together when enabling the mock fallback path.
