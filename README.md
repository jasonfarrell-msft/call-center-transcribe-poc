# CallCenterTranscription

Phase 0 scaffold for the .NET-based call-center transcription POC.

## Projects

- `src/CallCenterTranscription.Api` — ASP.NET Core backend (SignalR + API seams)
- `src/CallCenterTranscription.Web` — Razor Pages frontend scaffold
- `src/CallCenterTranscription.Shared` — shared DTO contracts/events
- `src/CallCenterTranscription.Ai` — `IReasoningClient` abstraction + mock implementation
- `src/CallCenterTranscription.Telephony` — `IAudioSource` abstraction + mock implementation
- `tests/CallCenterTranscription.Tests` — contract and wiring smoke tests

## Run validation

```bash
dotnet build CallCenterTranscription.sln
dotnet test CallCenterTranscription.sln
```

## Notes

- Target framework is `net9.0` (SDK support confirmed locally).
- No secrets or connection strings are hardcoded; backend/frontend integration points are configuration/DI seams.
- API auth enforcement is controlled by `Security:RequireAuth` (`false` in Phase 0 scaffold, enable in Phase 1+ with real identity wiring).
- Live-data security and privacy requirements are defined in `docs/live-data-security-guardrails.md`.
