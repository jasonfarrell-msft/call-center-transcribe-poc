# Revision Engineer — Deployment Readiness Fixes

- **When:** 2026-06-06T15:55:10-0400
- **By:** Revision Engineer
- **What:** Updated Azure deployment artifacts to make ACA bootstrap safe with `enableApiHealthProbes=false` by default, tightened Key Vault firewall posture to `defaultAction=Deny` (`bypass=AzureServices`), and removed ACS live readiness claims by deferring Event Grid/callback/media automation until API routes are implemented.
- **Why:** Security review rejected prior artifacts for unsafe placeholder-health coupling, implied ACS live readiness without routes, and permissive Key Vault firewall defaults.
- **Impact:** Infrastructure remains provisionable for POC resource floor while avoiding false ACS-live readiness claims; post-provision gate now explicitly requires real API image deployment and `/healthz` verification before enabling ACA probes.
