// Live transcript wiring: connects the rep dashboard to the backend SignalR pipeline so a real
// ACS phone call's transcript streams into the transcript column. Drives the connection-state
// machine: Disconnected -> Connecting -> Transcription active -> (Ended) -> Disconnected.
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

    // Header fields that must stay in sync with the live connection state (the top bar).
    const summaryEl = root.querySelector("[data-conn-summary]");
    const callIdEl = root.querySelector("[data-meta-callid]");
    const customerEl = root.querySelector("[data-meta-customer]");
    const connectedEl = root.querySelector("[data-meta-connected]");

    // Transcript body placeholders.
    const pendingEl = scroller.querySelector("[data-live-pending]");

    // Live side-rail surfaces.
    const sentimentEmptyEl = root.querySelector("[data-live-sentiment-empty]");
    const sentimentBodyEl = root.querySelector("[data-live-sentiment-body]");
    const sentimentScoreEl = root.querySelector("[data-live-sentiment-score]");
    const sentimentStateEl = root.querySelector("[data-live-sentiment-state]");
    const sentimentMeterEl = root.querySelector("[data-live-sentiment-meter]");
    const sentimentToneEl = root.querySelector("[data-live-sentiment-tone]");
    const sentimentTrendEl = root.querySelector("[data-live-sentiment-trend]");
    const sentimentUpdatedEl = root.querySelector("[data-live-sentiment-updated]");
    const sentimentSummaryEl = root.querySelector("[data-live-sentiment-summary]");

    const knowledgeEmptyEl = root.querySelector("[data-live-knowledge-empty]");
    const knowledgeListEl = root.querySelector("[data-live-knowledge-list]");

    const WAITING = "Waiting for call";
    const nearBottomThreshold = 80;

    const STATE_CLASSES = {
        disconnected: "conn-status--disconnected",
        connecting: "conn-status--connecting",
        pending: "conn-status--pending",
        live: "conn-status--live",
        ended: "conn-status--ended"
    };

    let currentCallId = null;
    let isCallActive = false;   // true only after callAccepted; gates transcript/sentiment rendering
    let ghostLine = null;
    let endedTimer = null;
    let translationPanelCounter = 0;
    let pendingKnowledgeEvent = null;
    const lineByUtterance = new Map();
    const translationByUtterance = new Map();
    const expandedTranslationUtterances = new Set();

    function setText(el, value) {
        if (el) {
            el.textContent = value;
        }
    }

    function setRootState(callState, callId = null) {
        root.setAttribute("data-live-call-state", callState);
        if (callId) {
            root.setAttribute("data-live-call-id", callId);
        } else {
            root.removeAttribute("data-live-call-id");
        }
    }

    function dispatchRepEvent(eventName, detail = {}) {
        document.dispatchEvent(new CustomEvent(eventName, { bubbles: false, detail }));
    }

    function setHidden(el, hidden) {
        if (!el) {
            return;
        }

        if (hidden) {
            el.setAttribute("hidden", "");
        } else {
            el.removeAttribute("hidden");
        }
    }

    function toDisplayLabel(value, fallback = "Unknown") {
        if (!value) {
            return fallback;
        }

        return value
            .split(/[-_\s]+/)
            .filter((token) => token.length > 0)
            .map((token) => token.charAt(0).toUpperCase() + token.slice(1).toLowerCase())
            .join(" ");
    }

    function normalizeUtteranceId(value) {
        const normalized = (value || "").trim();
        return normalized.length > 0 ? normalized : null;
    }

    function resetHeader() {
        setText(summaryEl, "Live mode • Waiting for call");
        setText(callIdEl, WAITING);
        setText(customerEl, WAITING);
        setText(connectedEl, WAITING);
    }

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
            requestAnimationFrame(() => {
                scroller.scrollTop = scroller.scrollHeight;
            });
        }
    }

    function clearEmptyState() {
        const empty = scroller.querySelector("[data-live-empty]");
        if (empty) {
            empty.remove();
        }
    }

    function showEmptyState() {
        if (!scroller.querySelector("[data-live-empty]")) {
            const p = document.createElement("p");
            p.className = "console-empty-state";
            p.setAttribute("data-live-empty", "");
            p.textContent = "Waiting for a call. Transcription will appear here in real time once a call connects.";
            scroller.prepend(p);
        }
        setHidden(pendingEl, true);
    }

    function showPendingState() {
        clearEmptyState();
        setHidden(pendingEl, false);
    }

    function hidePendingState() {
        setHidden(pendingEl, true);
    }

    function clearTranscript() {
        scroller.innerHTML = "";
        ghostLine = null;
        lineByUtterance.clear();
        translationByUtterance.clear();
    }

    function formatTime(timestampUtc) {
        const date = timestampUtc ? new Date(timestampUtc) : new Date();
        if (Number.isNaN(date.getTime())) {
            return "";
        }

        return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
    }

    function resetSentimentState() {
        setHidden(sentimentEmptyEl, false);
        setHidden(sentimentBodyEl, true);
        setHidden(sentimentSummaryEl, true);
        setText(sentimentScoreEl, "--");
        setText(sentimentStateEl, "Awaiting sentiment");
        setText(sentimentToneEl, WAITING);
        setText(sentimentTrendEl, "Awaiting sentiment");
        setText(sentimentUpdatedEl, WAITING);

        if (sentimentMeterEl) {
            sentimentMeterEl.style.width = "0%";
            sentimentMeterEl.className = getSentimentVisualClass(50);
        }
    }

    function resetKnowledgeState() {
        if (!knowledgeListEl) {
            return;
        }

        knowledgeListEl.innerHTML = "";
        setHidden(knowledgeEmptyEl, false);
        setHidden(knowledgeListEl, true);
    }

    function clearLiveCallArtifacts() {
        isCallActive = false;
        currentCallId = null;
        setRootState("idle");
        ghostLine = null;
        lineByUtterance.clear();
        translationByUtterance.clear();
        expandedTranslationUtterances.clear();
        pendingKnowledgeEvent = null;
        scroller.innerHTML = "";
        showEmptyState();
        resetSentimentState();
        resetKnowledgeState();
    }

    function getSentimentVisualClass(scorePercent) {
        if (scorePercent <= 30) {
            return "sentiment-meter-bar sentiment-meter-bar--negative";
        }

        if (scorePercent < 55) {
            return "sentiment-meter-bar sentiment-meter-bar--caution";
        }

        if (scorePercent < 75) {
            return "sentiment-meter-bar sentiment-meter-bar--steady";
        }

        return "sentiment-meter-bar sentiment-meter-bar--positive";
    }

    function getSentimentStateLabel(scorePercent) {
        if (scorePercent <= 30) {
            return "Escalation risk";
        }

        if (scorePercent < 55) {
            return "Needs recovery";
        }

        if (scorePercent < 75) {
            return "Stabilizing";
        }

        return "Positive momentum";
    }

    function isEnglishLanguage(languageCode) {
        return !languageCode || languageCode.toLowerCase().startsWith("en");
    }

    function setTranslationExpanded(button, panel, isExpanded) {
        const showLabel = button.getAttribute("data-show-label") || "Show translation";
        const hideLabel = button.getAttribute("data-hide-label") || "Hide translation";
        const label = isExpanded ? hideLabel : showLabel;

        if (isExpanded) {
            panel.removeAttribute("hidden");
        } else {
            panel.setAttribute("hidden", "");
        }

        button.setAttribute("aria-expanded", isExpanded ? "true" : "false");
        button.setAttribute("aria-label", label);
        button.textContent = label;
    }

    function registerExpandedTranslation(utteranceId, isExpanded) {
        if (!utteranceId) {
            return;
        }

        if (isExpanded) {
            expandedTranslationUtterances.add(utteranceId);
        } else {
            expandedTranslationUtterances.delete(utteranceId);
        }
    }

    function ensureTranslationHost(line) {
        let host = line.querySelector("[data-live-translation-host]");
        if (host) {
            return host;
        }

        host = document.createElement("div");
        host.className = "transcript-tags";
        host.setAttribute("data-live-translation-host", "true");
        line.appendChild(host);
        return host;
    }

    function ensureTranslationStatus(line, message) {
        const host = ensureTranslationHost(line);
        let status = host.querySelector("[data-live-translation-status]");
        if (!status) {
            status = document.createElement("span");
            status.className = "transcript-action-disabled";
            status.setAttribute("data-live-translation-status", "true");
            host.appendChild(status);
        }

        status.textContent = message;
        return status;
    }

    function applyTranslationEventToLine(utteranceId, translationEvent) {
        const line = lineByUtterance.get(utteranceId);
        if (!line || !translationEvent || !translationEvent.translatedText) {
            return;
        }

        const host = ensureTranslationHost(line);
        host.querySelector("[data-live-translation-status]")?.remove();

        let button = line.querySelector("[data-live-translation-toggle]");
        let panel = line.querySelector("[data-live-translation-panel]");

        if (!panel) {
            translationPanelCounter += 1;
            const panelId = `live-translation-${translationPanelCounter}`;

            panel = document.createElement("section");
            panel.id = panelId;
            panel.className = "translation-panel";
            panel.setAttribute("data-live-translation-panel", "true");
            panel.setAttribute("aria-label", "Translation");
            panel.setAttribute("hidden", "");

            const label = document.createElement("p");
            label.className = "translation-label";
            label.textContent = `Translation (${toDisplayLabel(translationEvent.targetLanguage, "English")})`;
            panel.appendChild(label);

            const text = document.createElement("p");
            text.setAttribute("lang", "en");
            text.className = "mb-0";
            panel.appendChild(text);

            line.appendChild(panel);

            const contextLabel = line.getAttribute("data-live-translation-context") || "this turn";
            const showLabel = `Show English translation for ${contextLabel}`;
            const hideLabel = `Hide English translation for ${contextLabel}`;

            button = document.createElement("button");
            button.type = "button";
            button.className = "btn btn-link transcript-action-link";
            button.setAttribute("data-translation-toggle", "true");
            button.setAttribute("data-live-translation-toggle", "true");
            button.setAttribute("data-live-utterance-id", utteranceId);
            button.setAttribute("data-translation-target", panelId);
            button.setAttribute("data-show-label", showLabel);
            button.setAttribute("data-hide-label", hideLabel);
            button.setAttribute("aria-controls", panelId);
            button.setAttribute("aria-expanded", "false");
            button.setAttribute("aria-label", showLabel);
            button.textContent = showLabel;
            host.appendChild(button);
        }

        const textEl = panel.querySelector("p.mb-0");
        if (textEl) {
            textEl.textContent = translationEvent.translatedText;
        }

        const shouldExpand = expandedTranslationUtterances.has(utteranceId);
        setTranslationExpanded(button, panel, shouldExpand);
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

        if (!isEnglishLanguage(evt.detectedLanguage)) {
            text.setAttribute("lang", evt.detectedLanguage.toLowerCase());
        }

        line.appendChild(meta);
        line.appendChild(text);

        if (!isInterim) {
            const utteranceId = normalizeUtteranceId(evt.utteranceId);
            const contextLabel = evt.speakerDisplayLabel && evt.sequence
                ? `${evt.speakerDisplayLabel} turn ${evt.sequence}`
                : `turn ${evt.sequence || ""}`.trim();
            line.setAttribute("data-live-translation-context", contextLabel || "this turn");

            if (utteranceId) {
                lineByUtterance.set(utteranceId, line);
                if (!isEnglishLanguage(evt.detectedLanguage)) {
                    ensureTranslationStatus(line, "Translation unavailable");
                }

                const existingTranslation = translationByUtterance.get(utteranceId);
                if (existingTranslation) {
                    applyTranslationEventToLine(utteranceId, existingTranslation);
                }
            }
        }

        return line;
    }

    function onTranscript(evt) {
        if (!evt || !evt.text) {
            return;
        }

        if (!isCallActive) {
            const transcriptCallId = evt.callId || currentCallId;
            if (!transcriptCallId || (currentCallId && transcriptCallId !== currentCallId)) {
                return;
            }

            // Replay/live transcript is the earliest trustworthy signal that the call is already
            // accepted when /api/calls/active can only tell us that a call exists.
            onCallAccepted({ callId: transcriptCallId, source: "transcript" });
        }

        if (endedTimer) {
            clearTimeout(endedTimer);
            endedTimer = null;
        }

        setState("live", "Speech services transcribing");
        setText(summaryEl, "Live feed active • Transcribing");
        clearEmptyState();

        const wasNearBottom = isNearBottom();

        if (evt.isFinal) {
            if (ghostLine) {
                ghostLine.remove();
                ghostLine = null;
            }

            scroller.appendChild(buildLine(evt, false));
        } else if (ghostLine) {
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

    function onTranslation(evt) {
        if (!evt) {
            return;
        }

        const utteranceId = normalizeUtteranceId(evt.utteranceId);
        if (!utteranceId) {
            return;
        }

        translationByUtterance.set(utteranceId, evt);
        applyTranslationEventToLine(utteranceId, evt);
    }

    function onSentiment(evt) {
        if (!isCallActive) {
            return;
        }

        if (!evt || typeof evt.score !== "number") {
            return;
        }

        const clamped = Math.max(-1, Math.min(1, evt.score));
        const scorePercent = Math.round(((clamped + 1) / 2) * 100);

        setHidden(sentimentEmptyEl, true);
        setHidden(sentimentBodyEl, false);
        setText(sentimentScoreEl, String(scorePercent));
        setText(sentimentStateEl, getSentimentStateLabel(scorePercent));
        setText(sentimentToneEl, toDisplayLabel(evt.label, "Unknown"));
        setText(sentimentTrendEl, toDisplayLabel(evt.trend, "Unknown"));
        setText(sentimentUpdatedEl, formatTime(evt.timestampUtc) || "Now");

        if (sentimentMeterEl) {
            sentimentMeterEl.style.width = `${scorePercent}%`;
            sentimentMeterEl.className = getSentimentVisualClass(scorePercent);
        }

        if (sentimentSummaryEl) {
            const summary = `Tone is ${toDisplayLabel(evt.label, "unknown").toLowerCase()} and ${toDisplayLabel(evt.trend, "steady").toLowerCase()}.`;
            setText(sentimentSummaryEl, summary);
            setHidden(sentimentSummaryEl, false);
        }
    }

    function appendKnowledgeCardItem(card) {
        if (!knowledgeListEl) {
            return;
        }

        const item = document.createElement("li");
        const header = document.createElement("div");
        header.className = "assist-card-header";

        const title = document.createElement("strong");
        title.textContent = card.title || "Knowledge card";
        header.appendChild(title);

        if (Number.isFinite(card.rank) && card.rank > 0) {
            const rank = document.createElement("span");
            rank.className = "assist-rank";
            rank.textContent = `Priority ${card.rank}`;
            header.appendChild(rank);
        }

        item.appendChild(header);

        if (card.snippet) {
            const snippet = document.createElement("p");
            snippet.className = "assist-card-copy";
            snippet.textContent = card.snippet;
            item.appendChild(snippet);
        }

        const metaParts = [card.citationLabel, card.sourceSection].filter(Boolean);
        if (metaParts.length > 0) {
            const meta = document.createElement("p");
            meta.className = "assist-card-meta";
            meta.textContent = metaParts.join(" • ");
            item.appendChild(meta);
        }

        if (Array.isArray(card.matchedEvidence) && card.matchedEvidence.length > 0) {
            const evidenceList = document.createElement("ul");
            evidenceList.className = "assist-evidence-list";
            evidenceList.setAttribute("aria-label", "Matched evidence");

            card.matchedEvidence.forEach((evidence) => {
                const evidenceItem = document.createElement("li");

                const kind = document.createElement("span");
                kind.className = "assist-evidence-kind";
                kind.textContent = toDisplayLabel(evidence.kind, "Match");
                evidenceItem.appendChild(kind);

                const detail = document.createElement("span");
                detail.className = "assist-evidence-detail";

                const transcriptText = evidence.transcriptText || "";
                const normalizedText = evidence.normalizedText || "";
                const matchedKnowledgeText = evidence.matchedKnowledgeText || "";
                let message = transcriptText ? `“${transcriptText}”` : "Matched customer phrase";
                if (normalizedText && transcriptText.trim().toLowerCase() !== normalizedText.trim().toLowerCase()) {
                    message += ` → “${normalizedText}”`;
                }
                if (matchedKnowledgeText) {
                    message += ` matched “${matchedKnowledgeText}”`;
                }

                detail.textContent = message;
                evidenceItem.appendChild(detail);
                evidenceList.appendChild(evidenceItem);
            });

            item.appendChild(evidenceList);
        }

        if (card.sourceUrl) {
            try {
                const sourceUrl = new URL(card.sourceUrl, window.location.origin);
                if (sourceUrl.protocol === "http:" || sourceUrl.protocol === "https:") {
                    const link = document.createElement("a");
                    link.className = "assist-source-link";
                    link.href = sourceUrl.toString();
                    link.target = "_blank";
                    link.rel = "noopener noreferrer";
                    link.textContent = "View source";
                    item.appendChild(link);
                }
            } catch {
                // Ignore malformed URLs from upstream mock/live feeds.
            }
        }

        knowledgeListEl.appendChild(item);
    }

    function onKnowledgeCards(evt) {
        if (!knowledgeListEl || !evt || !Array.isArray(evt.cards)) {
            return;
        }

        if (!evt.callId || !currentCallId || evt.callId !== currentCallId) {
            return;
        }

        if (!isCallActive) {
            pendingKnowledgeEvent = evt;
            return;
        }

        pendingKnowledgeEvent = null;
        knowledgeListEl.innerHTML = "";
        evt.cards
            .slice()
            .sort((left, right) => {
                const leftRank = Number.isFinite(left.rank) && left.rank > 0 ? left.rank : Number.MAX_SAFE_INTEGER;
                const rightRank = Number.isFinite(right.rank) && right.rank > 0 ? right.rank : Number.MAX_SAFE_INTEGER;
                if (leftRank !== rightRank) {
                    return leftRank - rightRank;
                }

                return String(left.title || "").localeCompare(String(right.title || ""));
            })
            .forEach((card) => appendKnowledgeCardItem(card));

        const hasCards = evt.cards.length > 0;
        setHidden(knowledgeEmptyEl, hasCards);
        setHidden(knowledgeListEl, !hasCards);
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

    // stream.callPending — customer is ringing; backend has answered but rep has NOT accepted.
    // Keep transcript/sentiment gated and placeholders visible until rep acceptance.
    async function onCallPending(evt) {
        const callId = evt && evt.callId;
        if (!callId) {
            return;
        }

        if (endedTimer) {
            clearTimeout(endedTimer);
            endedTimer = null;
        }

        isCallActive = false;
        currentCallId = callId;
        setRootState("pending", callId);
        ghostLine = null;
        lineByUtterance.clear();
        translationByUtterance.clear();
        expandedTranslationUtterances.clear();

        // Wipe any old transcript so nothing flashes through.
        scroller.innerHTML = "";
        if (pendingEl) {
            scroller.appendChild(pendingEl);
        }
        showPendingState();
        resetSentimentState();
        resetKnowledgeState();

        setState("live", "Speech services connected");
        setText(summaryEl, "Live mode • Waiting for rep acceptance");
        setText(callIdEl, callId);
        setText(customerEl, "Inbound caller");
        setText(connectedEl, WAITING);
        dispatchRepEvent("rep.callPending", { callId, source: "signalr" });

        // Subscribe to the call's SignalR group now so transcript events arrive the moment
        // the rep accepts (backend starts emitting them post-accept).
        await subscribeToCall(callId);
    }

    // stream.callAccepted — rep accepted; begin rendering transcript and sentiment.
    function onCallAccepted(evt) {
        const callId = (evt && evt.callId) || currentCallId;
        if (!callId) {
            return;
        }

        if (endedTimer) {
            clearTimeout(endedTimer);
            endedTimer = null;
        }

        currentCallId = callId;
        isCallActive = true;
        setRootState("accepted", callId);

        hidePendingState();
        clearEmptyState();

        setState("live", "Speech services ready");
        setText(summaryEl, "Live mode • Transcription active");
        setText(callIdEl, callId);
        setText(customerEl, "Inbound caller");
        setText(connectedEl, formatTime());
        dispatchRepEvent("rep.callAccepted", { callId, source: "signalr" });

        if (pendingKnowledgeEvent && pendingKnowledgeEvent.callId === callId) {
            onKnowledgeCards(pendingKnowledgeEvent);
        }
    }

    function onCallEnded(evt) {
        if (evt && evt.callId && currentCallId && evt.callId !== currentCallId) {
            return;
        }

        isCallActive = false;
        currentCallId = null;
        setRootState("ended");
        ghostLine = null;
        lineByUtterance.clear();
        translationByUtterance.clear();
        expandedTranslationUtterances.clear();

        setState("live", "Speech services connected");
        setText(summaryEl, "Live mode • Call ended");

        // Signal rep-phone.js to hang up the ACS call leg if it is still active,
        // ensuring the mic/audio capture stops regardless of how the call ended.
        document.dispatchEvent(new CustomEvent("rep.callEnded", { bubbles: false }));

        endedTimer = setTimeout(() => {
            endedTimer = null;
            scroller.innerHTML = "";
            showEmptyState();
            resetSentimentState();
            resetKnowledgeState();
            setRootState("idle");
            setState("live", "Speech services connected");
            resetHeader();
        }, 4000);
    }

    async function resync() {
        // Catch a call that started during the SignalR handshake / before this client connected,
        // or re-join the group after an automatic reconnect (groups don't survive reconnect).
        // The active-call endpoint only proves that a call exists, not whether the rep already
        // accepted it, so we resync into pending and let callAccepted/transcript replay promote
        // the UI when the backend emits a stronger signal.
        try {
            const response = await fetch(apiBaseUrl + "/api/calls/active", { cache: "no-store" });
            if (!response.ok) {
                return;
            }

            const data = await response.json();
            if (data && data.callId) {
                await onCallPending({ callId: data.callId });
            }
        } catch (err) {
            console.debug("live-transcript: resync skipped.", err);
        }
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(apiBaseUrl + "/hubs/pipeline")
        .withAutomaticReconnect()
        .build();

    connection.on("stream.callPending", onCallPending);
    connection.on("stream.callAccepted", onCallAccepted);
    connection.on("stream.callEnded", onCallEnded);
    connection.on("stream.transcript", onTranscript);
    connection.on("stream.translation", onTranslation);
    connection.on("stream.sentiment", onSentiment);
    connection.on("stream.knowledgeCards", onKnowledgeCards);

    connection.onreconnecting(() => {
        setState("connecting", "Speech services reconnecting…");
        setText(summaryEl, "Live mode • Reconnecting…");
    });

    connection.onreconnected(() => {
        clearLiveCallArtifacts();
        setState("live", "Speech services connected");
        resetHeader();
        resync();
    });

    connection.onclose(() => {
        clearLiveCallArtifacts();
        setState("disconnected", "Speech services disconnected");
        setText(summaryEl, "Live mode • Connection lost");
        setText(callIdEl, WAITING);
        setText(customerEl, WAITING);
        setText(connectedEl, WAITING);
    });

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const toggleButton = target.closest("[data-live-translation-toggle='true']");
        if (!(toggleButton instanceof HTMLElement)) {
            return;
        }

        const utteranceId = normalizeUtteranceId(toggleButton.getAttribute("data-live-utterance-id"));
        window.setTimeout(() => {
            registerExpandedTranslation(utteranceId, toggleButton.getAttribute("aria-expanded") === "true");
        }, 0);
    });

    async function start() {
        try {
            await connection.start();
            resetSentimentState();
            resetKnowledgeState();
            setState("live", "Speech services connected");
            resetHeader();
            await resync();
        } catch (err) {
            console.warn("live-transcript: connection failed; retrying in 5s.", err);
            setState("connecting", "Speech services reconnecting…");
            setText(summaryEl, "Live mode • Reconnecting…");
            setTimeout(start, 5000);
        }
    }

    start();
})();
