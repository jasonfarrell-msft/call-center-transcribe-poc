# ACS Option C Plumbing Built
**By:** Dyakka — ACS / Telephony Specialist  
**Date:** 2026-06-08T14:05:26.525-04:00  
**Type:** Implementation Record + Residual TODOs  
**Status:** Code Complete — Mock Stays Default

---

## What Was Built

All Option C code deliverables per Athrun's sign-off (`athrun-acs-option-c-signoff.md`) are complete.  
Build result: `dotnet build CallCenterTranscription.sln -c Release --nologo` → **0 errors, 0 warnings**.

### 1. NuGet Packages

| Package | Version | Project |
|---------|---------|---------|
| `Azure.Communication.CallAutomation` | 1.5.1 GA | `CallCenterTranscription.Telephony.csproj` |
| `Azure.Identity` | 1.21.0 | `CallCenterTranscription.Api.csproj` |

---

### 2. AcsAudioSource : IAudioSource

**File:** `src/CallCenterTranscription.Telephony/AcsAudioSource.cs`

- Backed by `System.Threading.Channels.Channel<AudioFrame>` — bounded capacity 1000, `DropOldest`, `SingleReader=true`, `SingleWriter=true`.
- `ReadAsync(CancellationToken)` → `IAsyncEnumerable<AudioFrame>` via `channel.Reader.ReadAllAsync()`. Exact IAudioSource contract match.
- `HandleWebSocketMessageAsync(byte[], CancellationToken)` — parses ACS JSON (`AudioMetadata` = log only; `AudioData` = base64-decode PCM → `AudioFrame{pcm16, 16000Hz}` → `TryWrite`). Malformed frames skipped with warnings, never thrown.
- `CompleteStream()` — calls `channel.Writer.TryComplete()`. No reconnect logic (POC known limitation; document before going live).
- Audio format: PCM 16-bit mono 16,000 Hz — matches downstream IAudioSource contract defaults.

---

### 3. Routes Added (`AcsEndpoints.cs` → `src/CallCenterTranscription.Api/`)

All routes mapped by `app.MapAcsRoutes()` called from `Program.cs`.

| Route | Method | Auth | Purpose |
|-------|--------|------|---------|
| `/api/events/acs/incoming-call` | POST | AllowAnonymous | Event Grid webhook: SubscriptionValidationEvent handshake + IncomingCall → AnswerCall + StartMediaStreaming |
| `/api/events/acs/callbacks` | POST | AllowAnonymous | ACS mid-call events (CallConnected, etc.); returns 200 OK |
| `/api/calls/media-stream` | WS Upgrade | AllowAnonymous | ACS media-streaming WebSocket; feeds frames into AcsAudioSource |

**Auth exclusion:** These routes are in `app.MapGroup("/api/events/acs").AllowAnonymous()` and `app.Map(...).AllowAnonymous()` — completely outside the `AgentAssistAccess` JWT policy. Event Grid and ACS cannot present Bearer tokens.

**`app.UseWebSockets()`** added early in the middleware pipeline (before route execution).

**SubscriptionValidationEvent handling:** Route detects `eventType: "Microsoft.EventGrid.SubscriptionValidationEvent"`, extracts `data.validationCode`, returns `{ validationResponse: "..." }`. This is the Event Grid endpoint ownership proof.

**IncomingCall handling:** Answers via `CallAutomationClient` (DefaultAzureCredential, no connection strings). Sets `MediaStreamingOptions` with `MediaStreamingAudioChannel.Mixed`, `StreamingTransport.Websocket`, `MediaStreamingContent.Audio`, `StartMediaStreaming = true`, `TransportUri = wss://{host}/api/calls/media-stream`.

---

### 4. DI Config Swap

**Config key:** `AudioSource:Mode` (env: `AudioSource__Mode`)  
**Default:** `"Mock"` — nothing changes, MockAudioSource still resolves.

```
AudioSource__Mode=Mock  → IAudioSource = MockAudioSource  (DEFAULT)
AudioSource__Mode=Acs   → IAudioSource = AcsAudioSource   (live path)
```

`AcsAudioSource` is **always registered as a concrete singleton** so the WebSocket handler can inject it regardless of mode (dormant in Mock mode — Channel stays empty; no calls are answered).

