# 2026-06-06T15:29:41.673-04:00 — AZD API Docker context set to repo root

- **By:** Meyrin
- **Decision proposal:** Keep `azure.yaml` API service as `project: src/CallCenterTranscription.Api` with `docker.path: ./Dockerfile`, and set `docker.context: ../..` so Docker builds from repository root.
- **Why this matters to team:** `CallCenterTranscription.Api` depends on sibling projects (`Shared`, `Ai`, `Telephony`), so project-local Docker contexts cannot copy all build inputs. Root context keeps restore/publish deterministic for local and CI `azd` workflows.
- **Operational note:** API Dockerfile publishes .NET 9 app on port `8080` and runs as non-root runtime user (`USER $APP_UID`).
- **Source evidence:** `azure.yaml`, `src/CallCenterTranscription.Api/Dockerfile`, `src/CallCenterTranscription.Api/CallCenterTranscription.Api.csproj`.
