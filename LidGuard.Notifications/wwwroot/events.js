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

    function getFollowUpStatusLabel(status) {
        if (stringIsNullOrWhiteSpace(status)) return "";

        const key = `stopFollowUpStatus${status.charAt(0).toUpperCase()}${status.slice(1)}`;
        return page.dataset[key] || status;
    }

    function setFormSubmitting(form, submitting, statusSelector) {
        form.dataset.submitting = submitting ? "true" : "false";
        form.setAttribute("aria-busy", submitting ? "true" : "false");
        const submittingText = form.dataset.submittingText || getText("eventsLoading", "Loading...");
        const status = statusSelector ? form.querySelector(statusSelector) : null;
        if (status && submitting) {
            status.textContent = submittingText;
            status.hidden = false;
        }

        for (const input of form.querySelectorAll("input, button")) input.disabled = submitting;
    }

    function markReplyFormSubmitting(form) {
        if (form.dataset.submitting === "true") return false;

        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        const submittingText = form.dataset.submittingText || getText("eventsLoading", "Loading...");
        const status = form.querySelector("[data-reply-submit-status]");
        if (status) {
            status.textContent = submittingText;
            status.hidden = false;
        }

        const card = form.closest("[data-event-card]") || form;
        for (const textarea of card.querySelectorAll("textarea")) textarea.readOnly = true;
        for (const button of card.querySelectorAll("button")) {
            button.disabled = true;
            if (button.form === form && button.type === "submit") {
                button.dataset.originalText = button.textContent;
                button.textContent = submittingText;
            }
        }

        return true;
    }

    async function readActionResponse(response) {
        const contentType = response.headers.get("content-type") || "";
        if (!contentType.includes("application/json")) {
            const text = await response.text();
            throw new Error(text || getText("eventsLoadFailed", "Request failed."));
        }

        const actionResponse = await response.json();
        if (!response.ok || !actionResponse.succeeded) {
            const error = new Error(actionResponse.message || getText("eventsLoadFailed", "Request failed."));
            error.actionResponse = actionResponse;
            throw error;
        }
        return actionResponse;
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-Requested-With": "XMLHttpRequest"
            },
            body: JSON.stringify(body)
        });
        return await readActionResponse(response);
    }

    function setTimeValue(timeElement, value) {
        if (!timeElement || stringIsNullOrWhiteSpace(value)) return;

        timeElement.dateTime = value;
        timeElement.textContent = value;
    }

    function updateFollowUpCard(card, actionResponse) {
        if (!card || !actionResponse) return;

        const statusLabel = getFollowUpStatusLabel(actionResponse.status);
        const statusText = card.querySelector("[data-stop-follow-up-status-text]");
        if (statusText && !stringIsNullOrWhiteSpace(statusLabel)) statusText.textContent = statusLabel;

        const statusBadge = card.querySelector("[data-stop-follow-up-status-badge]");
        if (statusBadge && !stringIsNullOrWhiteSpace(statusLabel)) statusBadge.textContent = statusLabel;

        setTimeValue(card.querySelector("[data-stop-follow-up-deadline]"), actionResponse.deadlineAtUtc);
        setTimeValue(card.querySelector("[data-stop-follow-up-maximum-deadline]"), actionResponse.maximumDeadlineAtUtc);

        const providerHookTimeoutRemaining = card.querySelector("[data-provider-hook-timeout-remaining]");
        if (providerHookTimeoutRemaining && !stringIsNullOrWhiteSpace(actionResponse.providerHookTimeoutRemainingText)) providerHookTimeoutRemaining.textContent = actionResponse.providerHookTimeoutRemainingText;

        if (actionResponse.status && actionResponse.status !== "Pending") {
            const actions = card.querySelector("[data-stop-follow-up-actions]");
            if (actions) actions.hidden = true;
        }

        refreshDeviceTimes(card);
    }

    function setActionStatus(card, message) {
        const status = card?.querySelector("[data-stop-follow-up-action-status]");
        if (!status) return;

        status.textContent = message || "";
        status.hidden = stringIsNullOrWhiteSpace(message);
    }

    async function submitReply(form) {
        if (!markReplyFormSubmitting(form)) return;

        const card = form.closest("[data-event-card]");
        const textarea = form.querySelector("textarea[name='reply']");
        try {
            const actionResponse = await postJson(form.dataset.replyUrl, {
                reply: textarea?.value || "",
                waitForConsumption: true
            });
            updateFollowUpCard(card, actionResponse);
            const status = form.querySelector("[data-reply-submit-status]");
            if (status) {
                status.textContent = actionResponse.message || "";
                status.hidden = stringIsNullOrWhiteSpace(status.textContent);
            }
            setActionStatus(card, actionResponse.message);
        } catch (error) {
            updateFollowUpCard(card, error.actionResponse);
            const status = form.querySelector("[data-reply-submit-status]");
            if (status) {
                status.textContent = error.message || getText("eventsLoadFailed", "Request failed.");
                status.hidden = false;
            }
            form.dataset.submitting = "false";
            form.setAttribute("aria-busy", "false");
            for (const textareaElement of (card || form).querySelectorAll("textarea")) textareaElement.readOnly = false;
            for (const button of (card || form).querySelectorAll("button")) {
                button.disabled = false;
                if (button.dataset.originalText) button.textContent = button.dataset.originalText;
            }
        }
    }

    async function submitExtension(form) {
        if (form.dataset.submitting === "true") return;

        const card = form.closest("[data-event-card]");
        const extendMinutesInput = form.querySelector("input[name='extendMinutes']");
        setFormSubmitting(form, true, "[data-extend-submit-status]");
        try {
            const actionResponse = await postJson(form.dataset.extendUrl, {
                extendMinutes: Number.parseInt(extendMinutesInput?.value || "1", 10)
            });
            updateFollowUpCard(card, actionResponse);
            const status = form.querySelector("[data-extend-submit-status]");
            if (status) {
                status.textContent = actionResponse.message || "";
                status.hidden = stringIsNullOrWhiteSpace(status.textContent);
            }
            setActionStatus(card, actionResponse.message);
        } catch (error) {
            updateFollowUpCard(card, error.actionResponse);
            const status = form.querySelector("[data-extend-submit-status]");
            if (status) {
                status.textContent = error.message || getText("eventsLoadFailed", "Request failed.");
                status.hidden = false;
            }
        } finally {
            setFormSubmitting(form, false, null);
        }
    }

    async function submitCancellation(form) {
        if (form.dataset.submitting === "true") return;

        const card = form.closest("[data-event-card]");
        setFormSubmitting(form, true, null);
        try {
            const actionResponse = await postJson(form.dataset.cancelUrl, {});
            updateFollowUpCard(card, actionResponse);
            setActionStatus(card, actionResponse.message);
        } catch (error) {
            updateFollowUpCard(card, error.actionResponse);
            setActionStatus(card, error.message || getText("eventsLoadFailed", "Request failed."));
        } finally {
            setFormSubmitting(form, false, null);
        }
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
    page.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (form.matches("[data-stop-follow-up-reply-form]")) {
            event.preventDefault();
            void submitReply(form);
            return;
        }

        if (form.matches("[data-stop-follow-up-extend-form]")) {
            event.preventDefault();
            void submitExtension(form);
            return;
        }

        if (form.matches("[data-stop-follow-up-cancel-form]")) {
            event.preventDefault();
            void submitCancellation(form);
        }
    });
    page.addEventListener("toggle", event => {
        if (!(event.target instanceof HTMLDetailsElement)) return;
        if (!event.target.matches("[data-event-details]")) return;

        void loadEventDetails(event.target);
    }, true);
})();
