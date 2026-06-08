# Decision: GitHub Actions Node20 → Node24 bump

- **Date:** 2026-06-08T12:05:43.410-04:00
- **By:** Meyrin
- **Type:** CI/CD maintenance

## Context

GitHub announced that runners will enforce Node.js 24 for all actions on 2026-06-16, and will remove Node.js 20 from runners entirely on 2026-09-16. Five actions used in `.github/workflows/deploy-frontend.yml` were pinned to major versions that still declare `using: 'node20'` in their `action.yml`, triggering deprecation warnings on every run. Four squad automation workflows also carried floating-tag `actions/checkout@v4` references (Node20 era, no SHA pin).

## Decision

Bump all five flagged actions to their current latest major that declares `using: 'node24'`, and resolve each new major tag to its exact 40-char upstream commit SHA (annotated tags dereferenced to the underlying commit). Apply the same SHA-pinning to the floating-tag checkout references in the squad workflows, promoting supply-chain posture at the same time.

## Before → After

| Action | Old SHA (abbrev) | Old version | New SHA (abbrev) | New version | Node runtime |
|--------|-----------------|-------------|-----------------|-------------|-------------|
| actions/checkout | `34e11487` | v4 | `93cb6efe` | v5 | node24 ✅ |
| actions/setup-dotnet | `67a3573c` | v4 | `9a946fdb` | v5 | node24 ✅ |
| actions/upload-artifact | `ea165f8d` | v4 | `043fb46d` | **v7** | node24 ✅ |
| actions/download-artifact | `d3f86a10` | v4 | `3e5f45b2` | **v8** | node24 ✅ |
| azure/login | `1384c340` | v2 | `532459ea` | **v3** | node24 ✅ |

> **Note on upload/download-artifact version jumps:** v5 and v6 of both artifact actions still declare `node20`. The first Node24 releases are upload-artifact **v7** (v7.0.1) and download-artifact **v8** (v8.0.1). These are the correct targets despite the version number gap from the original v4 pins.

> **Note on azure/login:** v2.x (including latest v2.3.0) still declares `node20`. The first Node24 release is **v3.0.0**, which GA'd 2026-03-17. The workflow was promoted from v2 → v3 accordingly.

## Full resolved commit SHAs

```
actions/checkout       v5   93cb6efe18208431cddfb8368fd83d5badbf9bfd
actions/setup-dotnet   v5   9a946fdbd5fb07b82b2f5a4466058b876ab72bb2
actions/upload-artifact v7  043fb46d1a93c77aae656e7c1c64a875d1fc6a0a
actions/download-artifact v8 3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c
azure/login            v3   532459ea530d8321f2fb9bb10d1e0bcf23869a43
```

## Workflows modified

| Workflow | Actions bumped |
|----------|---------------|
| `deploy-frontend.yml` | checkout, setup-dotnet, upload-artifact, download-artifact, azure/login |
| `squad-heartbeat.yml` | checkout (floating → SHA-pinned v5) |
| `squad-issue-assign.yml` | checkout (floating → SHA-pinned v5) |
| `squad-triage.yml` | checkout (floating → SHA-pinned v5) |
| `sync-squad-labels.yml` | checkout (floating → SHA-pinned v5) |

## Untouched actions (not flagged, not changed)

- `azure/webapps-deploy@v3` (`b686016b`) — not in the flagged list; still at v3/node20 era. **Residual risk:** if Azure also moves this action to Node24 enforcement, it will need a separate bump. Monitor for `azure/webapps-deploy` Node24 release.
- `actions/github-script@v7` — not flagged; still floating tag. Out of scope for this task but carries supply-chain risk from floating tag.

## Pinning rationale

SHA pinning to a 40-char commit SHA (not a floating tag) is mandatory per the project SECURITY-FIRST policy. Annotated tags (where the tag object SHA differs from the commit SHA) were dereferenced via `gh api repos/{owner}/{repo}/git/tags/{tagObjectSha}` to obtain the underlying commit SHA. This prevents tag-move supply-chain attacks where an upstream actor rewrites a tag to point at a malicious commit.

## Deadline

- Node.js 20 enforcement: **2026-06-16** (warnings upgrade to errors)
- Node.js 20 removal from runners: **2026-09-16**
- These changes were applied **2026-06-08**, 8 days ahead of the enforcement deadline.
