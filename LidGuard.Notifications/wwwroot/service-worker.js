self.addEventListener("push", event => {
    const isKorean = self.navigator?.language?.toLowerCase().startsWith("ko") === true;
    const fallbackMessage = {
        title: isKorean ? "LidGuard 알림" : "LidGuard notification",
        body: isKorean ? "LidGuard가 절전 이벤트를 받았습니다." : "LidGuard received a suspend event.",
        url: "/events",
        tag: "lidguard-suspend"
    };
    const message = event.data ? event.data.json() : fallbackMessage;

    event.waitUntil(self.registration.showNotification(message.title || fallbackMessage.title, {
        body: message.body || fallbackMessage.body,
        tag: message.tag || fallbackMessage.tag,
        data: { url: message.url || fallbackMessage.url }
    }));
});

self.addEventListener("notificationclick", event => {
    event.notification.close();
    const url = event.notification.data?.url || "/events";

    event.waitUntil(clients.matchAll({ type: "window", includeUncontrolled: true }).then(clientList => {
        for (const client of clientList) {
            if (client.url.endsWith(url) && "focus" in client) return client.focus();
        }

        return clients.openWindow(url);
    }));
});
