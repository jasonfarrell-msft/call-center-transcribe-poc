# 2026-06-06T15:29:41.673-04:00 — Health checks must bypass HTTPS redirect behind Azure proxies

- **By:** Meyrin
- **Decision proposal:** For API and Web hosted behind Azure reverse proxies (Container Apps/App Service), apply forwarded headers middleware before HTTPS redirection and exempt `/healthz` from redirect.
- **Why this matters to team:** This keeps platform health probes deterministic (direct 200 on `/healthz`) while preserving HTTPS redirect behavior for user-facing routes.
- **Operational note:** Both services now configure `X-Forwarded-For` and `X-Forwarded-Proto` handling in startup and keep `/healthz` mapped as an anonymous minimal endpoint.
- **Source evidence:** `src/CallCenterTranscription.Api/Program.cs`, `src/CallCenterTranscription.Web/Program.cs`.
