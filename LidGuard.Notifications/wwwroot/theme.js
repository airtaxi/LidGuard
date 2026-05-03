(() => {
    const storageKey = "lidguard-notifications-theme";
    const root = document.documentElement;
    const colorSchemeQuery = window.matchMedia?.("(prefers-color-scheme: dark)");

    function normalizeTheme(value) {
        if (value === "light" || value === "dark") return value;

        return "system";
    }

    function getStoredTheme() {
        try {
            return normalizeTheme(window.localStorage.getItem(storageKey));
        } catch {
            return "system";
        }
    }

    function getEffectiveTheme(theme) {
        if (theme === "light" || theme === "dark") return theme;

        return colorSchemeQuery?.matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        if (theme === "system") {
            root.removeAttribute("data-theme");
            return;
        }

        root.dataset.theme = theme;
    }

    function saveTheme(theme) {
        try {
            window.localStorage.setItem(storageKey, theme);
        } catch {
            return;
        }
    }

    function updateButton(button, theme) {
        const effectiveTheme = getEffectiveTheme(theme);
        const nextTheme = effectiveTheme === "dark" ? "light" : "dark";
        const darkModeLabel = button.dataset.darkModeLabel || "Dark mode";
        const lightModeLabel = button.dataset.lightModeLabel || "Light mode";
        const switchToFormat = button.dataset.switchToFormat || "Switch to {0}";
        const label = nextTheme === "dark" ? darkModeLabel : lightModeLabel;
        const accessibleModeLabel = label.toLocaleLowerCase(document.documentElement.lang || undefined);

        const accessibleLabel = switchToFormat.replace("{0}", accessibleModeLabel);
        button.dataset.themeState = effectiveTheme;
        button.dataset.nextTheme = nextTheme;
        button.setAttribute("aria-label", accessibleLabel);
        button.title = accessibleLabel;
    }

    let selectedTheme = getStoredTheme();
    applyTheme(selectedTheme);

    document.addEventListener("DOMContentLoaded", () => {
        const button = document.getElementById("themeToggle");
        if (!button) return;

        updateButton(button, selectedTheme);
        button.addEventListener("click", () => {
            selectedTheme = button.dataset.nextTheme;
            saveTheme(selectedTheme);
            applyTheme(selectedTheme);
            updateButton(button, selectedTheme);
        });

        const handleColorSchemeChange = () => {
            if (selectedTheme !== "system") return;

            updateButton(button, selectedTheme);
        };

        if (colorSchemeQuery?.addEventListener) colorSchemeQuery.addEventListener("change", handleColorSchemeChange);
        else if (colorSchemeQuery?.addListener) colorSchemeQuery.addListener(handleColorSchemeChange);
    });
})();
