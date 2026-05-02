# LidGuard.Notifications

🌐 [English](README.md)

## 개요

`LidGuard.Notifications`는 LidGuard pre-suspend webhook을 받아 구독된 브라우저로 Web Push 알림을 전달하는 작은 ASP.NET Core Razor Pages 서버입니다. 브라우저 구독, webhook event, delivery 결과는 SQLite에 저장합니다.

이 서버는 의도적으로 순수 WebAssembly 앱이 아닙니다. LidGuard는 들어오는 webhook을 받을 HTTP endpoint가 필요하고, Web Push에는 서버에만 보관해야 하는 private VAPID key가 필요합니다.

## HTTPS와 localhost 요구 사항

브라우저 Web Push는 secure context가 필요합니다. 운영 환경에서는 HTTPS를 사용하세요. 개발 중에는 브라우저가 `localhost`에서도 service worker와 push API를 허용합니다.

HTTPS, 강력한 `AccessToken`, 별도의 강력한 `WebhookSecret` 없이 이 서버를 공개하지 마세요.

## VAPID key 생성

임시 .NET project를 만들지 말고 CLI에서 VAPID key를 생성합니다. `web-push` npm package는 cross-platform generator를 제공하며 이 저장소에 dependency를 추가하지 않습니다.

먼저 OS에 맞게 `npx`를 준비합니다:

- Windows: 공식 installer로 Node.js LTS를 설치하거나 `winget install OpenJS.NodeJS.LTS`를 실행합니다.
- macOS: 공식 installer로 Node.js LTS를 설치하거나 `brew install node`를 실행합니다.
- Linux: 배포판 package로 Node.js와 npm을 설치합니다. Debian/Ubuntu에서는 `sudo apt install nodejs npm`을 사용할 수 있습니다.

그다음 모든 OS에서 같은 명령을 실행합니다:

```bash
npx --yes web-push generate-vapid-keys
```

생성된 public key는 `VapidPublicKey`에, private key는 `VapidPrivateKey`에 넣습니다.

Private key는 절대 client JavaScript, service worker, 브라우저에서 볼 수 있는 파일, Git-tracked configuration에 넣지 마세요. 개발 환경에서는 `dotnet user-secrets`를 사용하고, 운영 환경에서는 환경 변수나 hosting provider의 secret store를 사용하세요.

## User-Secrets 설정

저장소 루트에서 실행합니다:

```powershell
dotnet user-secrets set "LidGuardNotifications:AccessToken" "<personal-access-token>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:WebhookSecret" "<webhook-secret>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidPublicKey" "<vapid-public-key>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidPrivateKey" "<vapid-private-key>" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:VapidSubject" "mailto:you@example.com" --project LidGuard.Notifications
dotnet user-secrets set "LidGuardNotifications:PublicBaseUrl" "https://localhost:5001" --project LidGuard.Notifications
```

`AccessToken`과 `WebhookSecret`은 서로 다른 값이어야 합니다.

`DatabasePath`는 선택 사항입니다. 생략하면 서버는 Windows에서 `%LOCALAPPDATA%\LidGuard\Notifications\notifications.sqlite`를 사용합니다.

## 환경 변수 설정

중첩 설정에는 double underscore를 사용합니다:

```powershell
$env:LidGuardNotifications__AccessToken = "<personal-access-token>"
$env:LidGuardNotifications__WebhookSecret = "<webhook-secret>"
$env:LidGuardNotifications__VapidPublicKey = "<vapid-public-key>"
$env:LidGuardNotifications__VapidPrivateKey = "<vapid-private-key>"
$env:LidGuardNotifications__VapidSubject = "mailto:you@example.com"
$env:LidGuardNotifications__PublicBaseUrl = "https://notify.example.com"
$env:LidGuardNotifications__DatabasePath = "C:\Data\LidGuard\notifications.sqlite"
```

## 로컬 실행

```powershell
dotnet run --project LidGuard.Notifications
```

표시된 localhost URL을 열고 `AccessToken`으로 로그인한 뒤 dashboard에서 브라우저를 구독합니다.

## 브라우저 구독과 구독 해제

1. HTTPS 또는 `localhost`로 dashboard를 엽니다.
2. `AccessToken`으로 로그인합니다.
3. `Subscribe browser`를 선택합니다.
4. 브라우저 알림을 허용합니다.
5. 현재 브라우저 구독을 제거하려면 `Unsubscribe`를 사용합니다.

Subscription은 endpoint 기준으로 upsert됩니다. 새 브라우저 구독은 저장된 row를 다시 활성화하고 endpoint key를 갱신합니다.

## LidGuard webhook URL

서버 URL과 webhook secret으로 LidGuard를 설정합니다:

```powershell
lidguard settings --pre-suspend-webhook-url https://host/api/webhooks/lidguard/{webhookSecret}
```

`{webhookSecret}`을 설정된 `WebhookSecret`으로 바꿉니다. Webhook secret은 LidGuard 전용입니다. 브라우저 로그인용 `AccessToken`과 같은 값이면 안 됩니다.

## 테스트용 webhook 호출

브라우저를 하나 이상 구독한 뒤 `curl` 또는 PowerShell을 사용합니다:

```powershell
curl.exe -X POST "https://host/api/webhooks/lidguard/{webhookSecret}" -H "Content-Type: application/json" -d "{\"reason\":\"SoftLocked\",\"softLockedSessionCount\":2}"
```

Webhook endpoint는 event를 기록하고 즉시 `202 Accepted`를 반환합니다. 이후 background service가 알림을 보내고 delivery 결과를 기록합니다.

## 운영 체크리스트

- 앱을 HTTPS로 제공합니다.
- `AccessToken`, `WebhookSecret`, `VapidPrivateKey`는 Git 밖에 저장합니다.
- `AccessToken`과 `WebhookSecret`은 서로 다르게 유지합니다.
- `VapidSubject`는 연락 가능한 `mailto:` 또는 HTTPS URL로 설정합니다.
- 알림 이력이 중요하다면 SQLite database를 백업합니다.
- 가능하면 dashboard로 들어오는 접근을 제한합니다.
- `/events` 페이지 또는 `WebhookEvents`, `NotificationDeliveries` 테이블로 delivery 실패를 모니터링합니다.

## 문제 해결

- 브라우저 구독이 되지 않으면 페이지가 HTTPS 또는 `localhost`로 제공되는지 확인합니다.
- 브라우저가 알림 권한을 묻지 않으면 browser settings에서 해당 site의 알림이 허용되어 있는지 확인합니다.
- `Subscribe browser`가 VAPID 오류로 실패하면 key를 다시 생성하고 server configuration의 public key가 private key와 맞는지 확인합니다.
- LidGuard webhook이 `404`를 반환하면 URL에 정확한 `WebhookSecret`이 들어 있는지 확인합니다.
- Event는 보이지만 알림이 오지 않으면 `/events`를 열어 permanent 또는 transient delivery failure를 확인합니다.
- HTTP `404` 또는 `410`을 동반한 permanent Web Push 실패는 subscription을 비활성화합니다. 브라우저를 다시 구독하세요.
