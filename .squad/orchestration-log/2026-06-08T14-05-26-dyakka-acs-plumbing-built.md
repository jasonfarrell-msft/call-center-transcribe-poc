# Dyakka Run 2: ACS Option C Plumbing Built

**Date:** 2026-06-08T14:05:26Z  
**Agent:** Dyakka (ACS/Telephony Specialist)  
**Phase:** Implementation — Option C code delivery  
**Deliverable:** `dyakka-acs-plumbing-built.md`

## Summary

Implemented all Option C code deliverables per Athrun's sign-off:

### Build Results
- `dotnet build CallCenterTranscription.sln -c Release` → **0 errors, 0 warnings, 25/26 tests pass**
  - 1 pre-existing UI test failure (unrelated)

### Code Delivered

1. **AcsAudioSource : IAudioSource** (`src/CallCenterTranscription.Telephony/AcsAudioSource.cs`)
   - Channel-backed (bounded 1000, DropOldest, SingleReader/Writer)
   - ReadAsync() → IAsyncEnumerable<AudioFrame> contract match
   - HandleWebSocketMessageAsync() → ACS JSON decode (AudioMetadata logged, AudioData base64→PCM frame)
   - CompleteStream() for stream termination
   - Audio format: PCM 16-bit mono 16kHz

2. **Routes** (`src/CallCenterTranscription.Api/AcsEndpoints.cs`)
   - `POST /api/events/acs/incoming-call` — SubscriptionValidationEvent handshake + IncomingCall → AnswerCall + StartMediaStreaming
   - `POST /api/events/acs/callbacks` — ACS mid-call events (200 OK)
   - WebSocket `/api/calls/media-stream` — ACS media streaming frames

3. **DI Config Swap** (`AudioSource:Mode`, env: `AudioSource__Mode`)
   - Default: `"Mock"` (MockAudioSource stays active)
   - Flip to `"Acs"` for live path (no rebuild needed)

### NuGet Packages Added
- `Azure.Communication.CallAutomation` 1.5.1 GA
- `Azure.Identity` 1.21.0

### What's Dormant Until Live Flip
- AcsAudioSource.ReadAsync() — Channel empty in Mock mode
- All three routes — never triggered without Event Grid + phone number
- CallAutomationClient — registered but never called

**Status:** Code complete, Mock stays default, 6 residual TODOs documented
