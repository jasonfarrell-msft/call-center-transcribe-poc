# Bicep Specialist Azure Infra Decision

- **When:** 2026-06-06T15:29:41.750-04:00
- **By:** Bicep Specialist
- **What:** Generated `infra/` Bicep for the approved Sweden Central resource group plan, but intentionally deferred Event Grid automation and Azure AI Foundry project/model deployment. The template provisions a regional Azure AI Services account now and outputs the manual follow-up steps.
- **Why:** The current app codebase does not yet expose a validated ACS incoming-call callback/WebSocket surface, so creating Event Grid resources now would be unsafe. Azure AI project/model deployment remains safer as a post-provision step than guessing unstable resource shapes or model contracts.
- **Impact:** Infrastructure is build-valid and ready for later `azure-validate` / deployment handoff, with explicit manual follow-up for ACS eventing, ACS data-plane RBAC, and AI model deployment.
