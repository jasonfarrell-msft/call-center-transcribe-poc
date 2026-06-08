# Athrun Run 1: ACS Option C Architecture + Security Sign-Off

**Date:** 2026-06-08T14:05:26Z  
**Agent:** Athrun (Lead / Architect)  
**Phase:** Architecture decision gate  
**Deliverable:** `athrun-acs-option-c-signoff.md`

## Summary

Reviewed Dyakka's Option C proposal and issued **✅ APPROVE TO BUILD** with binding architectural decisions.

### Key Decisions Signed Off

1. **ACS Data-Plane RBAC Role**
   - Role: `Communication Services Contributor` (GUID: `2b4609a5-7812-4aba-b5e3-076e6a078419`)
   - Scope: Single ACS resource (not resource group)
   - Principal: ACA system-assigned managed identity
   - Justification: No narrower role covers AnswerCall + StartMediaStreaming

2. **Webhook Security**
   - Use: SubscriptionValidationEvent handshake + schema validation
   - Defer: Entra-protected delivery auth (blocked on Event Grid subscription)
   - No secrets: Zero HMAC/Key Vault for webhook (Point-in-time validation sufficient for POC)

3. **WebSocket Topology**
   - `minReplicas = 1` (param, default 1; prevents cold-start call drops)
   - `maxReplicas = 1` (single replica, session affinity moot)
   - Reconnect: No logic this round (drop = restart call, documented limitation)

4. **DI Swap Contract**
   - Config key: `AudioSource:Mode` (env: `AudioSource__Mode`)
   - Default: `"Mock"` — no behavior change
   - Activation: Flip to `"Acs"` on ACA env var (no rebuild)

5. **SDK & Auth**
   - Package: `Azure.Communication.CallAutomation` GA
   - Auth: `DefaultAzureCredential` (managed identity)
   - Zero secrets, zero connection strings in code

### Scope Gates (IN / OUT)

**IN:** AcsAudioSource, routes, DI swap, NuGet, Bicep RBAC + minReplicas + env var  
**OUT:** PSTN number, Event Grid subscription, Entra webhook auth, Audio→Speech consumer, Rep AddParticipant

**Status:** Approved; Dyakka and Meyrin may proceed with binding spec
