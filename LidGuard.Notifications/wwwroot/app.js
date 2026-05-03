const subscribeButton = document.getElementById("subscribeButton");
const unsubscribeButton = document.getElementById("unsubscribeButton");
const activeSubscriptionCount = document.getElementById("activeSubscriptionCount");
const subscriptionStatus = document.getElementById("subscriptionStatus");
const notificationDashboard = document.getElementById("notificationDashboard");
const copyResetHandles = new WeakMap();

function setStatus(message) {
    subscriptionStatus.textContent = message;
}

function getText(name, fallback) {
    return notificationDashboard?.dataset[name] || fallback;
}

async function copyTextToClipboard(text) {
    if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.style.position = "fixed";
    textArea.style.left = "-9999px";
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    try {
        if (!document.execCommand("copy")) throw new Error(getText("copyFailedLabel", "Copy failed"));
    } finally {
        document.body.removeChild(textArea);
    }
}

async function copyCommand(button) {
    const copyLabel = getText("copyLabel", "Copy");
    const copiedLabel = getText("copiedLabel", "Copied");
    const copyFailedLabel = getText("copyFailedLabel", "Copy failed");
    const resetHandle = copyResetHandles.get(button);
    if (resetHandle) window.clearTimeout(resetHandle);

    try {
        await copyTextToClipboard(button.dataset.copyText || "");
        setCopyButtonState(button, "copied", copiedLabel);
    } catch {
        setCopyButtonState(button, "failed", copyFailedLabel);
    }

    copyResetHandles.set(button, window.setTimeout(() => {
        setCopyButtonState(button, "idle", copyLabel);
        copyResetHandles.delete(button);
    }, 1200));
}

function setCopyButtonState(button, state, label) {
    button.dataset.state = state;
    button.setAttribute("aria-label", label);
    button.title = label;
}

async function updateActiveSubscriptionCount(response) {
    const subscriptionChangeResponse = await response.json();
    activeSubscriptionCount.textContent = subscriptionChangeResponse.activeSubscriptionCount.toString();
}

function convertBase64UrlToUint8Array(value) {
    const padding = "=".repeat((4 - value.length % 4) % 4);
    const base64 = (value + padding).replace(/-/g, "+").replace(/_/g, "/");
    const raw = window.atob(base64);
    const output = new Uint8Array(raw.length);

    for (let index = 0; index < raw.length; index++) output[index] = raw.charCodeAt(index);

    return output;
}

async function getRegistration() {
    if (!("serviceWorker" in navigator)) throw new Error(getText("serviceWorkersUnavailable", "Service workers are not available in this browser."));
    if (!("PushManager" in window)) throw new Error(getText("webPushUnavailable", "Web Push is not available in this browser."));

    return await navigator.serviceWorker.register("/service-worker.js");
}

async function getPublicKey() {
    const response = await fetch("/api/push/public-key", { credentials: "same-origin" });
    if (!response.ok) throw new Error(getText("vapidPublicKeyLoadFailed", "Failed to load the VAPID public key."));

    const publicKeyResponse = await response.json();
    return publicKeyResponse.publicKey;
}

async function subscribeBrowser() {
    subscribeButton.disabled = true;
    try {
        const permission = await Notification.requestPermission();
        if (permission !== "granted") {
            setStatus(getText("notificationPermissionNotGranted", "Notification permission was not granted."));
            return;
        }

        const registration = await getRegistration();
        const publicKey = await getPublicKey();
        const subscription = await registration.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: convertBase64UrlToUint8Array(publicKey)
        });
        const subscriptionJson = subscription.toJSON();
        const response = await fetch("/api/push/subscriptions", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                endpoint: subscriptionJson.endpoint,
                keys: subscriptionJson.keys
            })
        });

        if (!response.ok) throw new Error(await response.text());

        await updateActiveSubscriptionCount(response);
        setStatus(getText("browserSubscribed", "This browser is subscribed."));
    } catch (error) {
        setStatus(error.message || getText("subscriptionFailed", "Subscription failed."));
    } finally {
        subscribeButton.disabled = false;
    }
}

async function unsubscribeBrowser() {
    unsubscribeButton.disabled = true;
    try {
        const registration = await getRegistration();
        const subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            setStatus(getText("browserNotSubscribed", "This browser is not subscribed."));
            return;
        }

        const response = await fetch("/api/push/subscriptions", {
            method: "DELETE",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ endpoint: subscription.endpoint })
        });
        if (!response.ok) throw new Error(await response.text());

        await updateActiveSubscriptionCount(response);
        await subscription.unsubscribe();
        setStatus(getText("browserUnsubscribed", "This browser is unsubscribed."));
    } catch (error) {
        setStatus(error.message || getText("unsubscribeFailed", "Unsubscribe failed."));
    } finally {
        unsubscribeButton.disabled = false;
    }
}

subscribeButton?.addEventListener("click", subscribeBrowser);
unsubscribeButton?.addEventListener("click", unsubscribeBrowser);
for (const button of document.querySelectorAll("[data-copy-text]")) {
    setCopyButtonState(button, "idle", getText("copyLabel", "Copy"));
    button.addEventListener("click", () => copyCommand(button));
}
