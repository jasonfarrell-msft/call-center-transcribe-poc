// Rep softphone: turns the rep's browser into a real ACS call participant so they can HEAR and
// TALK to the PSTN caller through their local mic/speakers, gated by an explicit Accept click.
//
// Flow:
//   1. Mint a VoIP token via the same-origin proxy (/rep/token) — the proxy injects the backend
//      shared secret so the browser never holds it. The browser persists its ACS userId so token
//      refresh (long calls) and registration survive reloads and API restarts.
//   2. Build a CallAgent and ask for mic permission.
//   3. Register (/rep/register) ONLY after the incomingCall handler is wired ("registered" =
//      ready-to-take-a-call), then re-register on a ~15s heartbeat so we reconverge after an API
//      restart. The API AddParticipant-s this identity to the answered call → the browser rings.
//   4. The native incomingCall event drives the in-console Accept/Decline bar. Accept is the gate.
(() => {
    const root = document.querySelector("[data-console-refresh-root='true']");
    if (!root || root.getAttribute("data-live-mode") !== "true") {
        return;
    }

    const ACS = window.ACS;
    if (!ACS || !ACS.CallClient || !ACS.AzureCommunicationTokenCredential) {
        console.warn("rep-phone: ACS Calling SDK not loaded.");
        return;
    }

    // The token/register endpoints are same-origin proxies on THIS web app (not the API base).
    const TOKEN_URL = "/rep/token";
    const REGISTER_URL = "/rep/register";
    const STORAGE_KEY = "rep.acs.userId";
    const HEARTBEAT_MS = 15000;
    const DISPLAY_NAME = (root.getAttribute("data-rep-display-name") || "Representative").trim() || "Representative";

    // ── UI handles ────────────────────────────────────────────────────────────────────────────
    const bar = root.querySelector("[data-rep-callbar]");
    const statusEl = root.querySelector("[data-rep-status]");
    const acceptBtn = root.querySelector("[data-rep-accept]");
    const declineBtn = root.querySelector("[data-rep-decline]");
    const muteBtn = root.querySelector("[data-rep-mute]");
    const muteLabel = root.querySelector("[data-rep-mute-label]");
    const hangupBtn = root.querySelector("[data-rep-hangup]");

    function setStatus(text) {
        if (statusEl) statusEl.textContent = text;
    }
    function show(el, visible) {
        if (el) el.hidden = !visible;
    }

    // Bar states: "idle" (registered, waiting), "ringing" (incoming), "incall", "unavailable".
    function applyState(state) {
        switch (state) {
            case "ringing":
                show(acceptBtn, true); show(declineBtn, true);
                show(muteBtn, false); show(hangupBtn, false);
                break;
            case "incall":
                show(acceptBtn, false); show(declineBtn, false);
                show(muteBtn, true); show(hangupBtn, true);
                break;
            case "unavailable":
                show(acceptBtn, false); show(declineBtn, false);
                show(muteBtn, false); show(hangupBtn, false);
                break;
            default: // idle
                show(acceptBtn, false); show(declineBtn, false);
                show(muteBtn, false); show(hangupBtn, false);
                break;
        }
    }

    // ── Identity + token ─────────────────────────────────────────────────────────────────────
    let repUserId = (() => {
        try { return localStorage.getItem(STORAGE_KEY) || ""; }
        catch { return ""; }
    })();

    function persistUserId(id) {
        repUserId = id || "";
        try { if (repUserId) localStorage.setItem(STORAGE_KEY, repUserId); }
        catch { /* private mode — in-memory only */ }
    }

    async function fetchToken() {
        const qs = repUserId ? `?userId=${encodeURIComponent(repUserId)}` : "";
        const resp = await fetch(TOKEN_URL + qs, { method: "GET", cache: "no-store" });
        if (!resp.ok) {
            throw new Error(`token endpoint returned ${resp.status}`);
        }
        const data = await resp.json();
        if (data && data.userId) persistUserId(data.userId);
        return data; // { userId, token, expiresOn }
    }

    // ── Registration heartbeat ───────────────────────────────────────────────────────────────
    let heartbeatTimer = null;
    let softphoneRegistered = false;

    async function register() {
        if (!repUserId) return false;
        try {
            const resp = await fetch(REGISTER_URL, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                cache: "no-store",
                body: JSON.stringify({ userId: repUserId })
            });
            if (!resp.ok) {
                throw new Error(`register endpoint returned ${resp.status}`);
            }
            softphoneRegistered = true;
            if (!currentCall && !currentIncoming) {
                applyState("idle");
                setStatus("Ready — waiting for a call");
            }
            return true;
        } catch (err) {
            softphoneRegistered = false;
            console.warn("rep-phone: register failed (will retry).", err);
            if (!currentCall && !currentIncoming) {
                applyState("unavailable");
                setStatus("Softphone registration lost — retrying…");
            }
            return false;
        }
    }

    function startHeartbeat() {
        if (heartbeatTimer) return;
        register();
        heartbeatTimer = setInterval(register, HEARTBEAT_MS);
        window.addEventListener("online", register);
        document.addEventListener("visibilitychange", () => {
            if (document.visibilityState === "visible") register();
        });
    }

    // ── Call lifecycle ───────────────────────────────────────────────────────────────────────
    let callAgent = null;
    let currentIncoming = null;
    let currentCall = null;

    function wireCall(call) {
        currentCall = call;
        call.on("stateChanged", () => {
            const s = call.state;
            if (s === "Connected") {
                applyState("incall");
                setStatus("On call with customer");
                syncMute();
            } else if (s === "Disconnected") {
                currentCall = null;
                applyState("idle");
                setStatus("Ready — waiting for a call");
            } else {
                setStatus(`Call ${s.toLowerCase()}…`);
            }
        });
        call.on("isMutedChanged", syncMute);
    }

    function syncMute() {
        if (!currentCall || !muteBtn) return;
        const muted = !!currentCall.isMuted;
        muteBtn.setAttribute("aria-pressed", muted ? "true" : "false");
        if (muteLabel) muteLabel.textContent = muted ? "Unmute" : "Mute";
    }

    function onIncomingCall(args) {
        const incoming = args && args.incomingCall;
        if (!incoming) return;
        // Single-call POC: auto-reject a second concurrent invite.
        if (currentCall || currentIncoming) {
            incoming.reject().catch(() => {});
            return;
        }
        currentIncoming = incoming;
        applyState("ringing");
        setStatus("Incoming call — Accept to connect");

        incoming.on && incoming.on("callEnded", () => {
            if (currentIncoming === incoming) {
                currentIncoming = null;
                if (!currentCall) { applyState("idle"); setStatus("Ready — waiting for a call"); }
            }
        });
    }

    acceptBtn && acceptBtn.addEventListener("click", async () => {
        if (!currentIncoming) return;
        try {
            setStatus("Connecting…");
            const call = await currentIncoming.accept();
            currentIncoming = null;
            wireCall(call);
        } catch (err) {
            console.error("rep-phone: accept failed.", err);
            setStatus("Could not connect the call.");
            applyState("idle");
            currentIncoming = null;
        }
    });

    declineBtn && declineBtn.addEventListener("click", async () => {
        if (!currentIncoming) return;
        try { await currentIncoming.reject(); }
        catch (err) { console.warn("rep-phone: reject failed.", err); }
        currentIncoming = null;
        applyState("idle");
        setStatus("Call declined — waiting for a call");
    });

    muteBtn && muteBtn.addEventListener("click", async () => {
        if (!currentCall) return;
        try {
            if (currentCall.isMuted) await currentCall.unmute();
            else await currentCall.mute();
        } catch (err) { console.warn("rep-phone: mute toggle failed.", err); }
        syncMute();
    });

    hangupBtn && hangupBtn.addEventListener("click", async () => {
        if (!currentCall) return;
        try { await currentCall.hangUp(); }
        catch (err) { console.warn("rep-phone: hang up failed.", err); }
    });

    // ── Bootstrap ────────────────────────────────────────────────────────────────────────────
    async function init() {
        show(bar, true);
        applyState("unavailable");
        setStatus("Starting softphone…");

        let initial;
        try {
            initial = await fetchToken();
        } catch (err) {
            console.error("rep-phone: token fetch failed.", err);
            setStatus("Softphone unavailable (no token).");
            return;
        }

        const credential = new ACS.AzureCommunicationTokenCredential({
            token: initial.token,
            refreshProactively: true,
            tokenRefresher: async () => (await fetchToken()).token
        });

        try {
            const callClient = new ACS.CallClient();
            callAgent = await callClient.createCallAgent(credential, { displayName: DISPLAY_NAME });

            const deviceManager = await callClient.getDeviceManager();
            try {
                await deviceManager.askDevicePermission({ audio: true, video: false });
            } catch (permErr) {
                console.warn("rep-phone: mic permission not granted yet.", permErr);
                setStatus("Allow microphone access to take calls.");
            }

            // "Registered" == the incomingCall handler is wired and ready.
            callAgent.on("incomingCall", onIncomingCall);
            setStatus("Registering softphone…");
            const registered = await register();
            if (!registered && !softphoneRegistered) {
                applyState("unavailable");
                setStatus("Softphone registration pending — retrying…");
            }
            startHeartbeat();
        } catch (err) {
            console.error("rep-phone: failed to initialise CallAgent.", err);
            setStatus("Softphone failed to start.");
            applyState("unavailable");
        }
    }

    init();
})();
