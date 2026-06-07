# 2026-06-06T15:20:19.326-04:00 — Minimal Azure deployment architecture for the Sweden Central POC

- **By:** Athrun
- **Decision proposal:** Keep the POC architecture as the thinnest viable split already accepted by Squad: **API on Azure Container Apps**, **Web on Azure App Service**, **real ACS for the live demo**, **Azure AI Speech + Translator + Language + Azure AI Foundry** for the AI path, and **mock audio** as the fallback path.

## Assumptions

1. The deployment target is **resource group `rg-callcentertranscribe-swc-mx01`**.
2. Regional Azure resources should use **`swedencentral`** unless the service itself only supports **geography/global semantics**.
3. If a required service cannot satisfy strict Sweden Central processing, that is a **named blocker/exception**, not something we silently route around.

## Must-have resources

1. **Azure Container Registry (Basic)** in `swedencentral`
   - Hosts the API container image for ACA.
2. **Azure Container Apps managed environment** in `swedencentral`
3. **One Azure Container App** for `CallCenterTranscription.Api`
   - Public HTTPS ingress enabled
   - WebSockets enabled for ACS media streaming and SignalR
   - System-assigned managed identity
   - **Demo posture:** `minReplicas=1`, `maxReplicas=1` until the real ACS path is proven multi-replica-safe
4. **One Linux App Service Plan** in `swedencentral`
   - Start at a small paid SKU suitable for one demo user path
5. **One Web App** for `CallCenterTranscription.Web`
   - System-assigned managed identity
6. **Azure AI Speech** in `swedencentral`
   - Real-time STT + diarization
7. **Azure AI Language** in `swedencentral`
   - Sentiment only; no custom authoring assumptions
8. **Azure AI Foundry** in `swedencentral`
   - One **regional standard** GPT-5.x reasoning deployment
   - One embeddings deployment if RAG embeddings stay externalized instead of purely local
   - Exact model/version/quota must be validated before deployment
9. **Azure Key Vault** in `swedencentral`
   - Required for any unavoidable secrets/certificates
   - If managed identity covers everything, it should stay nearly empty
10. **Log Analytics workspace** in `swedencentral`
11. **Application Insights** (workspace-based) in `swedencentral`
12. **Azure Communication Services resource**
   - Same resource group
   - Use the closest valid geography configuration for the service
13. **One ACS phone number asset**
   - Required for the inbound PSTN demo
14. **Event Grid subscription** from ACS `IncomingCall` to the ACA webhook
15. **Azure AI Translator**
   - Functionally required by the accepted translation decision
   - See blocker below: strict Sweden Central processing is not currently achievable with Translator Text

## Explicit blockers / region exceptions

1. **ACS is not a normal per-datacenter regional service**
   - The ACS resource is created against a **geography**, not a Sweden Central datacenter stamp.
   - Microsoft documents that ACS data **may transit or be processed in other geographies**.
   - ACS Event Grid **system topics are global** and may store event data in any Microsoft datacenter.
2. **Translator Text does not keep Europe requests inside Sweden Central**
   - Microsoft documents the Europe endpoint as processing within **France Central** and **West Europe**.
   - Translation is therefore a **functional must-have** that conflicts with a **strict Sweden Central processing** requirement.
3. **Azure AI Foundry must be pinned to an exact regional deployment**
   - “GPT-5.x” is too vague for deployment.
   - If the exact reasoning model or embedding model is not available as a **regional** deployment in `swedencentral`, treat that as a blocker instead of falling back to Data Zone or Global without approval.

## Phase-later / explicitly deferred

- Azure AI Search
- Redis / caching tier
- Cosmos DB / SQL DB / durable transcript persistence
- Blob storage beyond platform defaults
- Azure SignalR Service / Web PubSub
- VNet integration, private endpoints, WAF, Front Door
- Separate ACA services/jobs for pipeline subdivision
- Provisioned Foundry throughput
- Multi-region resiliency

## Security requirements

1. **Managed identity first**
   - ACA and App Service use system-assigned managed identity by default.
2. **No secrets in code or appsettings**
   - Any unavoidable key/certificate lives in Key Vault.
3. **Public ingress is allowed only where the demo requires it**
   - ACA exposes HTTPS webhook/callback routes and WSS media-streaming routes.
4. **Auth must be enabled in deployed environments**
   - `Security__RequireAuth=true` is the deployment target posture for API routes and SignalR once the web-to-API auth flow is chosen.
5. **Least privilege**
   - Disable ACR admin user.
   - Grant only required data-plane roles to ACA/App Service identities.
6. **Transcript privacy**
   - Avoid logging raw transcripts, phone numbers, or translated content into App Insights unless redacted and retention-bounded.

## Resource inventory for quota checks

| Resource / asset | Qty | Proposed shape | Region / scope | Quota / validation focus |
| --- | --- | --- | --- | --- |
| Resource group | 1 | `rg-callcentertranscribe-swc-mx01` | `swedencentral` | Subscription-level deployment access |
| Azure Container Registry | 1 | Basic | `swedencentral` | Registry quota, image pulls, RBAC |
| ACA managed environment | 1 | Consumption profile is sufficient to start | `swedencentral` | Environment availability |
| ACA API app | 1 | 1 replica warm during demo | `swedencentral` | CPU/memory fit, ingress, WebSockets |
| App Service Plan | 1 | Small paid Linux SKU | `swedencentral` | Instance quota, TLS/health-check support |
| Web App | 1 | Single app | `swedencentral` | App settings, identity, outbound access |
| Azure AI Speech | 1 | Standard | `swedencentral` | Real-time transcription support |
| Azure AI Language | 1 | Standard | `swedencentral` | Sentiment API throughput only |
| Azure AI Foundry reasoning deployment | 1 | GPT-5.x regional standard | `swedencentral` | Exact model/version availability + TPM quota |
| Azure AI Foundry embeddings deployment | 1 | Minimal embedding model | `swedencentral` | Regional availability + TPM quota |
| Key Vault | 1 | Standard | `swedencentral` | RBAC, secret/cert count |
| Log Analytics workspace | 1 | Pay-as-you-go | `swedencentral` | Ingestion/retention cost |
| Application Insights | 1 | Workspace-based | `swedencentral` | Sampling/retention |
| ACS resource | 1 | Voice/Call Automation capable | Geography-based, same RG | Geography fit, calling eligibility |
| ACS phone number | 1 | Inbound PSTN demo number | Service asset | Country availability, billing eligibility |
| Event Grid subscription | 1 | ACS `IncomingCall` webhook | Global/system-topic semantics | Handshake/retry behavior |
| Azure AI Translator | 1 | Text Translation | Europe processing geography | Accept EU processing or block deployment |

## Bottom line

- If the requirement means **“put every regional ARM resource in Sweden Central and keep exceptions explicit,”** this architecture is the minimal viable POC shape.
- If the requirement means **“all data must be processed only in Sweden Central,”** the current accepted scope is blocked by **ACS** and **Translator**, and deployment should stop until the requirement or scope is changed.
