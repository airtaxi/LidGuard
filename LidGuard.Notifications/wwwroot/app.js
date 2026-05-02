const subscribeButton = document.getElementById("subscribeButton");
const unsubscribeButton = document.getElementById("unsubscribeButton");
const subscriptionStatus = document.getElementById("subscriptionStatus");

function setStatus(message) {
    subscriptionStatus.textContent = message;
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
    if (!("serviceWorker" in navigator)) throw new Error("Service workers are not available in this browser.");
    if (!("PushManager" in window)) throw new Error("Web Push is not available in this browser.");

    return await navigator.serviceWorker.register("/service-worker.js");
}

async function getPublicKey() {
    const response = await fetch("/api/push/public-key", { credentials: "same-origin" });
    if (!response.ok) throw new Error("Failed to load the VAPID public key.");

    const publicKeyResponse = await response.json();
    return publicKeyResponse.publicKey;
}

async function subscribeBrowser() {
    subscribeButton.disabled = true;
    try {
        const permission = await Notification.requestPermission();
        if (permission !== "granted") {
            setStatus("Notification permission was not granted.");
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

        setStatus("This browser is subscribed.");
    } catch (error) {
        setStatus(error.message || "Subscription failed.");
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
            setStatus("This browser is not subscribed.");
            return;
        }

        await fetch("/api/push/subscriptions", {
            method: "DELETE",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ endpoint: subscription.endpoint })
        });
        await subscription.unsubscribe();
        setStatus("This browser is unsubscribed.");
    } catch (error) {
        setStatus(error.message || "Unsubscribe failed.");
    } finally {
        unsubscribeButton.disabled = false;
    }
}

subscribeButton?.addEventListener("click", subscribeBrowser);
unsubscribeButton?.addEventListener("click", unsubscribeBrowser);
