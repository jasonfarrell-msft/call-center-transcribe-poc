// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const consoleRefreshRootSelector = "[data-console-refresh-root='true']";
    const consoleRefreshRegionSelector = "[data-console-refresh-region]";
    const translationToggleSelector = "[data-translation-toggle='true']";
    const transcriptScrollerSelector = "[data-transcript-scroll='true']";
    const missionControlScrollerSelector = ".mission-control-scroller";
    const nearBottomThreshold = 80;

    function isHtmlElement(value) {
        return value instanceof HTMLElement;
    }

    function getTranslationTarget(button) {
        const targetId = button.getAttribute("data-translation-target");
        if (!targetId) {
            return null;
        }

        const panel = document.getElementById(targetId);
        return isHtmlElement(panel) ? panel : null;
    }

    function getTranslationLabels(button) {
        return {
            show: button.getAttribute("data-show-label") ?? "Show translation",
            hide: button.getAttribute("data-hide-label") ?? "Hide translation"
        };
    }

    function setTranslationExpanded(button, panel, isExpanded) {
        const labels = getTranslationLabels(button);
        const label = isExpanded ? labels.hide : labels.show;

        if (isExpanded) {
            panel.removeAttribute("hidden");
        } else {
            panel.setAttribute("hidden", "");
        }

        button.setAttribute("aria-expanded", isExpanded ? "true" : "false");
        button.textContent = label;
        button.setAttribute("aria-label", label);
    }

    function toggleTranslation(button) {
        const panel = getTranslationTarget(button);
        if (!panel) {
            return;
        }

        setTranslationExpanded(button, panel, panel.hasAttribute("hidden"));
    }

    function getFocusRestoreKey(root) {
        if (!(document.activeElement instanceof HTMLElement) || !root.contains(document.activeElement)) {
            return null;
        }

        const activeElement = document.activeElement;

        if (activeElement.matches(translationToggleSelector)) {
            return {
                type: "translation-toggle",
                value: activeElement.getAttribute("data-translation-target")
            };
        }

        if (activeElement.matches(transcriptScrollerSelector)) {
            return { type: "transcript-scroller" };
        }

        if (isHtmlElement(activeElement.closest(missionControlScrollerSelector))) {
            return { type: "mission-control-scroller" };
        }

        if (activeElement.id) {
            return {
                type: "element-id",
                value: activeElement.id
            };
        }

        return null;
    }

    function restoreFocus(root, focusRestoreKey) {
        if (!focusRestoreKey) {
            return;
        }

        let target = null;
        switch (focusRestoreKey.type) {
            case "translation-toggle":
                if (focusRestoreKey.value) {
                    target = root.querySelector(`${translationToggleSelector}[data-translation-target="${focusRestoreKey.value}"]`);
                }
                break;
            case "transcript-scroller":
                target = root.querySelector(transcriptScrollerSelector);
                break;
            case "mission-control-scroller":
                target = root.querySelector(missionControlScrollerSelector);
                break;
            case "element-id":
                if (focusRestoreKey.value) {
                    target = root.querySelector(`#${CSS.escape(focusRestoreKey.value)}`);
                }
                break;
            default:
                break;
        }

        if (isHtmlElement(target)) {
            target.focus({ preventScroll: true });
        }
    }

    function captureTranscriptState(root) {
        const scroller = root.querySelector(transcriptScrollerSelector);
        if (!isHtmlElement(scroller)) {
            return null;
        }

        const openTranslationPanelIds = Array.from(root.querySelectorAll(".translation-panel[id]:not([hidden])"))
            .filter(isHtmlElement)
            .map((panel) => panel.id);
        const distanceFromBottom = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight;

        return {
            nearBottom: distanceFromBottom <= nearBottomThreshold,
            scrollTop: scroller.scrollTop,
            openTranslationPanelIds
        };
    }

    function restoreTranscriptState(root, state) {
        if (!state) {
            return;
        }

        state.openTranslationPanelIds.forEach((panelId) => {
            const panel = document.getElementById(panelId);
            const button = root.querySelector(`${translationToggleSelector}[data-translation-target="${panelId}"]`);
            if (isHtmlElement(panel) && isHtmlElement(button)) {
                setTranslationExpanded(button, panel, true);
            }
        });

        const scroller = root.querySelector(transcriptScrollerSelector);
        if (!isHtmlElement(scroller)) {
            return;
        }

        requestAnimationFrame(() => {
            if (state.nearBottom) {
                scroller.scrollTop = scroller.scrollHeight;
                return;
            }

            const maxScrollTop = Math.max(scroller.scrollHeight - scroller.clientHeight, 0);
            scroller.scrollTop = Math.min(state.scrollTop, maxScrollTop);
        });
    }

    function captureMissionControlState(root) {
        const scroller = root.querySelector(missionControlScrollerSelector);
        if (!isHtmlElement(scroller)) {
            return null;
        }

        return {
            scrollTop: scroller.scrollTop
        };
    }

    function restoreMissionControlState(root, state) {
        if (!state) {
            return;
        }

        const scroller = root.querySelector(missionControlScrollerSelector);
        if (!isHtmlElement(scroller)) {
            return;
        }

        requestAnimationFrame(() => {
            const maxScrollTop = Math.max(scroller.scrollHeight - scroller.clientHeight, 0);
            scroller.scrollTop = Math.min(state.scrollTop, maxScrollTop);
        });
    }

    function replaceRefreshRegions(root, nextRoot) {
        const regionNames = Array.from(root.querySelectorAll(consoleRefreshRegionSelector))
            .map((region) => region.getAttribute("data-console-refresh-region"))
            // Skip empty region names: in live mode the header/columns wrappers render an empty
            // data-console-refresh-region (they are driven by the live SignalR stream, not the
            // scripted poll), so they must be excluded from server-markup replacement.
            .filter((value, index, values) =>
                typeof value === "string" &&
                value.trim().length > 0 &&
                values.indexOf(value) === index);

        regionNames.forEach((regionName) => {
            const currentRegion = root.querySelector(`[data-console-refresh-region="${regionName}"]`);
            const nextRegion = nextRoot.querySelector(`[data-console-refresh-region="${regionName}"]`);
            if (currentRegion && nextRegion) {
                currentRegion.replaceWith(nextRegion.cloneNode(true));
            }
        });
    }

    function initializeTranscriptScroll(root) {
        const scroller = root.querySelector(transcriptScrollerSelector);
        if (!isHtmlElement(scroller)) {
            return;
        }

        requestAnimationFrame(() => {
            scroller.scrollTop = scroller.scrollHeight;
        });
    }

    function initializeConsoleRefresh(root) {
        // Explicit product decision: in live mode the representative console is fully SignalR-
        // driven, so we retire HTML polling rather than running both paths in parallel.
        if (root.getAttribute("data-live-mode") === "true") {
            return;
        }

        const intervalMs = Number.parseInt(root.getAttribute("data-console-refresh-interval-ms") ?? "4000", 10);
        if (!Number.isFinite(intervalMs) || intervalMs <= 0) {
            return;
        }

        let refreshInFlight = false;

        const refreshConsole = async () => {
            if (refreshInFlight || document.visibilityState === "hidden") {
                return;
            }

            refreshInFlight = true;
            try {
                const response = await fetch(window.location.href, {
                    headers: { "X-Requested-With": "XMLHttpRequest" },
                    cache: "no-store"
                });

                if (!response.ok) {
                    console.debug("Representative console refresh skipped.", { status: response.status });
                    return;
                }

                const markup = await response.text();
                const nextDocument = new DOMParser().parseFromString(markup, "text/html");
                const nextRoot = nextDocument.querySelector(consoleRefreshRootSelector);
                if (!isHtmlElement(nextRoot)) {
                    return;
                }

                const transcriptState = captureTranscriptState(root);
                const missionControlState = captureMissionControlState(root);
                const focusRestoreKey = getFocusRestoreKey(root);
                replaceRefreshRegions(root, nextRoot);
                restoreTranscriptState(root, transcriptState);
                restoreMissionControlState(root, missionControlState);
                restoreFocus(root, focusRestoreKey);
            } catch (error) {
                console.debug("Representative console refresh skipped.", error);
            } finally {
                refreshInFlight = false;
            }
        };

        window.setInterval(refreshConsole, intervalMs);
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const translationButton = target.closest(translationToggleSelector);
        if (isHtmlElement(translationButton)) {
            toggleTranslation(translationButton);
        }
    });

    const refreshRoot = document.querySelector(consoleRefreshRootSelector);
    if (!isHtmlElement(refreshRoot)) {
        return;
    }

    initializeTranscriptScroll(refreshRoot);
    initializeConsoleRefresh(refreshRoot);
})();
