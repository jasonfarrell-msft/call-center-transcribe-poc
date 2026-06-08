# Decision: /lib static-asset provisioning via libman + HTML no-cache middleware

**Date:** 2026-06-08T13:29:12.574-04:00
**Author:** Meyrin (Backend Dev)
**Status:** Implemented

---

## Context

Two production issues were identified on `web-cctrans-kdarok.azurewebsites.net`:

1. `/lib/bootstrap/…`, `/lib/jquery/…`, and `/lib/jquery-validation-unobtrusive/…` returned HTTP 404 because `wwwroot/lib/` contained only LICENSE files — no actual dist JS/CSS. No `libman.json` existed; the files were never restored before `dotnet publish`.
2. HTML document responses (Razor Pages) carried no `Cache-Control` header, causing Edge and other browsers to cache the HTML heuristically and show stale UI after deploys. Fingerprinted CSS/JS assets were fine; only the HTML doc went stale.

---

## Decision 1 — /lib asset provisioning via libman (not committed vendor blobs)

**Chosen approach:** libman restore at build time (option a).

**Rationale:**
- Committing minified vendor blobs bloats git history, makes security audits harder, and creates friction when updating libraries. Rejected.
- Switching to a CDN link in `_Layout.cshtml` would change markup (out of scope) and introduce a hard dependency on CDN availability at runtime. Rejected.
- libman is the intended ASP.NET Core mechanism for client-side library management. Adding a `libman.json` and a workflow restore step keeps the build reproducible without committing vendor blobs and matches how the scaffold was designed to work.

**Files created:**
- `.config/dotnet-tools.json` — pins `microsoft.web.librarymanager.cli@3.0.71` as a dotnet local tool.
- `src/CallCenterTranscription.Web/libman.json` — declares four libraries via the `jsdelivr` provider (CDN-backed, no auth required):
  - `bootstrap@5.3.3` → `wwwroot/lib/bootstrap/` (CSS + bundle JS)
  - `jquery@3.7.1` → `wwwroot/lib/jquery/` (jquery.min.js)
  - `jquery-validation@1.21.0` → `wwwroot/lib/jquery-validation/` (jquery.validate.min.js)
  - `jquery-validation-unobtrusive@4.0.0` → `wwwroot/lib/jquery-validation-unobtrusive/` (jquery.validate.unobtrusive.min.js)

**Workflow change (`.github/workflows/deploy-frontend.yml`):**
A `run:` step — no new action, no new SHA pin needed — added between "Restore web project" and "Publish web project":

```yaml
- name: Restore client-side libraries (libman)
  working-directory: src/CallCenterTranscription.Web
  run: |
    dotnet tool restore --verbosity minimal
    dotnet tool run libman restore
```

`dotnet tool restore` finds `.config/dotnet-tools.json` by traversing up from `src/CallCenterTranscription.Web`. `libman restore` reads `libman.json` in the working directory and downloads from jsdelivr into `wwwroot/lib/`. These files are then included by `dotnet publish`.

**Security note:** jsdelivr is a well-established public CDN (jsDelivr). Libraries are version-pinned in `libman.json`. The libman CLI tool version is pinned in `dotnet-tools.json`. No executable scripts are pulled without version pinning. The `run:` step uses the project's own `dotnet tool` — no new SHA-pinned action needed.

**Verification:** Local run of `dotnet tool restore` + `libman restore` + `dotnet publish -c Release` confirmed all five files present in the publish output at real size:
- `wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` — 227 KB
- `wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js` — 79 KB
- `wwwroot/lib/jquery/dist/jquery.min.js` — 85 KB
- `wwwroot/lib/jquery-validation/dist/jquery.validate.min.js` — 25 KB
- `wwwroot/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js` — 5.7 KB

---

## Decision 2 — HTML document Cache-Control: no-cache middleware

**Chosen approach:** Inline `app.Use` middleware with an `OnStarting` callback in `Program.cs`.

**Policy:** `Cache-Control: no-cache, no-store, must-revalidate` + `Pragma: no-cache` + `Expires: 0` on all `text/html` responses. `no-cache` allows the browser to store the response but requires revalidation (ETag round-trip) on every navigation — cheap and correct. `no-store` is added as belt-and-suspenders to prevent even intermediate caches from holding a stale HTML shell.

**Why not `no-cache` alone:** Both are fine; `no-store` is belt-and-suspenders for shared/enterprise proxies that may otherwise serve a stale HTML shell from cache.

**Implementation (`src/CallCenterTranscription.Web/Program.cs`):**
Placed immediately after the HSTS/exception handler block and before `app.UseRouting()`. The `OnStarting` callback fires just before the response starts writing, at which point `Content-Type` is already set by whichever endpoint handled the request. The callback gates on `ct.StartsWith("text/html", OrdinalIgnoreCase)` so:
- Razor Page responses (`text/html; charset=utf-8`) → headers set ✓
- Static asset responses (`text/css`, `application/javascript`, etc.) → headers NOT set ✓
- Health check (`/healthz`, `application/json`) → headers NOT set ✓

`MapStaticAssets()` in .NET 9 serves static files as endpoints via the routing system; `MapStaticAssets()` sets its own `Cache-Control: public, max-age=…, immutable` headers for fingerprinted assets. Because the `OnStarting` callback checks Content-Type at flush time, it never overwrites those headers.

**Middleware ordering preserved:** HSTS → html-no-cache middleware → UseRouting → UseAuthorization → /healthz → MapStaticAssets → MapRazorPages. No existing middleware was moved or removed.

---

## Alternatives considered

- **Response caching / output caching middleware for HTML:** Overly complex; would require configuring per-route policies and coordinating with .NET 9's output cache. The inline OnStarting approach is minimal and targeted.
- **Web.config / IIS headers rule on App Service:** Fragile (App Service can reset Web.config); not portable to other deployment targets. App-level middleware is the correct layer.
