# Session Log: Fix and Deploy — /lib 404s + HTML stale-cache

**Date:** 2026-06-08T17:40:59Z  
**Scope:** Production hotfix + review  
**Participants:** Meyrin (impl), Athrun (review)  

## Summary

Fixed two production issues on `web-cctrans-kdarok.azurewebsites.net`:

1. **libman asset provisioning** — `/lib/bootstrap`, `/lib/jquery`, `/lib/jquery-validation*` returned 404 because wwwroot/lib/ was empty. Added libman.json + dotnet-tools.json + restore step in deploy workflow. All 5 libs now present at deploy time.

2. **HTML stale-cache** — Razor Pages had no Cache-Control header, causing browsers to heuristically cache stale HTML UI after deploys. Added OnStarting middleware in Program.cs to enforce `Cache-Control: no-cache,no-store,must-revalidate` on text/html only. Static assets unaffected.

Both fixes committed (b02908c) and approved by Athrun. Resolves two follow-up items from prior session.

## Decision records

Merged to decisions.md:
- `.squad/decisions/inbox/meyrin-libassets-and-html-cache.md`
- `.squad/decisions/inbox/athrun-libassets-cache-review.md`

## Build verification

- `dotnet build CallCenterTranscription.sln` → 0 errors
- `dotnet publish -c Release` → all 5 dist files present (227 KB bootstrap CSS, 79 KB bootstrap JS, 85 KB jQuery, 25 KB validation, 5.7 KB unobtrusive)
- `node --check` on site.js → clean

## Next phase

Both fixes ready for production merge. Staged for commit via Scribe orchestration.
