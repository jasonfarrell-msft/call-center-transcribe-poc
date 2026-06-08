# Decision: Speech Consumer Built — SpeechTranscriptionService

**Date:** 2026-06-08T15:24:21.856-04:00
**Author:** Lacus (AI Engineer)
**Requested by:** Jason
**Status:** IMPLEMENTED
**Build:** `dotnet build CallCenterTranscription.sln -c Release --nologo` → succeeded, 0 errors

---

## What Was Built

`SpeechTranscriptionService : BackgroundService` — the audio→transcript consumer that was
the last missing critical-path piece before go-live.

**Location:** `src/CallCenterTranscription.Api/Services/SpeechTranscriptionService.cs`

**Supporting file:** `src/CallCenterTranscription.Api/Services/ActiveCallStore.cs`
(singleton that stores the live ACS `CallConnectionId` so the consumer knows which
SignalR group to push transcript events to)

---

## Consumer Design

```
BackgroundService.ExecuteAsync(stoppingToken):
  1. Guard: exit if Speech:Endpoint or Speech:Region not configured (graceful degradation)
  2. Acquire AAD token via DefaultAzureCredential (scope: cognitiveservices.azure.com/.default)
  3. Build SpeechConfig.FromEndpoint(endpoint) + set AuthorizationToken = aad#{resourceId}#{token}
  4. Create PushAudioInputStream (PCM 16-bit, 16,000 Hz, mono) + SpeechRecognizer
  5. Wire Recognizing → emit isFinal=false TranscriptEvent via SignalR
     Wire Recognized → emit isFinal=true  TranscriptEvent via SignalR
  6. Start PeriodicTimer (9 min) to refresh AAD token on recognizer.AuthorizationToken
  7. await foreach (frame in IAudioSource.ReadAsync(stoppingToken)):
       if (frame.Payload.Length > 0) pushStream.Write(frame.Payload)
  8. On stream end: pushStream.Close() → EOS to SDK → recognizer.StopContinuousRecognitionAsync()
```

**IAudioSource is injected** — the service is completely mode-agnostic. It reads the same
interface whether Mode=Mock (MockAudioSource) or Mode=Acs (AcsAudioSource). The Coordinator
flips the mode via `az containerapp update --set-env-vars AudioSource__Mode=Acs`; no code change.

---

## SignalR Output Contract

- **Hub method (stream name):** `PipelineContract.StreamNames.Transcript` = `"stream.transcript"`
- **DTO:** `TranscriptEvent` (existing, in `CallCenterTranscription.Shared.Events`)
- **Group:** `PipelineContract.GroupNames.ForCall(callId)` = `"call:{callId}"`
- **isFinal policy:**
  - `Recognizing` event → `IsFinal = false` (interim, gives "live typing" effect in UI)
  - `Recognized` event (reason=RecognizedSpeech) → `IsFinal = true` (committed utterance)
- **No UI changes required.** The `TranscriptEvent` DTO already had the `IsFinal` field.
  The scripted feed only pushed `IsFinal=true`; the consumer adds `IsFinal=false` for interim
  results. This is purely additive and back-compatible.

---

## Auth Approach — Managed Identity, NO Key

1. `DefaultAzureCredential` acquires an AAD token for
   `https://cognitiveservices.azure.com/.default`
2. Token formatted as `"aad#{speechResourceId}#{aadToken}"` for custom-domain keyless auth
   (the Speech SDK's documented format for Microsoft Entra-authenticated requests to a
   Speech resource with a custom subdomain)
