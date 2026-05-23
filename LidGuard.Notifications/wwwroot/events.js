(() => {
    const page = document.querySelector("[data-events-page]");
    if (!page) return;

    const eventList = page.querySelector("[data-event-list]");
    const loadMoreButton = page.querySelector("[data-load-more-events]");
    const eventsError = page.querySelector("[data-events-error]");
    const recentEventsCount = page.querySelector("[data-recent-events-count]");

    function getText(name, fallback) {
        return page.dataset[name] || fallback;
    }

    function setEventsError(message) {
        if (!eventsError) return;

        eventsError.textContent = message;
        eventsError.hidden = stringIsNullOrWhiteSpace(message);
    }

    function stringIsNullOrWhiteSpace(value) {
        return !value || value.trim().length === 0;
    }

    function updateRecentEventsCount() {
        if (!recentEventsCount || !eventList) return;

        const eventCount = eventList.querySelectorAll("[data-event-card]").length;
        const format = page.dataset.recentEventsFormat || "{0}";
        recentEventsCount.textContent = format.replace("{0}", eventCount.toLocaleString());
    }

    function refreshDeviceTimes(root) {
        window.lidGuardLocalTime?.refresh(root);
    }

    async function loadMoreEvents() {
        if (!loadMoreButton || !eventList) return;

        const beforeWebhookEventIdentifier = loadMoreButton.dataset.beforeWebhookEventIdentifier;
        if (stringIsNullOrWhiteSpace(beforeWebhookEventIdentifier)) {
            loadMoreButton.hidden = true;
            return;
        }

        const loadMoreLabel = getText("eventsLoadMore", "Load more");
        loadMoreButton.disabled = true;
        loadMoreButton.textContent = getText("eventsLoading", "Loading...");
        setEventsError("");

        try {
            const url = new URL(page.dataset.moreUrl || window.location.href, window.location.href);
            url.searchParams.set("beforeWebhookEventIdentifier", beforeWebhookEventIdentifier);
            const response = await fetch(url, {
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw new Error(getText("eventsLoadFailed", "Failed to load more events."));

            const template = document.createElement("template");
            template.innerHTML = (await response.text()).trim();
            const state = template.content.querySelector("[data-event-page-state]");
            if (!state) throw new Error(getText("eventsLoadFailed", "Failed to load more events."));

            state.remove();

            for (const card of template.content.querySelectorAll("[data-event-card]")) eventList.appendChild(card);

            const hasMore = state?.dataset.hasMore === "true";
            const nextBeforeWebhookEventIdentifier = state?.dataset.nextBeforeWebhookEventIdentifier || "";
            loadMoreButton.hidden = !hasMore || stringIsNullOrWhiteSpace(nextBeforeWebhookEventIdentifier);
            loadMoreButton.dataset.beforeWebhookEventIdentifier = nextBeforeWebhookEventIdentifier;
            refreshDeviceTimes(eventList);
            updateRecentEventsCount();
        } catch (error) {
            setEventsError(error.message || getText("eventsLoadFailed", "Failed to load more events."));
        } finally {
            loadMoreButton.disabled = false;
            loadMoreButton.textContent = loadMoreLabel;
        }
    }

    async function loadEventDetails(details) {
        if (!details.open || details.dataset.loaded === "true" || details.dataset.loading === "true") return;

        const content = details.querySelector("[data-event-details-content]");
        if (!content) return;

        const webhookEventIdentifier = details.dataset.webhookEventIdentifier;
        if (stringIsNullOrWhiteSpace(webhookEventIdentifier)) return;

        details.dataset.loading = "true";
        content.textContent = getText("eventsLoading", "Loading...");
        content.classList.add("muted");

        try {
            const url = new URL(page.dataset.detailsUrl || window.location.href, window.location.href);
            url.searchParams.set("webhookEventIdentifier", webhookEventIdentifier);
            const response = await fetch(url, {
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw new Error(getText("eventDetailsLoadFailed", "Failed to load event details."));

            content.innerHTML = await response.text();
            content.classList.remove("muted");
            details.dataset.loaded = "true";
            refreshDeviceTimes(content);
        } catch (error) {
            content.textContent = error.message || getText("eventDetailsLoadFailed", "Failed to load event details.");
            content.classList.add("muted");
        } finally {
            delete details.dataset.loading;
        }
    }

    loadMoreButton?.addEventListener("click", loadMoreEvents);
    page.addEventListener("toggle", event => {
        if (!(event.target instanceof HTMLDetailsElement)) return;
        if (!event.target.matches("[data-event-details]")) return;

        void loadEventDetails(event.target);
    }, true);
})();
