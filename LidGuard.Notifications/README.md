# LidGuard.Notifications

## Overview

`LidGuard.Notifications` is a small ASP.NET Core Razor Pages server that receives LidGuard pre-suspend webhooks and forwards them to subscribed browsers through Web Push. It stores browser subscriptions, webhook events, and delivery results in SQLite.

The server is intentionally not a pure WebAssembly app. LidGuard needs an HTTP endpoint for incoming webhooks, and Web Push requires a private VAPID key that must stay on the server.

## HTTPS And Localhost Requirements

Browser Web Push requires a secure context. Use HTTPS in production. During development, browsers also allow service workers and push APIs on `localhost`.

Do not expose this server publicly without HTTPS, a strong `AccessToken`, and a separate strong `WebhookSecret`.

## VAPID Key Generation

Generate VAPID keys with `ClosureOSS.WebPush`:

```csharp
using WebPush;

var vapidDetails = VapidHelper.GenerateVapidKeys();
Console.WriteLine($"Public:  {vapidDetails.PublicKey}");
Console.WriteLine($"Private: {vapidDetails.PrivateKey}");
```

Put the generated public key in `VapidPublicKey` and the generated private key in `VapidPrivateKey`.

Never put the private key in client JavaScript, a service worker, a browser-visible file, or Git-tracked configuration. For development, use `dotnet user-secrets`. For production, use environment variables or the hosting provider's secret store.

## User-Secrets Configuration

From the repository root:

```powershell
dotnet user-secrets set "LidGuardNotifications:AccessToken" "<personal-access-token>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:WebhookSecret" "<webhook-secret>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidPublicKey" "<vapid-public-key>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidPrivateKey" "<vapid-private-key>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidSubject" "mailto:you@example.com" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:PublicBaseUrl" "https://localhost:5001" --project LidGuard.Notifications
```

`AccessToken` and `WebhookSecret` must be different values.

`DatabasePath` is optional. When omitted, the server uses `%LOCALAPPDATA%\LidGuard\Notifications\notifications.sqlite` on Windows.

## Environment Variable Configuration

Use double underscores for nested configuration:

```powershell
$env:LidGuardNotifications__AccessToken = "<personal-access-token>"
$env:LidGuardNotifications__WebhookSecret = "<webhook-secret>"
$env:LidGuardNotifications__VapidPublicKey = "<vapid-public-key>"
$env:LidGuardNotifications__VapidPrivateKey = "<vapid-private-key>"
$env:LidGuardNotifications__VapidSubject = "mailto:you@example.com"
$env:LidGuardNotifications__PublicBaseUrl = "https://notify.example.com"
$env:LidGuardNotifications__DatabasePath = "C:\Data\LidGuard\notifications.sqlite"
```

## Local Run

```powershell
dotnet run --project LidGuard.Notifications
```

Open the displayed localhost URL, sign in with `AccessToken`, then subscribe the browser from the dashboard.

## Browser Subscribe And Unsubscribe

1. Open the dashboard over HTTPS or `localhost`.
2. Sign in with `AccessToken`.
3. Select `Subscribe browser`.
4. Allow browser notifications.
5. Use `Unsubscribe` to remove the current browser subscription.

Subscriptions are upserted by endpoint. A new browser subscription reactivates the stored row and updates the endpoint keys.

## LidGuard Webhook URL

Configure LidGuard with the server URL and webhook secret:

```powershell
lidguard settings --pre-suspend-webhook-url https://host/api/webhooks/lidguard/{webhookSecret}
```

Replace `{webhookSecret}` with the configured `WebhookSecret`. The webhook secret is for LidGuard only; it must not be the same value as the browser login `AccessToken`.

## Test Webhook Call

Use `curl` or PowerShell after at least one browser is subscribed:

```powershell
curl.exe -X POST "https://host/api/webhooks/lidguard/{webhookSecret}" -H "Content-Type: application/json" -d "{\"reason\":\"SoftLocked\",\"softLockedSessionCount\":2}"
```

The webhook endpoint records the event and immediately returns `202 Accepted`. The background service then sends notifications and records delivery results.

## Operations Checklist

- Serve the app over HTTPS.
- Store `AccessToken`, `WebhookSecret`, and `VapidPrivateKey` outside Git.
- Keep `AccessToken` and `WebhookSecret` different.
- Configure `VapidSubject` as a contactable `mailto:` or HTTPS URL.
- Back up the SQLite database if notification history matters.
- Restrict inbound access to the dashboard when possible.
- Monitor the `WebhookEvents` and `NotificationDeliveries` tables or the `/events` page for delivery failures.

## Troubleshooting

- If the browser cannot subscribe, confirm the page is served over HTTPS or `localhost`.
- If the browser never prompts for notification permission, check that notifications are allowed for the site in browser settings.
- If `Subscribe browser` fails with a VAPID error, regenerate keys and make sure the public key in server configuration matches the private key.
- If LidGuard webhooks return `404`, confirm the URL contains the exact `WebhookSecret`.
- If events appear but notifications do not, open `/events` and check for permanent or transient delivery failures.
- Permanent Web Push failures with HTTP `404` or `410` deactivate the subscription. Subscribe the browser again.