3. Loaded into `SpeechConfig.FromEndpoint(new Uri(endpoint))` via `AuthorizationToken` property
4. **No `SpeechConfig.FromSubscription(key, region)` anywhere** — zero keys in code or config
5. Required RBAC: `Cognitive Services User` on the Speech resource →
   ACA system-assigned managed identity (already in Bicep per Athrun's spec)
6. Token refresh: `PeriodicTimer(9 minutes)` sets `recognizer.AuthorizationToken` in a
   background loop. Refresh failures are logged as warnings and non-fatal — existing token
   remains valid for ~1 hour

Config keys (non-secret):
- `Speech__Endpoint` — custom domain URL, e.g. `https://speechcctrans{suffix}.cognitiveservices.azure.com/`
- `Speech__Region` — e.g. `swedencentral`
- `Speech__ResourceId` — ARM resource ID for `aad#` token prefix (optional but recommended)
- `Speech__CandidateLanguages` — comma-separated, e.g. `en-US,sv-SE,de-DE,fr-FR` (optional)
- `Speech__DefaultCallId` — fallback group name when no live ACS call is active (optional)

---

## DemoSafety Guard Removal

The `DemoSafety:DataMode` startup guard in `Program.cs` was removed per Athrun's go-live
sign-off (Decision 3 + Decision 5). This guard threw `InvalidOperationException` if
`DemoSafety:DataMode != "Mock"`, blocking startup in live mode. With `SpeechTranscriptionService`
now implemented, the guard is superseded by the `AudioSource:Mode` DI swap mechanism.

The scripted propane feed (`ScriptedPropaneRetentionScenarioFeed`) and all REST endpoints
(`/api/events/*`, `/api/session/*`) remain intact and unmodified.

---

## ACS Call ID Capture

`ActiveCallStore` (singleton) is set in `AcsEndpoints.cs` after `callClient.AnswerCallAsync()`
succeeds: `callStore.SetCallId(result.Value.CallConnection.CallConnectionId)`. Cleared when
the media-stream WebSocket closes. `SpeechTranscriptionService` reads `callStore.CallId` to
route transcript events to the correct group. Falls back to `Speech:DefaultCallId` config or
`"live-call"` if no active call ID is set (safe for Mock mode where no call is answered).

---

## Package Added

`Microsoft.CognitiveServices.Speech` v1.50.0 (latest GA as of 2026-06-08) added to
`src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj`.

---

## Coexistence with Scripted Feed

- `ScriptedPropaneRetentionScenarioFeed` REST endpoints are **untouched**
- In Mock mode: `MockAudioSource` yields one zero-frame then completes → Speech SDK receives
  negligible audio → produces no transcript → service exits `ExecuteAsync` cleanly
- In Acs mode: `AcsAudioSource` Channel stays open; `SpeechTranscriptionService` reads frames
  for the duration of the call
- No conflict between the scripted feed and the consumer — separate concerns, separate paths

---

## Residual TODOs (deferred per Athrun's spec)

| Item | Status | Owner |
|------|--------|-------|
| Diarization / speaker attribution | Deferred — consumer emits `speakerId="unknown"` | Lacus (future sprint) |
| Language auto-detect (full candidate list) | Deferred — first lang from `Speech:CandidateLanguages` used | Lacus |
| Confidence score from SDK JSON result | Deferred — `null` for POC | Lacus |
| BackgroundService restart after Mock stream completes | Non-issue for ACS; acceptable for POC | — |
| `Speech__ResourceId` env var on ACA | Must be set for correct `aad#` token format | Meyrin |
| Verify `Cognitive Services User` RBAC is live on ACA MI | Blocking for go-live | Meyrin |
| Flip `AudioSource__Mode=Acs` + `minReplicas=1` | After RBAC verified + image deployed | Meyrin (per Athrun's sequence) |

---

## Go-Live Readiness

This decision closes **Step 1** of Athrun's go-live sequence. Steps 2–7 remain with Meyrin
and the Coordinator per `athrun-acs-go-live-signoff.md`.

The fallback remains: if anything in the live path fails during a demo, flip
`AudioSource__Mode=Mock` (`az containerapp update --set-env-vars AudioSource__Mode=Mock`)
and the scripted propane feed serves the demo with no transcript gap visible.