`CallAutomationClient` is registered when `Acs:Endpoint` is configured — uses `DefaultAzureCredential`. The managed identity RBAC role assignment on ACA is Meyrin's Bicep deliverable.

`AddCallCenterServices` now takes `IConfiguration` (updated in `ServiceCollectionExtensions.cs` + `Program.cs` + test file).

---

## What Is DORMANT Until Live Flip

Everything below is code-complete and sitting in the codebase but produces no real-world effect until the flip:

- `AcsAudioSource.ReadAsync()` — Channel stays empty in Mock mode; no consumers will get frames.
- `/api/events/acs/incoming-call` — Present but never triggered (no Event Grid subscription, no PSTN number).
- `/api/calls/media-stream` — Present but ACS never connects (no calls answered).
- `CallAutomationClient` — Registered (when `Acs:Endpoint` configured) but never makes API calls.

---

## Config Flip to Go Live

Flip one env var on the ACA Container App and ensure infra prerequisites are met:

```
AudioSource__Mode=Acs
```

That's the only app code change needed. Prerequisites (Meyrin + portal):

1. **ACS RBAC role assignment** (Bicep): `Communication Services Contributor` on ACS resource for ACA system identity — per Athrun Decision 1.
2. **`apiMinReplicas = 1`** (Bicep) — per Athrun Decision 3. Prevents cold-start call drops.
3. **PSTN phone number** — portal provisioning (verify billing country eligibility).
4. **Event Grid subscription** — `Microsoft.Communication.IncomingCall` → `POST /api/events/acs/incoming-call` (webhook). Run the SubscriptionValidationEvent handshake first.
5. **Entra delivery authentication** on the Event Grid subscription — **blocking prerequisite for going live**. Meyrin must add this when wiring the subscription. See TODO comments in `AcsEndpoints.cs`.
6. **`Acs__Endpoint`** — already configured in ACA; no action needed.

---

## Explicit Residual TODOs

| Item | Owner | Status | Notes |
|------|-------|--------|-------|
| PSTN phone number purchase | Jason + portal | ❌ Not started | Verify Swedish billing eligibility |
| Event Grid system topic + subscription | Meyrin | ❌ Deferred | Wait until webhook validated; then wire with Entra delivery auth |
| Microsoft Entra delivery auth on Event Grid subscription | Meyrin | ❌ BLOCKING for live | Must be done before going live. See `AcsEndpoints.cs` TODO comment |
| ACS RBAC role assignment (Bicep) | Meyrin | ❌ Separate deliverable | `Communication Services Contributor` on ACS resource for ACA system identity |
| `apiMinReplicas = 1` (Bicep param) | Meyrin | ❌ Separate deliverable | Prevents cold-start call drops (30-second ring window) |
| Audio → Speech consumer (`IHostedService`) | Lacus + Meyrin | ❌ Next round | Reads `IAudioSource.ReadAsync()` and feeds to Azure AI Speech SDK |
| Rep join via `AddParticipant` | Dyakka (next round) | ❌ Not started | Requires phone number + live calls |
| Reconnect logic on WebSocket drop | Dyakka (future) | ❌ POC skip | Known limitation; restart the call for demo |
| Full ACS callback event handling | Dyakka (future) | ❌ Minimal (200 OK) | `CallConnected`, `MediaStreamingStarted` etc.; extend when needed |

---

## SDK Note (Azure.Communication.CallAutomation 1.5.1)

`MediaStreamingOptions` constructor changed from older versions:
```csharp
// 1.5.1 (current):
new MediaStreamingOptions(MediaStreamingAudioChannel.Mixed, StreamingTransport.Websocket)
{
    TransportUri           = new Uri("wss://host/api/calls/media-stream"),
    MediaStreamingContent  = MediaStreamingContent.Audio,
    StartMediaStreaming     = true
}

// OLD (pre-1.5, DO NOT USE — no longer exists):
// new MediaStreamingOptions(uri, MediaStreamingTransport.Websocket, MediaStreamingContent.Audio, MediaStreamingAudioChannel.Mixed)
```

`MediaStreamingAudioChannel`, `MediaStreamingContent`, and `StreamingTransport` are struct-based extensible enums (not C# enums) — they have static properties like `Mixed`, `Audio`, `Websocket`.
