# Vendored Azure Communication Services Calling SDK

Single self-contained IIFE bundle exposing `window.ACS`.

- Source packages: `@azure/communication-calling@1.43.1`, `@azure/communication-common`
- Built offline with esbuild (IIFE, minified). All transitive deps + web-worker/WASM
  assets are inlined (no relative side-file fetches), so it loads from any same-origin path.
- Exposes: `window.ACS.CallClient`, `window.ACS.AzureCommunicationTokenCredential`,
  `window.ACS.LocalAudioStream`, `window.ACS.Features`, etc.

To rebuild: `npm i @azure/communication-calling @azure/communication-common`,
then bundle an entry that re-exports both and assigns `globalThis.ACS`.
