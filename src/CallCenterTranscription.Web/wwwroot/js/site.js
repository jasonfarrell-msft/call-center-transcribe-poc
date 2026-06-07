// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.querySelectorAll("[data-translation-toggle='true']").forEach((button) => {
    button.addEventListener("click", () => {
        const targetId = button.getAttribute("data-translation-target");
        if (!targetId) {
            return;
        }

        const panel = document.getElementById(targetId);
        if (!panel) {
            return;
        }

        const isHidden = panel.hasAttribute("hidden");
        if (isHidden) {
            panel.removeAttribute("hidden");
            button.setAttribute("aria-expanded", "true");
            button.textContent = "Hide English translation";
            button.setAttribute("aria-label", "Hide English translation");
        } else {
            panel.setAttribute("hidden", "");
            button.setAttribute("aria-expanded", "false");
            button.textContent = "Reveal English translation";
            button.setAttribute("aria-label", "Reveal English translation");
        }
    });
});
