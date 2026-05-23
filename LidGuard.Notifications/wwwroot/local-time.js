(() => {
    function padTimePart(value) {
        return value.toString().padStart(2, "0");
    }

    function formatDeviceTime(timestamp) {
        return [
            timestamp.getFullYear(),
            padTimePart(timestamp.getMonth() + 1),
            padTimePart(timestamp.getDate())
        ].join("-") + " " + [
            padTimePart(timestamp.getHours()),
            padTimePart(timestamp.getMinutes()),
            padTimePart(timestamp.getSeconds())
        ].join(":");
    }

    function refreshDeviceTimes(root = document) {
        for (const element of root.querySelectorAll("time[data-device-time]")) {
            const timestamp = new Date(element.dateTime);
            if (Number.isNaN(timestamp.getTime())) continue;

            element.textContent = formatDeviceTime(timestamp);
            element.title = timestamp.toLocaleString(undefined, { timeZoneName: "short" });
        }
    }

    window.lidGuardLocalTime = { refresh: refreshDeviceTimes };
    refreshDeviceTimes();
})();
