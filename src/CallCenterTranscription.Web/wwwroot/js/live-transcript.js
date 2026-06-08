// Live transcript wiring: connects the rep dashboard to the backend SignalR pipeline so a real
// ACS phone call's transcript streams into the transcript column. Drives the connection-state
// machine: Disconnected -> Connecting -> Live -> (Ended) -> Disconnected.
//
// Rendering strategy (reviewer-mandated): append-only committed (final) lines, plus a SINGLE
// live "ghost" line that mirrors the current interim partial. This sidesteps Speech SDK
// ResultId instability entirely — no fragile in-place upsert keyed on utteranceId.
(() => {
    const root = document.querySelector("[data-console-refresh-root='true']");
    if (!root || root.getAttribute("data-live-mode") !== "true") {
        return;
    }
    if (typeof signalR === "undefined") {
        console.warn("live-transcript: SignalR client not loaded.");
        return;
    }

    const apiBaseUrl = (root.getAttribute("data-api-base-url") || "").replace(/\/+$/, "");
    if (!apiBaseUrl) {
        console.warn("live-transcript: no API base URL configured; cannot connect.");
        return;
    }

    const statusPill = root.querySelector("[data-conn-status]");
    const statusLabel = root.querySelector("[data-conn-label]");
    const scroller = root.querySelector("#live-transcript");
    if (!scroller) {
        return;
    }

    const STATE_CLASSES = {
        disconnected: "conn-status--disconnected",
        connecting: "conn-status--connecting",
        live: "conn-status--live",
        ended: "conn-status--ended"
    };
    const nearBottomThreshold = 80;

    let currentCallId = null;
    let ghostLine = null; // the single in-progress interim line element, if any
    let endedTimer = null;

    function setState(state, labelText) {
        if (statusPill) {
            Object.values(STATE_CLASSES).forEach((cls) => statusPill.classList.remove(cls));
            statusPill.classList.add(STATE_CLASSES[state] || STATE_CLASSES.disconnected);
        }
        if (statusLabel && labelText) {
            statusLabel.textContent = labelText;
        }
    }

    function isNearBottom() {
        return scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight <= nearBottomThreshold;
    }

    function autoscroll(wasNearBottom) {
        if (wasNearBottom) {
            requestAnimationFrame(() => { scroller.scrollTop = scroller.scrollHeight; });
        }
    }

    function clearEmptyState() {
        const empty = scroller.querySelector("[data-live-empty]");
        if (empty) {
            empty.remove();
        }
    }

    function clearTranscript() {
        scroller.innerHTML = "";
        ghostLine = null;
    }

    function formatTime(timestampUtc) {
        const date = timestampUtc ? new Date(timestampUtc) : new Date();
        if (Number.isNaN(date.getTime())) {
            return "";
        }
        return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
    }

    function buildLine(evt, isInterim) {
        const line = document.createElement("div");
        line.className = "live-line" + (isInterim ? " live-line--interim" : "");

        const meta = document.createElement("div");
        meta.className = "live-line-meta";

        const speaker = document.createElement("span");
        speaker.className = "live-speaker";
        speaker.textContent = evt.speakerDisplayLabel || "Speaker";
        meta.appendChild(speaker);

        const time = document.createElement("time");
        const t = formatTime(evt.timestampUtc);
        if (t) {
            time.textContent = t;
            time.setAttribute("datetime", evt.timestampUtc || "");
            meta.appendChild(time);
        }

        const text = document.createElement("p");
        text.className = "live-text";
        text.textContent = evt.text || "";

        line.appendChild(meta);
        line.appendChild(text);
        return line;
    }

    function onTranscript(evt) {
        if (!evt || !evt.text) {
            return;
        }
        if (endedTimer) {
            clearTimeout(endedTimer);
            endedTimer = null;
        }
        setState("live", "● Live transcription");
        clearEmptyState();

        const wasNearBottom = isNearBottom();

        if (evt.isFinal) {
            // Commit: the final text supersedes any in-progress partial. Drop the ghost and
            // append a permanent line.
            if (ghostLine) {
                ghostLine.remove();
                ghostLine = null;
            }
            scroller.appendChild(buildLine(evt, false));
        } else if (ghostLine) {
            // Update the single live partial in place.
            const text = ghostLine.querySelector(".live-text");
            if (text) {
                text.textContent = evt.text;
            }
        } else {
            ghostLine = buildLine(evt, true);
            scroller.appendChild(ghostLine);
        }

        autoscroll(wasNearBottom);
    }

    async function subscribeToCall(callId) {
        if (!callId) {
            return;
        }
        try {
            await connection.invoke("SubscribeToCall", callId);
        } catch (err) {
            console.warn("live-transcript: SubscribeToCall failed.", err);
        }
    }

    async function onCallStarted(evt) {
        const callId = evt && evt.callId;
        if (!callId) {
            return;
        }
        if (endedTimer) {
            clearTimeout(endedTimer);
            endedTimer = null;
        }
        currentCallId = callId;
        clearTranscript();
        setState("connecting", "Call connected — starting transcription…");
        // Await the group join so no early transcript is missed (reviewer fix).
        await subscribeToCall(callId);
    }

    function onCallEnded(evt) {
        // Ignore stale end events for a call we are no longer tracking.
        if (evt && evt.callId && currentCallId && evt.callId !== currentCallId) {
            return;
        }
        setState("ended", "Call ended");
        currentCallId = null;
        ghostLine = null;
        endedTimer = setTimeout(() => {
            setState("disconnected", "Disconnected — waiting for call");
        }, 4000);
    }

    async function resync() {
        // Catch a call that started during the SignalR handshake / before this client connected,
        // or re-join the group after an automatic reconnect (groups don't survive reconnect).
        try {
            const response = await fetch(apiBaseUrl + "/api/calls/active", { cache: "no-store" });
            if (!response.ok) {
                return;
            }
            const data = await response.json();
            if (data && data.callId) {
                onCallStarted({ callId: data.callId });
            }
        } catch (err) {
            console.debug("live-transcript: resync skipped.", err);
        }
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(apiBaseUrl + "/hubs/pipeline")
        .withAutomaticReconnect()
        .build();

    connection.on("stream.callStarted", onCallStarted);
    connection.on("stream.callEnded", onCallEnded);
    connection.on("stream.transcript", onTranscript);

    connection.onreconnecting(() => {
        setState("connecting", "Reconnecting…");
    });
    connection.onreconnected(() => {
        setState("disconnected", "Disconnected — waiting for call");
        resync();
    });
    connection.onclose(() => {
        setState("disconnected", "Disconnected — connection lost");
    });

    async function start() {
        try {
            await connection.start();
            setState("disconnected", "Disconnected — waiting for call");
            await resync();
        } catch (err) {
            console.warn("live-transcript: connection failed; retrying in 5s.", err);
            setState("disconnected", "Disconnected — retrying…");
            setTimeout(start, 5000);
        }
    }

    start();
})();
