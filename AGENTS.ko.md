# LidGuard 작업 기준

이 문서는 사용자가 읽기 위한 `AGENTS.md`의 한국어판이다.

`AGENTS.md`가 LidGuard의 제품 방향, 기술 설계, 현재 구현 상태, 다음 작업의 기준 문서다. `AGENTS.md`를 의미 있게 수정할 때는 이 파일도 같은 턴에서 함께 갱신해야 한다.

## 필수 규칙

- 사용자가 명시적으로 요청하지 않으면 `git commit` 또는 `git push`를 절대 실행하지 않는다.
- 커밋은 영어로 작성한다.
- 이 저장소는 NativeAOT 및 trimming에 민감하다. IL2026 / IL3050 경고를 유발하는 API는 피하고, reflection 기반이나 동적 JSON helper보다 AOT-safe 오버로드와 source-generated `System.Text.Json` serializer를 우선 사용한다.
- Windows native interop는 CsWin32 생성 API로 중앙화한다. CsWin32 또는 사용 가능한 metadata로 API를 표현할 수 없고 그 예외를 이 파일에 문서화한 경우가 아니면, 프로젝트 코드에 직접 `[DllImport]` / `[LibraryImport]`, `NativeLibrary` / `GetProcAddress`, 수동 COM vtable 호출을 추가하지 않는다.
- `Microsoft.Windows.WDK.Win32Metadata`는 `NtQueryInformationProcess` 같은 WDK 기반 API를 CsWin32가 생성하게 하기 위해서만 의도적으로 참조한다. 이 참조는 `PrivateAssets="all"`로 유지하고, 수동 native 선언을 추가해도 된다는 근거로 사용하지 않는다.
- session, runtime log, hook log, suspend history, backup state, notification data, timestamped backup file name에 저장되는 timestamp는 `DateTimeOffset.UtcNow` 같은 UTC source로 기록해야 한다. 사용자가 보는 CLI와 web 출력은 저장된 timestamp를 표시 직전에 현재 시스템 로컬 시간으로 변환해야 한다.
- 사용자가 명시적으로 요청하지 않으면 빌드를 실행하지 않는다. 단, 변경 규모가 매우 큰 경우는 예외다.
- 작업 중 애매한 점이 있으면 즉시 사용자에게 물어보고, 가능하면 선택지를 제공한다.

## 문서 정책

- `AGENTS.md`가 LidGuard의 단일 기준 문서다.
- 모든 `*.ko.md` 파일은 사용자용 한국어 미러 또는 번역본일 뿐이다. 원본 문서와 내용이 중복되어 컨텍스트를 낭비하므로 일반 작업 문맥 수집 때는 읽지 말고, 해당 원본 문서를 의미 있게 수정하거나 사용자가 명시적으로 요청할 때만 읽고 갱신한다.
- `Plan.md`는 중복 계획 문서를 없애기 위해 삭제했다.
- 핵심 동작이나 설계를 바꿀 때는 다른 계획 문서를 다시 만들지 말고 `AGENTS.md`와 `AGENTS.ko.md`를 함께 갱신한다.
- 앞으로 저장소 전체 README에서 Provider MCP나 모델 주도 MCP 세션 흐름을 설명할 때는, 올바른 동작이 모델이 적절한 시점에 LidGuard MCP 도구를 호출하는지에 전적으로 달려 있으므로 동작을 보장하지 않는다고 반드시 명시해야 한다.
- 앞으로 저장소 전체 README에서 Codex hook/session lifecycle 동작을 설명할 때는, Codex App이 같은 working directory에 `process=none` 세션을 남길 수 있으므로 LidGuard가 Codex에 대해 working-directory watchdog fallback을 아무 때나 쓰지 않고, 해석된 프로세스 자신이나 직계 부모가 `cmd.exe`, `pwsh.exe`, `powershell.exe` 인 shell-hosted CLI 세션에서만 예외적으로 사용하며, 그 cleanup 경로가 `process=none` Codex 세션은 절대 삭제하지 않는다고 반드시 명시해야 한다.

## 제품 목표

LidGuard는 Codex, Claude Code, GitHub Copilot CLI처럼 오래 실행되는 로컬 AI 코딩 에이전트를 위한 Windows 우선 유틸리티다.

목표는 추적 중인 에이전트 세션 중 하나라도 아직 보호가 필요할 때 Windows가 잠들지 않도록 유지하고, 세션이 끝나거나 suspend 가능 상태가 되면 사용자의 원래 전원 정책을 복원하는 것이다.

- 에이전트 세션은 provider hook을 통해 시작된다.
- LidGuard는 활성 세션을 감지하고 추적한다.
- Claude Code와 GitHub Copilot CLI 세션은 provider notification이 사용자의 응답 대기 상태를 가리킬 때 runtime 주도의 soft-lock 상태에 들어갈 수 있다.
- soft-lock이 아닌 활성 세션이 하나 이상 있으면 `PowerRequestSystemRequired`와 `PowerRequestAwayModeRequired`로 idle sleep을 막는다.
- 남아 있는 모든 활성 세션이 soft-lock 상태가 되면 LidGuard는 임시 keep-awake 보호를 해제하고, 임시 lid 정책 변경을 복원한 뒤, 덮개가 닫혀 있고 데스크톱에 suspend를 막는 보이는 활성 모니터가 하나도 남아 있지 않을 때만 설정된 suspend 흐름을 시작해야 한다.
- 세션이 설정된 session timeout 동안 활동이 없으면 LidGuard는 soft-lock 상태로 전환하고 일반 soft-lock 동작과 동일한 keep-awake 해제 경로를 적용해야 한다.
- 선택 설정으로 활성 전원 계획의 덮개 닫힘 동작을 임시로 `Do Nothing`으로 변경한다.
- 세션이 멈추면 모든 임시 전원 설정을 사용자의 원래 값으로 복원한다.
- 마지막 활성 세션이 끝난 뒤 노트북 덮개가 닫혀 있고 데스크톱에 suspend를 막는 보이는 활성 모니터가 하나도 남아 있지 않으면 LidGuard는 항상 suspend를 요청해야 한다.
- 활성 세션 수가 `0`이 되면 server runtime은 진행 중인 suspend 또는 cleanup 작업이 끝난 뒤 설정된 server runtime cleanup 지연 시간이 지난 후 종료해야 한다. 기본 지연 시간은 10분이며, `off`는 진행 중인 작업이 끝나는 즉시 종료를 의미한다.
- 활성 세션이 남아 있어도 전부 soft-lock 상태라면, LidGuard는 stop hook을 기다리지 않고 같은 suspend 경로를 따라야 한다.
- suspend 모드는 계속 설정 가능하며 기본값은 Sleep, 선택지는 Hibernate다.
- 마지막 세션 종료 후 suspend까지의 대기 시간도 설정 가능하며 기본값은 10초, `0`이면 즉시 suspend다.
- 마지막 세션 종료 후 재생할 소리도 선택 설정이며 기본값은 off이고, 지원되는 SystemSounds 이름 또는 재생 가능한 `.wav` 경로를 사용할 수 있다.
- post-stop suspend sound 볼륨 override도 선택 설정이며 기본값은 off이고, 허용되는 master volume 범위는 1%에서 100%다.
- 비활동 세션 timeout도 설정 가능하며 기본값은 12분, `off`로 비활성화할 수 있고, 활성화된 값은 최소 1분이어야 한다.
- 선택 사항인 post-session-end webhook URL은 기본 off다. Provider가 정상 session end를 보고했고 그 stop이 pre-suspend 흐름을 예약하지 않는 경우, LidGuard는 cleanup을 막지 않고 `PostSessionEnd` payload를 POST해야 한다. Abort, interrupt, 수동 stop/remove, watchdog, orphan cleanup 경로는 이 webhook을 보내면 안 된다.
- keep-awake 보호가 적용 중이고 노트북 덮개가 닫혀 있으며 데스크톱에 suspend를 막는 보이는 활성 모니터가 하나도 없을 때는, 선택적 Emergency Hibernation thermal monitor가 10초마다 시스템 온도를 확인하고 설정된 임계값에 도달하면 즉시 hibernate를 요청해야 한다.

핵심 설계 원칙은 일반 idle sleep과 lid-close sleep을 분리해서 처리하는 것이다. 일반 idle sleep은 power request로 막고, lid-close sleep은 일반 절전 방지 API로 안정적으로 막을 수 없으므로 `LIDACTION` 정책을 백업, 변경, 복원한다.

## 저장소 구조

- `LidGuard`
  - `net10.0` 대상 콘솔 앱이다.
  - hook-facing CLI, in-process headless runtime, stdio MCP 서버 호스팅을 함께 포함한다.
  - 공통, 플랫폼 중립 모델과 정책은 `Sessions`, `Settings`, `Power`, `Services`, `Results`, `Processes` 같은 feature folder에 둔다.
  - 공용 provider/hook 유틸리티는 `Hooks` 아래 일반 `*.cs` 파일에 둔다.
  - Windows 전용 runtime/process/power 구현은 `*.windows.cs`에 둔다.
  - Linux/macOS placeholder 파일은 현재 cross-platform build가 컴파일되도록 필요한 최소 public surface에만 둔다.
  - 현재 csproj에서는 nullable을 의도적으로 켜지 않는다.
  - `ImplicitUsings`가 켜져 있다.
  - NativeAOT/trimming 호환 플래그가 켜져 있다.
  - AOT 호환성을 위해 CsWin32를 `CsWin32RunAsBuildTask=true`, `DisableRuntimeMarshalling=true`로 사용한다.
  - root namespace는 `LidGuard`, assembly/apphost 이름은 `lidguard`다.
  - NuGet package ID `lidguard`, tool command `lidguard`인 .NET tool 배포를 준비한다.
  - 지원 패키지 RID는 `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`다.
  - Windows 동작은 구현되어 있고, macOS/Linux는 현재 지원 예정 메시지를 출력한 뒤 exit code `0`을 반환한다.
  - named pipe로 runtime에 `start`, `stop`, `remove-session`, `status`, `settings`, `cleanup-orphans` 요청을 보낸다.
  - `mcp-server` 서브커맨드로 stdio MCP 서버를 호스팅한다.
  - 기본 설정 JSON은 `%LOCALAPPDATA%\LidGuard\settings.json`에 저장한다.
- `LidGuard.Notifications`
  - `net10.0` 대상 ASP.NET Core Razor Pages 앱이다.
  - LidGuard pre-suspend 및 post-session-end webhook을 받아 구독된 브라우저로 Web Push 알림을 보낸다.
  - subscription, webhook event, delivery attempt를 SQLite에 저장한다.
  - 서버 측 VAPID 설정을 사용하며, VAPID private key와 access token은 절대 커밋하지 않는다.
- `LidGuard.slnx`
  - `LidGuard`와 `LidGuard.Notifications`를 포함하는 루트 solution 파일이다.

## 기술 설계

### Windows 전원 제어

- 일반 idle sleep 방지는 `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest`를 사용한다.
- `PowerRequestSystemRequired`로 idle system sleep을 막는다.
- 지원되는 환경에서는 `PowerRequestAwayModeRequired`로 away mode 동작을 요청한다.
- `PowerRequestDisplayRequired`는 화면 꺼짐까지 막아야 할 때만 선택적으로 사용한다.
- 보호가 끝나면 power request를 반드시 clear하고 handle을 닫는다.
- sleep idle timeout은 변경하지 않는다. runtime crash 시 사용자 전원 정책을 위험하게 남길 수 있어 폐기한 방식이다.

### 덮개 닫힘 정책

- 덮개 닫힘 설정은 Windows 전원 설정 `LIDACTION`이다.
- Subgroup GUID는 `4f971e89-eebd-4455-a8de-9e59040e7347`이다.
- Setting GUID는 `5ca83367-6e45-459f-a27b-476b1d01c936`이다.
- 값은 `0 = Do Nothing`, `1 = Sleep`, `2 = Hibernate`, `3 = Shut Down`이다.
- 변경 전 활성 전원 스킴에서 AC/DC 값을 함께 읽어 백업한다.
- 활성 세션 동안 설정이 켜져 있으면 AC/DC 값을 모두 `0 = Do Nothing`으로 쓴다.
- 마지막 활성 세션이 멈추면 백업한 AC/DC 값을 복원한다.
- v1은 백업 시점에 활성화되어 있던 스킴을 복원 대상으로 삼는다. 세션 중 활성 스킴이 바뀌는 경우의 정책은 후속 작업으로 남겨둔다.

### 덮개 상태와 절전 진입

- 덮개 열림/닫힘 알림은 `GUID_LIDSWITCH_STATE_CHANGE`를 사용한다.
- broadcast 값은 `0x0 = lid closed`, `0x1 = lid opened`이다.
- `LidSwitchNotificationRegistration`이 값을 `LidSwitchState`로 변환한다.
- closed-lid 정책 판단은 `GetSystemMetrics(SM_CMONITORS)`에서 시작하되 Windows WMI가 inactive로 보고한 monitor connection을 제외한다. 최종 suspend 가능성 확인에서는 `LidSwitchState`가 `Closed`일 때 내부 노트북 패널 connection도 제외하며, 이렇게 계산한 visible display monitor count가 `0`일 때만 lid-close 정책 기준의 suspend 가능 상태로 본다.
- 즉시 sleep/hibernate는 `SeShutdownPrivilege`를 켠 뒤 `SetSuspendState`를 호출한다.
- Modern Standby 시스템에서는 `SetSuspendState(false, ...)`가 `ERROR_NOT_SUPPORTED`로 실패할 수 있다. 향후에는 display-off 방식의 fallback을 고려할 수 있다.
- 마지막 활성 세션이 끝났을 때는 덮개가 닫혀 있고 suspend 가능성 기준의 visible display monitor count가 `0`일 때만, 설정된 지연 시간 후에 설정된 suspend 모드로 suspend를 요청해야 한다. 지연 시간이 `0`이면 즉시 suspend다.
- post-stop suspend sound가 설정되어 있으면, LidGuard는 먼저 지연 시간을 기다리고, 지정된 소리를 끝까지 재생한 뒤, 다시 덮개/세션 상태를 확인하고 suspend를 요청해야 한다.
- post-stop suspend sound 볼륨 override가 설정되어 있으면, LidGuard는 재생 직전에 기본 출력 장치의 master volume과 mute 상태를 캡처하고, 필요한 경우 일시적으로 unmute한 뒤, 설정된 master volume percent로 소리를 재생하고, sound playback cleanup 경로에서 이전 volume과 mute 상태를 복원해야 한다.
- pre-suspend webhook URL이 설정되어 있으면, LidGuard는 suspend를 요청하기 전에 5초 timeout으로 JSON을 POST해야 한다. body에는 `eventType = PreSuspend`와 `reason`이 포함되어야 하고, soft-lock 때문에 suspend되는 경우에는 soft-locked session 수까지 포함해야 한다. Notification receiver는 `eventType`이 빠진 webhook payload를 거부해야 한다.
- post-session-end webhook URL이 설정되어 있으면 LidGuard는 provider가 보고한 정상 session end 이후, 해당 stop이 suspend를 예약하지 않을 때만 5초 timeout으로 JSON을 POST해야 한다. Body에는 `eventType = PostSessionEnd`, `reason = SessionEnded`, provider/session identity, UTC start/activity/end timestamp, end reason metadata, active session count, working directory, 가능한 경우 transcript path가 포함되어야 한다.

### Emergency Hibernation Thermal Monitor

- Emergency Hibernation은 `SystemThermalInformation.GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode)`를 사용해 선택된 기준의 시스템 thermal zone 섭씨 온도를 읽는다.
- Emergency Hibernation 온도 기준은 Low, Average, High로 설정 가능하며 기본값은 Average다.
- thermal monitor는 공유 keep-awake 보호가 적용 중이고, 덮개가 닫혀 있으며, suspend 가능성 기준의 visible display monitor count가 `0`일 때만 동작한다.
- thermal poll 주기는 10초 고정이다.
- Emergency Hibernation 임계 온도는 설정 가능하며 기본값은 93도이고, runtime에서 사용하기 전에 항상 70도에서 110도 범위로 clamp해야 한다.
- 관측 온도가 clamp된 임계값 이상이 되면, LidGuard는 pending post-stop suspend를 취소하고, `reason = EmergencyHibernation`으로 pre-suspend webhook을 5초 timeout으로 보낸 뒤 즉시 hibernate를 요청해야 한다.
- Emergency Hibernation은 일반 suspend mode, post-stop suspend delay, post-stop suspend sound, sound volume override 설정을 무시한다.
- Emergency Hibernation webhook timeout 또는 실패가 즉시 hibernate 요청을 막으면 안 된다.

### 프로세스 종료 감시

Hook stop 이벤트가 누락될 수 있으므로 LidGuard는 에이전트 프로세스도 감시한다.

- hook이 parent process id를 제공할 수 있으면 그 값을 우선 사용한다.
- parent process id가 없으면, 그 fallback이 충분히 신뢰할 수 있는 provider에 한해서만 hook working directory와 `ICommandLineProcessResolver`를 사용한다. Codex는 여기서 예외적으로, 해석된 Codex 후보 프로세스 자신이나 직계 부모가 `cmd.exe`, `pwsh.exe`, `powershell.exe` 인 경우에만 implicit fallback을 허용하고, `process=none` Codex 세션은 그 working-directory cleanup 경로에서 제외한다.
- Windows에서는 대상 프로세스를 synchronize/query 권한으로 열고 `WaitForSingleObject`로 기다린다.
- 같은 세션에 대해 여러 cleanup 신호가 와도 첫 번째 cleanup만 의미 있게 처리되어야 한다.
- provider가 짧게 사라지는 wrapper 프로세스를 실행하고 실제 agent를 다른 프로세스로 넘긴다면 provider별 프로세스 선택 정책이 필요할 수 있다.

## Runtime 동작

### 현재 Windows CLI 경로

- `LidGuard`는 `help`, `start`, `stop`, `remove-pre-suspend-webhook`, `remove-post-session-end-webhook`, `remove-session`, `status`, `settings`, `cleanup-orphans`, `current-lid-state`, `current-monitor-count`, `current-temperature`, `suspend-history`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `mcp-status`, `mcp-install`, `mcp-remove`, `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`, `preview-system-sound`, `preview-current-sound`, `mcp-server`, `provider-mcp-server`를 파싱한다.
- `help`는 카테고리별 명령 overview와 약식 설명을 출력하고, `help <command>`는 한 명령 또는 인식된 명령 alias에 대한 focused detailed help를 출력한다.
- `<command> --help`는 같은 help metadata를 사용하며, 대상 명령이 옵션을 검증하거나 명령별 작업을 수행하기 전에 반환한다.
- `start`, `codex-hook`와 `claude-hook`의 `UserPromptSubmit` 경로, 그리고 `copilot-hook`의 `userPromptSubmitted` 경로는 저장된 기본 설정을 읽고 start IPC 요청에 포함한다.
- `remove-session --all`은 runtime이 현재 추적 중인 모든 활성 세션을 수동 삭제한다.
- `remove-session`은 session identifier로 활성 세션을 수동 삭제하고, `--provider`가 생략되면 같은 session identifier를 가진 모든 활성 세션을 삭제한다. `--provider mcp`를 사용할 때는 `--provider-name`으로 특정 MCP 기반 provider 하나로 범위를 좁힐 수 있고, `--provider-name`을 생략하면 같은 session identifier를 가진 모든 MCP 기반 세션을 삭제한다.
- `remove-pre-suspend-webhook`은 설정된 pre-suspend webhook URL을 비우고, 현재 설정된 웹훅이 없으면 그렇게 보고한다.
- `remove-post-session-end-webhook`은 설정된 post-session-end webhook URL을 비우고, 현재 설정된 웹훅이 없으면 그렇게 보고한다.
- `current-lid-state`는 closed-lid 정책 판단에 쓰는 것과 같은 `GUID_LIDSWITCH_STATE_CHANGE` 기준으로 현재 lid switch state를 출력한다.
- `current-monitor-count`는 closed-lid 정책 판단에 쓰는 것과 같은 기본 Windows monitor visibility check 기준으로 현재 visible display monitor count를 출력하되, 최종 suspend 가능성 확인에서 쓰는 internal-display 제외는 적용하지 않는다.
- `current-temperature`는 선택된 집계 기준으로 계산한 현재 system thermal-zone 온도를 섭씨로 출력하고, thermal-zone 데이터를 구할 수 없으면 unavailable로 보고한다.
- `suspend-history`는 `%LOCALAPPDATA%\LidGuard\suspend-history.log`에서 최근 suspend request 기록을 출력하며, 가능한 경우 mode, reason, result, active session count, 관련 session 또는 Emergency Hibernation 온도 정보를 포함한다.
- `status`, `suspend-history`, `hook-events`는 저장된 timestamp를 현재 시스템 로컬 시간으로 표시하되, 기반 session, history, hook log 저장소는 UTC를 유지한다.
- `settings`는 기본 설정을 출력/수정하고, 실행 중인 runtime이 있으면 즉시 반영한다.
- `settings`는 `--emergency-hibernation-on-high-temperature`, `--emergency-hibernation-temperature-mode`, `--emergency-hibernation-temperature-celsius`도 제공하며, 임계 온도 옵션은 70부터 110까지만 허용한다.
- `settings`는 post-stop sound 재생 중 master volume을 임시 override하기 위한 `--post-stop-suspend-sound-volume-override-percent off|<1-100>`도 제공하며, `off`는 기능을 끄고 범위를 벗어난 값은 거부한다.
- `settings`는 최근 suspend history 보존 개수를 위한 `--suspend-history-count off|<count>`도 제공하며, `off`는 기록을 끄고 활성화된 count는 최소 1이어야 한다.
- `settings`는 비활동 세션 soft-lock 전환을 위한 `--session-timeout-minutes off|<minutes>`도 제공하며, `off`는 timeout soft-lock을 끄고 활성화된 값은 최소 1이어야 한다.
- `settings`는 모든 활성 세션이 사라지고 pending cleanup이 끝난 뒤 server runtime cleanup을 위한 `--server-runtime-cleanup-delay-minutes off|<minutes>`도 제공하며, `off`는 즉시 종료이고 활성화된 값은 최소 1이어야 한다.
- `settings`는 suspend를 예약하지 않는 provider 정상 session-end 알림을 위한 `--post-session-end-webhook-url <http-or-https-url>`도 제공한다.
- `preview-system-sound`와 `preview-current-sound`는 저장된 post-stop suspend sound volume override 설정을 적용하고 재생이 끝날 때까지 기다린다. `preview-current-sound`는 저장된 post-stop suspend sound를 재생하며, 설정된 sound가 없으면 설정 안내를 출력한다.
- `hook-install`, `hook-status`, `hook-remove`, `hook-events`는 `--provider`가 생략되면 `codex`, `claude`, `copilot`, `all` 중 선택을 요청한다.
- `mcp-status`, `mcp-install`, `mcp-remove`도 `--provider`가 생략되면 `codex`, `claude`, `copilot`, `all` 중 선택을 요청한다.
- `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`는 Codex, Claude Code, GitHub Copilot CLI 전용 MCP 등록 명령을 쓰지 않고, 호출자가 넘긴 JSON 설정 파일 경로를 직접 수정한다.
- `--provider all`은 기본 configuration root가 이미 존재하는 provider에 대해서만 설치/제거/상태 확인/hook event 출력을 수행하고, 없는 provider는 skipped로 보고한다.
- `mcp-status --provider all`, `mcp-install --provider all`, `mcp-remove --provider all`도 기본 configuration root가 이미 존재하는 provider에 대해서만 처리하고, 없는 provider는 skipped로 보고한다.
- provider 파라미터를 받는 새 CLI 명령을 추가할 때는 provider 생략 시 조용히 기본값을 쓰지 말고 사용자에게 선택을 요청한다.
- runtime이 없으면 `start`가 detached `run-server`를 실행한다.
- `run-server`는 named mutex `Local\LidGuard.Runtime.v1`을 획득한다.
- `run-server`는 hook caller가 stdout/stderr pipe를 읽다가 hang되지 않도록 상속된 stdout/stderr에서 분리된다.
- runtime 통신은 local named pipe를 사용한다.
- 세션 실행 이벤트는 `%LOCALAPPDATA%\LidGuard\session-execution.log`에 JSON lines로 기록하며 최신 500개를 유지한다. 비활동 timeout으로 인한 soft-lock 전환은 `session-timeout-softlock-recorded`로 기록된다.
- First-chance, unhandled, unobserved task exception은 inner exception 상세를 포함해 `%LOCALAPPDATA%\LidGuard\log\exceptions.log`에 append한다. Unobserved task exception은 이 처리 과정에서 observed 상태로 표시해야 한다.
- 최근 suspend request 기록은 `%LOCALAPPDATA%\LidGuard\suspend-history.log`에 JSON lines로 기록되며, 활성화되어 있을 때 설정된 최신 entry count를 유지한다.
- Provider hook event log는 start 이벤트 수신 시 `prompt` 필드를 기록한다. 대상은 Codex와 Claude의 `UserPromptSubmit`, GitHub Copilot CLI의 `userPromptSubmitted`다.
- 기본 설정은 `%LOCALAPPDATA%\LidGuard\settings.json`에 저장한다.

### MCP 서버

- `LidGuard`는 `lidguard mcp-server`를 통해 로컬 자동화 클라이언트를 위한 stdio MCP 서버를 호스팅한다.
- `mcp-status`는 provider의 global/user MCP 설정 파일을 직접 검사해 `lidguard` 서버 엔트리가 있는지와 여전히 `mcp-server`를 가리키는지를 보고한다.
- `mcp-install`과 `mcp-remove`는 Codex, Claude Code, GitHub Copilot CLI에 대해 `lidguard`라는 이름의 user/global LidGuard stdio MCP 서버를 등록하거나 제거한다.
- `mcp-install`은 이미 설치된 관리형 LidGuard MCP 등록을 발견하면 기존 provider 엔트리를 먼저 제거한 뒤 현재 command와 arguments로 다시 설치해서 갱신한다.
- `mcp-install`은 stdio MCP 서버 등록 시 Windows `.cmd` shim보다 현재 `lidguard.exe` 경로를 우선 사용한다. Shim wrapper 프로세스가 MCP 클라이언트 아래에 계속 보일 수 있고, 에이전트 작업으로 오인되면 안 되기 때문이다.
- 일반 MCP 서버는 `get_settings_status`, `list_sessions`, `update_settings`, `remove_session`, `set_session_soft_lock`, `clear_session_soft_lock`을 노출한다.
- `list_sessions`는 전체 settings payload 없이 활성 세션 목록과 runtime lid/session state를 반환한다.
- `update_settings`는 여러 설정 필드를 한 요청에서 함께 받아 저장한다.
- `update_settings`는 `sessionTimeoutMinutes`로 비활동 세션 timeout도 노출하며, `off` 또는 최소 1 이상의 활성화 minute count를 받는다.
- `update_settings`는 `serverRuntimeCleanupDelayMinutes`로 server runtime cleanup 지연 시간도 노출하며, `off` 또는 최소 1 이상의 활성화 minute count를 받는다.
- `update_settings`는 `postSessionEndWebhookUrl`로 post-session-end webhook URL을 노출하며, 빈 문자열로 지울 수 있다.
- `remove_session`은 session identifier로 활성 세션을 수동 삭제하고, 필요하면 provider 하나와 MCP provider name 하나로 범위를 좁힌다.
- `set_session_soft_lock`과 `clear_session_soft_lock`은 provider와 session identifier를 직접 받는 범용 도구라서, MCP Provider가 아닌 흐름도 이 값을 공급할 수 있으면 MCP 기반 soft-lock 제어를 사용할 수 있다.
- `LidGuard`는 `lidguard provider-mcp-server --provider-name <name>`을 통해 별도의 stdio Provider MCP 서버도 호스팅한다.
- `provider-mcp-install`과 `provider-mcp-remove`는 호출자가 넘긴 JSON 설정 파일을 직접 수정해서 `provider-mcp-server`용 관리 stdio 서버 엔트리를 등록하거나 제거하며, 이 경로는 Codex, Claude Code, GitHub Copilot CLI 전용 MCP 등록 흐름을 의도적으로 재사용하지 않는다.
- `provider-mcp-install`은 `mcp-install`과 같은 MCP 실행 파일 선택 정책을 사용해서 Windows `.cmd` shim보다 현재 `lidguard.exe` 경로를 우선한다.
- Provider MCP 서버는 `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`을 노출한다.
- `provider_start_session`은 새로운 Provider 세션이 자율 작업을 시작하기 전에 한 번 호출하도록 의도한다. 이 도구는 새 GUID의 첫 블록에서 8자리 소문자 16진수 `sessionIdentifier`를 생성해 반환한다.
- 모델은 `provider_start_session`이 반환한 정확한 `sessionIdentifier`를 작업이 정말 끝날 때까지 `provider_set_soft_lock`, `provider_clear_soft_lock`, `provider_stop_session`에 그대로 재사용해야 한다.
- `provider_stop_session`은 작업이 정말 끝난 경우에만 턴 종료 전에 호출하도록 의도한다.
- `provider_set_soft_lock`은 모델이 사용자 입력이 필요해 턴을 마치려 할 때 LidGuard keep-awake 보호를 해제하려고 호출하도록 의도한다. 이 도구 자체가 턴을 끝내주지는 않으며, 호출 뒤에도 모델이 직접 대화를 종료하거나 사용자에게 제어를 넘겨야 한다.
- 사용자가 답해 이전에 soft lock된 Provider MCP 세션을 재개할 때는 새 세션을 다시 시작하지 말고, 먼저 기존 `sessionIdentifier`로 `provider_clear_soft_lock`을 호출해야 한다.
- Provider MCP 동작은 본질적으로 모델 의존적이다. 모델이 이 도구들을 올바른 시점에 호출한다고 LidGuard가 보장할 수 없으므로, 이 통합은 항상 best-effort로 문서화해야 한다.
- MCP 설정 변경은 CLI와 같은 named-pipe 클라이언트와 설정 저장소를 사용해 동기화하고, runtime이 없다고 해서 `run-server`를 새로 띄우지는 않는다.
- MCP 서버 로그는 stdio 트래픽을 깨지 않도록 stderr에만 남겨야 한다.

### 활성 세션 정책

- 세션 상태는 active session 기준으로 ref-count된다.
- `AgentProvider.Mcp` 세션은 여러 MCP 기반 provider가 같은 session identifier를 써도 충돌하지 않도록 provider name도 함께 가진다.
- 각 세션은 마지막 활동 시각과 soft-lock 상태, 이유, 감지 시각도 함께 가진다.
- 하나 이상의 활성 세션이 있더라도, 적어도 하나의 세션이 soft-lock이 아니어야만 공유 `SystemRequired`, `AwayModeRequired` power request를 유지한다.
- 남아 있는 모든 활성 세션이 soft-lock 상태가 되면, LidGuard는 stop 이벤트가 오기 전에도 suspend 가능 상태로 본다.
- start/update 및 새 tool 실행 같은 provider activity가 감지되면 해당 세션의 마지막 활동 시각을 갱신한다. provider activity는 해당 세션의 현재 soft-lock 상태도 해제한다.
- soft-lock 설정은 자율 작업이 아니라 대기 상태 전환을 뜻하므로 마지막 활동 시각을 갱신하지 않는다.
- 세션이 설정된 비활동 세션 timeout에 도달하면 LidGuard는 timeout 사유를 남긴 채 soft-lock으로 전환하고, 다른 soft-locked 세션과 동일한 suspend-eligible 처리 흐름을 적용한다.
- Codex, Claude, GitHub Copilot CLI 세션은 공유 `AgentTranscriptMonitor` 구현으로 transcript JSONL을 감시한다. transcript 길이가 증가하거나 `LastWriteTimeUtc`가 전진하면 보통 tool event와 같은 activity 경로로 세션의 마지막 활동 시각을 갱신하고 현재 soft-lock 상태를 해제하지만, provider별 transcript detector가 먼저 stop 또는 soft-lock 신호를 보고하면 그 신호를 우선 처리한다.
- Codex transcript profile은 hook이 준 `transcript_path`를 우선 사용하고, 없으면 session id 기준으로 `~/.codex/sessions` 아래에서 유일하게 매칭되는 transcript를 fallback으로 사용한다. 최신 transcript record가 payload type `turn_aborted`인 `event_msg`이면 LidGuard는 이를 중단된 Codex turn으로 보고 activity 기록 대신 일반 stop 경로로 세션을 정리한다. 그렇지 않고 최근 transcript record에 `request_user_input` 이름의 `response_item` `function_call`이 있으며 같은 `call_id`의 `function_call_output`이 아직 없으면, LidGuard는 Codex 세션을 `codex_transcript_request_user_input_pending` 사유로 soft-lock 처리한다.
- Claude transcript profile은 hook이 준 `transcript_path`를 우선 사용하고, 없으면 session id 기준으로 `~/.claude/projects` 아래에서 유일하게 매칭되는 transcript를 fallback으로 사용한다. 최신 transcript record가 `user` record이고 text content가 `[Request interrupted by user]` 또는 `[Request interrupted by user for tool use]`이면 LidGuard는 이를 중단된 Claude turn으로 보고 activity 기록 대신 일반 stop 경로로 세션을 정리한다.
- GitHub Copilot CLI transcript profile은 hook이 준 `transcriptPath` / `transcript_path`를 우선 사용하고, 없으면 `COPILOT_HOME\session-state\<sessionId>\events.jsonl` 또는 `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`을 fallback으로 사용한다. 최신 JSONL record의 top-level `type`이 `abort`이면 LidGuard는 이를 Copilot abort 신호로 보고 activity 기록 대신 일반 stop 경로로 세션을 정리한다.
- `AgentProvider.Mcp` 세션은 model-managed Provider MCP 세션에서 신뢰할 만한 단일 CLI 프로세스를 식별하기 어렵기 때문에, working directory만으로 watched process를 자동 해석하지 않는다.
- `AgentProvider.Codex` 세션은 explicit watched process id가 없더라도, 해석된 후보 프로세스 자신이나 직계 부모가 `cmd.exe`, `pwsh.exe`, `powershell.exe` 인 shell-hosted 경우에만 working directory 기준 watched process 자동 해석을 허용한다.
- shell-hosted Codex watchdog이나 `cleanup-orphans` 가 working directory 기준으로 세션을 정리할 때는, 그 directory의 watched Codex 세션만 제거하고 `process=none` Codex 세션은 의도적으로 남긴다.
- 선택적 lid action 변경은 한 번 백업하고 마지막 활성 세션이 끝난 뒤 복원한다.
- 공유 보호가 유지되고 덮개가 닫혀 있는 동안 Emergency Hibernation thermal monitor는 10초마다 poll하고, 보호가 해제되거나 기능이 꺼지면 자동으로 멈춘다.
- 같은 세션에 여러 stop 신호가 와도 cleanup side effect가 반복되면 안 된다.
- 활성 세션 수가 `0`이 되면, post-stop suspend 요청, lid-action 복원, pre-suspend webhook, post-session-end webhook, post-stop sound 같은 후처리가 더 이상 남아 있지 않은 상태에서 설정된 server runtime cleanup 지연 시간이 지난 뒤 runtime은 종료해야 한다.
- Provider가 보고한 정상 stop이 active session을 제거했고 그 stop에서 suspend가 예약되지 않으면, runtime은 post-session-end webhook을 background로 보내고, 그 전송이 끝나거나 timeout될 때까지 runtime cleanup을 pending으로 유지하며, webhook 실패는 stop 실패로 만들지 않고 log만 남긴다.
- persistent pending backup state는 아직 없으며, 다음 resilience 우선순위다.

### 설정 기본값

- 일반 idle sleep 방지: 켜짐.
- away-mode sleep 방지: 켜짐.
- display sleep 방지: 꺼짐.
- 임시 lid close action 변경: headless CLI runtime에서는 켜짐이며 AC/DC에 함께 적용.
- post-stop suspend 지연 시간: 기본 10초, `0`이면 즉시 suspend.
- post-stop suspend 모드: Sleep 기본, Hibernate 선택 가능.
- post-stop suspend sound: 기본 off.
- post-stop suspend sound 볼륨 override: 기본 off, 1%에서 100%까지 허용하며 범위를 벗어난 값은 clamp하지 않고 거부.
- suspend history 기록: 기본 on, 최신 10개 보존, `off` 또는 최소 1 이상의 활성화 count를 허용.
- 비활동 세션 timeout: 기본 12분, `off` 또는 최소 1 이상의 활성화 minute count를 허용하며 제품 수준 최대값은 두지 않는다.
- 모든 세션이 사라진 뒤 server runtime cleanup 지연 시간: 기본 10분, `off`는 즉시 종료이며 최소 1 이상의 활성화 minute count를 허용하고 제품 수준 최대값은 두지 않는다.
- pre-suspend webhook URL: 기본 off.
- post-session-end webhook URL: 기본 off.
- 고온 Emergency Hibernation: 기본 켜짐.
- Emergency Hibernation 온도 기준: 기본 Average, 선택값 Low 및 High.
- Emergency Hibernation 온도 임계값: 기본 93도, runtime에서는 70도에서 110도로 clamp.
- 덮개 닫힘 상태의 PermissionRequest 결정: 기본 Deny, Allow 선택 가능.
- PermissionRequest hook은 runtime이 `LidSwitchState = Closed`와 `VisibleDisplayMonitorCount = 0`을 함께 보고할 때만 구조화된 allow/deny 결정을 반환하고, 그 외에는 빈 stdout으로 provider의 기본 권한 흐름을 유지한다.
- 현재 Claude와 GitHub Copilot CLI의 closed-lid `PermissionRequest` 출력은 `interrupt: true`도 함께 넣는다. 앞으로 다른 provider hook이 비슷한 JSON 모양을 쓰더라도 provider가 다르면 공용 DTO로 합치지 말고 provider별 hook 출력 타입을 유지한다.
- Claude `Elicitation` hook은 runtime이 `LidSwitchState = Closed`와 `VisibleDisplayMonitorCount = 0`을 함께 보고할 때만 구조화된 `cancel`을 반환하고, 그 외에는 빈 stdout으로 Claude의 기본 elicitation 흐름을 유지한다.
- parent process watchdog: 켜짐.

## 구현된 컴포넌트

### Commons

- `Sessions`
  - `AgentProvider`
  - `LidGuardSessionKey`
  - `LidGuardSessionSoftLockState`
  - `LidGuardSessionStartRequest`
  - `LidGuardSessionStopRequest`
  - `LidGuardSessionSnapshot`
  - `LidGuardSessionRegistry`
- `Settings`
  - `ClosedLidPermissionRequestDecision`
  - `EmergencyHibernationTemperatureMode`
  - `LidGuardSettings`
  - `LidGuardSettings.Default`
  - `LidGuardSettings.HeadlessRuntimeDefault`
  - `LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius`
  - `LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent`
  - `LidGuardSettings.IsValidSuspendHistoryEntryCount`
  - `LidGuardSettings.IsValidSessionTimeoutMinutes`
  - `LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes`
  - `LidGuardSettings.Normalize`
- `Results`
  - `LidGuardOperationResult`
  - `LidGuardOperationResult<TValue>`
- `Platform`
  - `ILidGuardRuntimePlatform`
  - `LidGuardRuntimeServiceSet`
- `Services`
  - `IPowerRequestService`
  - `ILidGuardPowerRequest`
  - `ILidActionService`
  - `ISystemSuspendService`
  - `IProcessExitWatcher`
  - `ICommandLineProcessResolver`
  - `ILidStateSource`
  - `IVisibleDisplayMonitorCountProvider`
  - `IPostStopSuspendSoundPlayer`
  - `ISystemAudioVolumeController`
  - `SystemAudioVolumeState`
- `Power`
  - `PowerRequestOptions`
  - `PowerLine`
  - `LidAction`
  - `LidActionBackup`
  - `LidActionPolicyController`
  - `LidSwitchState`
  - `SystemSuspendMode`
- `Hooks`
  - Codex hook input 모델.
  - Claude hook input 모델.
  - GitHub Copilot CLI hook input 모델.
  - Codex 설치 request/result/inspection 모델.
  - Claude 설치 request/result/inspection 모델.
  - GitHub Copilot CLI 설치 request/result/inspection 모델.
  - Codex `config.toml` managed block 생성과 inspection.
  - Claude `settings.json` managed hook 생성과 inspection.
  - GitHub Copilot CLI managed hook JSON 생성과 inspection.

`LidActionPolicyController`는 AC/DC lid close action을 함께 백업하고, `DoNothing`을 쓰고, 백업값을 복원한다.

### LidGuard 앱

- `Ipc`
  - `LidGuardPipeCommands`
  - `LidGuardPipeNames`
  - `LidGuardPipeRequest`
  - `LidGuardPipeResponse`
  - `LidGuardRuntimeClient`
  - `LidGuardSessionStatus`
- `Settings`
  - `LidGuardSettingsStore`
  - `LidGuardSettingsFileJsonSerializerContext`
  - `ServerRuntimeCleanupConfiguration`
- `Control`
  - `LidGuardControlService`
  - `LidGuardControlSnapshot`
  - `LidGuardSessionRemovalOutcome`
  - `LidGuardSettingsPatch`
  - `LidGuardSettingsUpdateOutcome`
- `Runtime`
  - `AgentTranscriptMonitor`
  - `EmergencyHibernationThermalMonitor`
  - `PostStopSuspendSoundPlaybackCoordinator`
  - `SuspendHistoryLogStore`

`LidGuardControlService`는 저장된 설정을 읽고/쓰고, CLI entrypoint 없이도 실행 중 runtime에 업데이트된 설정을 밀어 넣을 수 있다.

### LidGuard Notifications 앱

- `Configuration`
  - `LidGuardNotificationsOptions`
- `Data`
  - SQLite 연결, schema 초기화, subscription 저장, webhook event 저장, delivery logging.
- `Services`
  - Web Push 전송, webhook API endpoint, background notification dispatch.
- `Pages`
  - token login, browser subscription dashboard, webhook event history.

notification server는 선택 사항이며 core LidGuard runtime 외부에서 동작한다. `eventType`을 포함한 pre-suspend 및 post-session-end webhook payload를 받고, VAPID private key는 서버에만 보관해야 한다.

### Windows

- `PowerRequestService`
  - `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest`를 사용한다.
  - system-required, away-mode-required, display-required request를 지원한다.
- `VisibleDisplayMonitorCountProvider`
  - `GetSystemMetrics(SM_CMONITORS)`에서 시작한 뒤 `WmiMonitorConnectionParams`로 inactive monitor connection을 제외한다.
  - 최종 suspend 가능성 확인에서 쓰는 internal-display 제외 flag를 받으므로, status와 진단용 monitor count는 활성 내부 노트북 패널을 계속 보고할 수 있다.
- `LidActionService`
  - 활성 전원 계획의 `LIDACTION`을 읽고 쓴다.
- `ProcessExitWatcher`
  - synchronize/query 권한으로 프로세스를 연다.
  - `WaitForSingleObject`로 기다린다.
- `CommandLineProcessResolver`
  - hook이 parent process id를 제공하지 않을 때 사용한다.
  - hook working directory와 일치하는 CLI 계열 프로세스를 찾는다.
  - command line이 `codex-hook`, `claude-hook`, `copilot-hook`, `mcp-server`, `provider-mcp-server`를 실행하는 transient LidGuard utility 프로세스는 제외한다. 따라서 MCP launcher wrapper가 watched agent process로 취급되지 않는다.
  - AOT 친화성을 위해 WMI 대신 remote process PEB에서 current directory를 읽는다.
  - 후보 프로세스 이름은 `codex`, `claude`, `copilot`, `cmd`, `pwsh`, `powershell`, `node`, `dotnet`, `gh`다.
- `LidSwitchNotificationRegistration`
  - `GUID_LIDSWITCH_STATE_CHANGE`를 등록한다.
  - broadcast 값을 `LidSwitchState`로 변환한다.
- `SystemSuspendService`
  - `SeShutdownPrivilege`를 활성화한다.
  - sleep/hibernate를 위해 `SetSuspendState`를 호출한다.
- `SystemAudioVolumeController`
  - Windows Core Audio endpoint volume API로 post-stop suspend sound 재생을 위한 기본 render 출력 master volume과 mute 상태를 캡처, 임시 적용, 복원한다.
- `LidGuardRuntimePlatform`
  - Windows 전원/프로세스 서비스를 Commons runtime platform abstraction에 맞춘다.
  - Windows-only 서비스가 생성되기 전에 미지원 플랫폼을 보고한다.
- `CodexHookInstaller`
  - `%USERPROFILE%\.codex\config.toml` 또는 `CODEX_HOME\config.toml`을 해석한다.
  - LidGuard-managed Codex hook block을 설치, 제거, 검사한다.
  - managed block marker가 없으면 status는 필수 hook event 안의 유효한 `lidguard ... codex-hook` command 항목을 감지하고, remove는 있을 경우 선택적 `SessionEnd` hook도 함께 정리한다.
  - 설정된 경우 기존 config 파일을 쓰기 전에 백업한다.
- `CodexHookEventLog`
  - Codex hook 진단 로그를 기록한다.
- `ClaudeHookInstaller`
  - `CLAUDE_CONFIG_DIR\settings.json` 또는 `%USERPROFILE%\.claude\settings.json`을 해석한다.
  - `settings.json` 안의 LidGuard-managed Claude hook 항목을 설치, 제거, 검사한다.
  - 설정된 경우 기존 config 파일을 쓰기 전에 백업한다.
- `ClaudeHookEventLog`
  - Claude hook 진단 로그를 기록한다.
- `GitHubCopilotHookInstaller`
  - `COPILOT_HOME\hooks\lidguard-copilot-cli.json` 또는 `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`을 해석한다.
  - 기본적으로 LidGuard-managed 전역 GitHub Copilot CLI hook 파일을 설치, 제거, 검사한다.
  - user-level hooks, user settings, repository hooks, repository Copilot settings 안의 non-LidGuard `agentStop` hook을 스캔해 continuation risk를 경고한다.
  - 설정된 경우 기존 hook 파일을 쓰기 전에 백업한다.
- `GitHubCopilotHookEventLog`
  - GitHub Copilot CLI hook 진단 로그를 기록한다.

### MCP

- `LidGuardMcpServerCommand`
  - 메인 `lidguard` 실행 파일에서 stdio MCP 서버를 호스팅한다.
- `ProviderMcpServerCommand`
  - 메인 `lidguard` 실행 파일에서 전용 stdio Provider MCP 서버를 호스팅한다.
- `LidGuardSettingsMcpTools`
  - `get_settings_status`를 노출한다.
  - 전체 settings payload 없이 활성 세션 목록을 가져오는 `list_sessions`를 노출한다.
  - Emergency Hibernation 온도 설정과 post-stop suspend sound 볼륨 override percent를 포함해 여러 설정 필드를 한 번의 호출에서 바꿀 수 있는 `update_settings`를 노출한다.
  - `update_settings`는 `suspendHistoryEntryCount`로 suspend history 보존 설정도 노출하며, `off` 또는 최소 1 이상의 활성화 count를 받는다.
  - `update_settings`는 `sessionTimeoutMinutes`로 비활동 세션 timeout도 노출하며, `off` 또는 최소 1 이상의 활성화 minute count를 받는다.
  - `update_settings`는 `serverRuntimeCleanupDelayMinutes`로 server runtime cleanup 지연 시간도 노출하며, `off` 또는 최소 1 이상의 활성화 minute count를 받는다.
  - `update_settings`는 `postSessionEndWebhookUrl`로 post-session-end webhook URL을 노출한다.
  - session identifier 기반 수동 활성 세션 삭제와 선택적 provider / MCP provider-name 필터를 위한 `remove_session`을 노출한다.
  - provider / session 대상 soft-lock 제어를 위한 `set_session_soft_lock`, `clear_session_soft_lock`을 노출한다.
- `LidGuardProviderMcpTools`
  - 모델 주도 Provider MCP 통합용 `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`을 노출한다.
- `LidGuard`의 MCP 호스팅
  - 공식 C# SDK의 `WithStdioServerTransport()`와 `WithTools<LidGuardSettingsMcpTools>()`를 사용한다.
  - MCP stdio 응답이 깨지지 않도록 host 로그를 stderr에 유지한다.

## Provider MCP 매핑

### Generic Provider MCP

- provider enum은 `AgentProvider.Mcp`다.
- Provider 세션은 `sessionId`와 `providerName`을 함께 써서 구분한다.
- `provider_start_session`은 새 GUID의 첫 8자리 소문자 16진수를 사용해 안정적인 Provider MCP `sessionId`를 생성하고 그 값을 모델에 반환한다.
- 모델은 `provider_start_session`이 반환한 정확한 `sessionId`를 세션이 정말 끝날 때까지 계속 재사용해야 한다.
- Provider MCP 설치/제거/상태 명령은 `lidguard provider-mcp-status --config <json-path>`, `lidguard provider-mcp-install --config <json-path> --provider-name <name>`, `lidguard provider-mcp-remove --config <json-path>`다.
- Provider MCP 설정은 JSON을 직접 수정하며, Codex / Claude Code / GitHub Copilot CLI 전용 MCP 등록 흐름을 재사용하지 않는다.
- Provider MCP 서버 명령은 `lidguard provider-mcp-server --provider-name <name>`다.
- Provider MCP 시작 도구는 `provider_start_session`이다.
- Provider MCP 종료 도구는 `provider_stop_session`이다.
- Provider MCP soft-lock 도구는 `provider_set_soft_lock`, `provider_clear_soft_lock`이다.
- `provider_start_session` 설명에는 새로운 세션을 시작할 때 호출하며, 재사용할 `sessionId`를 자동 생성해 준다고 적어야 한다.
- `provider_stop_session` 설명에는 작업이 정말 끝났을 때만 턴 종료 전에 호출하라고 적어야 한다.
- `provider_set_soft_lock` 설명에는 soft-lock 개념을 설명하고, 곧 사용자 입력 대기로 턴을 끝낼 예정이라면 먼저 이 도구를 호출하라고 적어야 한다. 또한 이 도구가 모델 대신 턴을 끝내주지는 못한다는 점도 설명해야 한다.
- `provider_clear_soft_lock` 설명에는 사용자가 응답한 뒤에는 새 세션을 만들지 말고, 이전에 반환받은 `sessionId`를 다시 이어서 재개하라고 적어야 한다.
- 모든 Provider MCP 동작은 모델의 준수 여부에 달려 있으므로, 보장된 동작이라고 약속하거나 문서화하면 안 된다.

## Provider Hook 매핑

### Codex CLI

- Start event: `UserPromptSubmit`.
- Permission decision event: `PermissionRequest`.
- 필수 stop event: `Stop`.
- 선택적 호환 stop event: Codex build가 실제로 내보낼 때의 `SessionEnd`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard codex-hook`, 아니면 현재 실행 파일 경로와 `codex-hook`.
- Snippet command: `lidguard codex-hooks --format config-toml`.
- Install/status/remove commands: `lidguard hook-install --provider codex`, `lidguard hook-status --provider codex`, `lidguard hook-remove --provider codex`.
- MCP status/install/remove commands: `lidguard mcp-status --provider codex`, `lidguard mcp-install --provider codex`, `lidguard mcp-remove --provider codex`.
- Codex는 `features.codex_hooks = true`가 필요할 수 있다.
- Codex MCP 등록은 `codex mcp add/remove`에 위임하고, `lidguard`라는 이름의 전역 stdio 서버 항목을 기록한다.
- `hook-install`과 `hook-status`는 `UserPromptSubmit`, `PermissionRequest`, `Stop`을 필수로 보고, `SessionEnd`는 있을 때만 별도로 표시하는 선택 훅으로 다룬다.
- `codex-hook`은 stdin에서 Codex hook JSON을 읽고 `hook_event_name`을 runtime IPC로 매핑한다.
- `UserPromptSubmit`은 내부 `start --provider codex`로 보낸다.
- `PermissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태와 visible display monitor count를 조회한 뒤, 덮개가 닫혀 있고 visible display monitor count가 `0`일 때만 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따른 구조화된 allow/deny 결정을 반환한다.
- `Stop`, 그리고 Codex build가 `SessionEnd`를 실제로 내보내는 경우 그 `SessionEnd`는 내부 `stop --provider codex`로 보낸다.
- Codex는 현재 공개 hook 표면에 대응되는 `Notification` 이벤트가 없어 notification 기반 soft-lock 감지는 아직 지원하지 않는다. 대신 LidGuard는 transcript JSONL의 Codex `request_user_input` 기반 soft-lock을 지원한다. 향후 Codex가 notification 또는 기계가 읽을 수 있는 pending-state hook을 노출하면 hook 수준 지원을 추가할 수 있다.
- Codex에는 notification 스타일의 soft-lock 해제 신호와 대응되는 tool activity hook이 없기 때문에, LidGuard는 `UserPromptSubmit`에서 받은 `transcript_path`를 기록하고 공유 transcript monitor로 transcript JSONL을 감시한다. 최근 `response_item` record에 `payload.type = function_call`, `payload.name = request_user_input`이 있으면 LidGuard는 해당 `payload.call_id`를 pending으로 추적하고, 같은 `payload.call_id`의 `payload.type = function_call_output`이 나타날 때까지 세션을 `codex_transcript_request_user_input_pending` 사유로 soft-lock 처리한다. Stop 신호나 pending `request_user_input` 신호가 없을 때는 transcript JSONL 길이 증가 또는 `LastWriteTimeUtc` 전진을 Codex provider activity로 취급해 `LastActivityAt`을 갱신하고 현재 soft-lock 상태를 표준 activity 경로로 해제한다. `transcript_path`가 비어 있으면 session id 기준으로 `~/.codex/sessions`에서 유일한 transcript 매칭을 fallback으로 사용한다. transcript monitor는 file-system 변경 알림과 짧은 metadata polling fallback을 함께 사용한다. 최신 record가 `turn_aborted` 이벤트이면 interrupted turn으로 처리해 activity 갱신 대신 추적 중인 Codex 세션을 stop한다.
- Codex hook input에는 안정적인 parent process id가 없다. 그래서 LidGuard는 Codex에 대해 explicit watched process id를 우선 사용하지만, 그 값이 없을 때도 해석된 Codex 후보 프로세스 자신이나 직계 부모가 `cmd.exe`, `pwsh.exe`, `powershell.exe` 인 shell-hosted 경우에는 working-directory fallback을 사용할 수 있다. 다만 그 cleanup 경로는 `process=none` Codex 세션을 절대 제거하지 않는다.
- Codex `PermissionRequest`는 effective closed-lid 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, visible display monitor가 하나라도 남아 있거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Codex 작업 자체는 막지 않아야 한다.
- 이 동작은 `openai/codex`의 `codex-rs` hook 소스를 분석한 결과를 근거로 한다. `exit 0`과 빈 stdout은 no-op 성공으로 처리되지만, stdout이 비어 있지 않으면 이벤트 종류에 따라 hook JSON으로 파싱되거나 일반 텍스트 컨텍스트로 해석될 수 있다.

참고:

- https://developers.openai.com/codex/hooks
- https://github.com/openai/codex

### Claude Code

- Start event: `UserPromptSubmit`.
- Activity telemetry events: `PreToolUse`, `PostToolUse`, `PostToolUseFailure`.
- Permission decision event: `PermissionRequest`.
- MCP elicitation 이벤트: `Elicitation`.
- Soft-lock notification event: `Notification`.
- Stop events: `Stop`, `StopFailure`, `SessionEnd`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard claude-hook`, 아니면 현재 실행 파일 경로와 `claude-hook`.
- Snippet command: `lidguard claude-hooks --format settings-json`.
- Install/status/remove commands: `lidguard hook-install --provider claude`, `lidguard hook-status --provider claude`, `lidguard hook-remove --provider claude`.
- MCP status/install/remove commands: `lidguard mcp-status --provider claude`, `lidguard mcp-install --provider claude`, `lidguard mcp-remove --provider claude`.
- `hook-install`과 `hook-status`는 `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `Stop`, `StopFailure`, `Elicitation`, `PermissionRequest`, `Notification`, `SessionEnd`를 모두 필수 managed hook으로 본다.
- 기본 config 경로: `CLAUDE_CONFIG_DIR`가 설정되어 있으면 `CLAUDE_CONFIG_DIR\settings.json`, 아니면 `%USERPROFILE%\.claude\settings.json`.
- Claude MCP 등록은 `%USERPROFILE%\.claude.json`의 user-scope global config를 사용하며 `claude mcp add/remove --scope user`에 위임한다.
- Windows hook config는 Claude `settings.json` command hook에 `shell = "powershell"`을 사용한다.
- 로컬에 확보한 Claude Code 소스 스냅샷을 분석한 결과, command hook은 `exit code 0`과 빈 stdout을 성공한 no-op으로 처리하고, stdout이 비어 있지 않으면 실행 경로에 따라 hook JSON 또는 일반 텍스트 출력으로 해석한다.
- 같은 로컬 소스 스냅샷 분석 기준으로 `PermissionRequest`는 hook이 `hookSpecificOutput.decision`을 포함한 구조화 JSON을 반환할 때만 programmatic allow/deny가 되며, LidGuard는 이 closed-lid 결정에 `interrupt: true`도 함께 넣어 Claude의 interactive permission 경로를 즉시 끊는다. 빈 stdout이면 일반 권한 흐름이 유지된다.
- `claude-hook`은 stdin에서 Claude hook JSON을 읽고 `hook_event_name`을 runtime IPC로 매핑한다.
- `UserPromptSubmit`은 Claude가 제공한 `transcript_path`가 있으면 함께 담아 내부 `start --provider claude`로 보낸다.
- `PreToolUse`, `PostToolUse`, interrupt가 아닌 `PostToolUseFailure`는 non-`AskUserQuestion` tool에 대해 provider activity를 기록하고 해당 세션의 현재 soft-lock 상태를 해제한다.
- `is_interrupt: true`인 `PostToolUseFailure`는 즉시 내부 `stop --provider claude`로 보낸다.
- `Elicitation`은 runtime을 stop하지 않고 runtime의 덮개 상태와 visible display monitor count를 조회한 뒤, 덮개가 닫혀 있고 visible display monitor count가 `0`일 때만 구조화된 `cancel`을 반환한다.
- `Notification`은 `permission_prompt`, `elicitation_dialog`일 때 세션을 soft-lock으로 표시하고, `elicitation_complete`, `elicitation_response`일 때 현재 soft-lock 상태를 해제한다.
- Claude transcript JSONL 변경은 Codex와 같은 공유 transcript monitor로 감시한다. `transcript_path`가 비어 있으면 session id 기준으로 `~/.claude/projects`에서 유일한 transcript 매칭을 fallback으로 사용하며, 최신 user text marker가 `[Request interrupted by user]` 또는 `[Request interrupted by user for tool use]`이면 activity 갱신 대신 추적 중인 Claude 세션을 stop한다.
- `PermissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태와 visible display monitor count를 조회한 뒤, 덮개가 닫혀 있고 visible display monitor count가 `0`일 때만 `interrupt: true`를 포함한 Claude 전용 구조화 allow/deny 결정을 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따라 반환한다.
- Claude Code 관련 설정, 지원, 문서 작업을 할 때는 사용자가 LidGuard와 함께 third-party prompt 성격의 hook을 쓰지 않도록 반드시 명시적이고 강하게 안내한다. LidGuard는 자신의 closed-lid `PermissionRequest`와 `Elicitation` 경로에 대해서만 응답해야 하며, third-party hook prompt를 대신 처리하거나 프록시할 수 있는 것처럼 설명하거나 다루면 안 된다.
- `Stop`, `StopFailure`, `SessionEnd`는 내부 `stop --provider claude`로 보낸다.
- 분석한 Claude hook input에는 `session_id`와 `cwd`는 있지만 안정적인 parent process id는 없으므로 현재 구현은 working directory 기준으로 프로세스를 찾는다.
- Claude `Elicitation`은 effective closed-lid 상태의 `cancel`에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, visible display monitor가 하나라도 남아 있거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Claude 작업 자체는 막지 않아야 한다.
- Claude `PermissionRequest`는 effective closed-lid 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, visible display monitor가 하나라도 남아 있거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Claude 작업 자체는 막지 않아야 한다.

참고:

- https://code.claude.com/docs/en/hooks

### GitHub Copilot CLI

- Start event: `userPromptSubmitted`.
- Stop event: `agentStop`, `sessionEnd`, session-state JSONL `abort`.
- Closed-lid permission 결정 event: `permissionRequest`.
- Closed-lid ask-user guard event: `toolName`이 `ask_user`인 `preToolUse`.
- Activity event: `postToolUse`.
- Soft-lock notification event: `notification_type` / `notificationType`이 `permission_prompt` 또는 `elicitation_dialog`인 `notification`.
- Telemetry-only event: `sessionStart`, `errorOccurred`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard copilot-hook --event <event-name>`, 아니면 현재 실행 파일 경로와 `copilot-hook --event <event-name>`.
- Snippet command: `lidguard copilot-hooks --format config-json`.
- Install/status/remove commands: `lidguard hook-install --provider copilot`, `lidguard hook-status --provider copilot`, `lidguard hook-remove --provider copilot`.
- MCP status/install/remove commands: `lidguard mcp-status --provider copilot`, `lidguard mcp-install --provider copilot`, `lidguard mcp-remove --provider copilot`.
- 기본 전역 config 경로: `COPILOT_HOME`이 설정되어 있으면 `COPILOT_HOME\hooks\lidguard-copilot-cli.json`, 아니면 `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`.
- GitHub Copilot CLI MCP 등록은 `copilot mcp add/remove`에 위임하고 `%USERPROFILE%\.copilot\mcp-config.json` user config를 사용한다.
- GitHub Copilot CLI는 `~/.copilot/settings.json`의 inline user hooks도 지원하고, `.github/hooks/` 및 repository Copilot settings는 user hooks와 함께 로드되므로 `hook-install`과 `hook-status`는 이 소스들도 충돌 검사용으로 함께 본다.
- `hook-install`과 `hook-status`는 `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, `permissionRequest`, `agentStop`, `errorOccurred`, 그리고 필터된 `notification` hook을 모두 요구한다.
- 공식 Copilot CLI 문서상 `agentStop` hook은 `decision: "block"`과 `reason`으로 continuation을 만들 수 있으므로, `hook-install`과 `hook-status`는 non-LidGuard `agentStop` hook이 있을 때 경고해야 한다.
- 공식 Copilot CLI hooks 문서 기준으로 `sessionStart` 같은 수동 hook은 JSON 출력 없이 로그만 남기는 shell command로 구현할 수 있으므로, 비결정형 hook에는 `exit code 0`과 빈 stdout이 유효한 no-op 패턴이다.
- 공식 hooks configuration 레퍼런스 기준으로 `preToolUse`의 출력 JSON은 optional이며, 출력을 생략하면 기본 허용으로 처리되므로 LidGuard가 명시적으로 hook 결정을 바꾸려는 경우에만 구조화 JSON을 반환하면 된다.
- 앞으로 GitHub Copilot CLI hook 출력이 다른 provider의 현재 hook JSON과 비슷해 보이더라도 GitHub Copilot CLI 전용 hook 출력 타입을 따로 둔다. Hook 계약은 provider별이며 CLI 사이에 표준화되어 있지 않다.
- CamelCase GitHub Copilot CLI hook payload는 stdin JSON에 event 이름이 항상 들어오지 않기 때문에, `copilot-hook`은 구성된 event 이름을 command line 인자로 받는다.
- `userPromptSubmitted`는 Copilot이 제공한 `transcriptPath` / `transcript_path`가 있으면 함께 담아 내부 `start --provider copilot`으로 보낸다.
- `permissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태와 visible display monitor count를 조회한 뒤, 덮개가 닫혀 있고 visible display monitor count가 `0`일 때만 `interrupt: true`를 포함한 GitHub Copilot CLI allow/deny 결정을 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따라 반환한다.
- `preToolUse`는 runtime을 stop하지 않고, 덮개가 닫혀 있고 visible display monitor count가 `0`일 때만 `ask_user`를 deny해서 사용자가 응답할 수 없는 soft lock을 막고, non-`ask_user` tool activity가 감지되면 현재 soft-lock 상태를 해제한다.
- `postToolUse`는 tool 완료 activity를 기록하고, non-`ask_user` tool에 대해 현재 soft-lock 상태를 해제한다.
- `notification`은 GitHub Copilot CLI가 `permission_prompt` 또는 `elicitation_dialog`를 알릴 때 세션을 soft-lock으로 표시한다.
- `agentStop`과 `sessionEnd`는 내부 `stop --provider copilot`으로 보낸다.
- GitHub Copilot CLI session-state JSONL 변경은 공유 transcript monitor로 감시한다. `transcriptPath` / `transcript_path`가 비어 있으면 `COPILOT_HOME\session-state\<sessionId>\events.jsonl` 또는 `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`을 fallback으로 사용하며, 최신 top-level `type`이 `abort`이면 activity 갱신 대신 추적 중인 Copilot 세션을 stop한다. 그 외 JSONL append 또는 `LastWriteTimeUtc` 전진은 `github_copilot_session_event_activity_detected` 사유로 `LastActivityAt`을 갱신하고 현재 soft-lock 상태를 해제한다.
- `sessionStart`, `errorOccurred`는 telemetry만 기록한다.
- 현재 문서화된 GitHub Copilot CLI hook payload에는 안정적인 parent process id가 없으므로 현재 구현은 working directory 기준으로 프로세스를 찾는다.
- GitHub Copilot CLI `permissionRequest`는 effective closed-lid 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, visible display monitor가 하나라도 남아 있거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환해 일반 권한 흐름을 유지한다.
- GitHub Copilot CLI `preToolUse`는 effective closed-lid 상태의 `ask_user` deny에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 그 외에는 성공 종료하면서 빈 stdout을 반환해 일반 tool 처리 흐름을 유지한다.

참고:

- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference
- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference

## CLI 예시

```powershell
lidguard start --provider codex --session "<session-id>" --parent-pid 1234
lidguard stop --provider codex --session "<session-id>"
lidguard remove-pre-suspend-webhook
lidguard remove-post-session-end-webhook
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard start --provider claude --session "<session-id>"
lidguard stop --provider claude --session "<session-id>"
lidguard claude-hook
lidguard claude-hooks --format settings-json
lidguard start --provider copilot --session "<session-id>"
lidguard stop --provider copilot --session "<session-id>"
lidguard copilot-hook --event userPromptSubmitted
lidguard copilot-hooks --format config-json
lidguard codex-hook
lidguard codex-hooks --format config-toml
lidguard hook-status --provider copilot
lidguard hook-install --provider copilot
lidguard hook-remove --provider copilot
lidguard hook-events --provider copilot --count 50
lidguard mcp-status --provider copilot
lidguard mcp-install --provider copilot
lidguard mcp-remove --provider copilot
lidguard hook-status --provider claude
lidguard hook-install --provider claude
lidguard hook-remove --provider claude
lidguard hook-events --provider claude --count 50
lidguard mcp-status --provider claude
lidguard mcp-install --provider claude
lidguard mcp-remove --provider claude
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 50
lidguard mcp-status --provider codex
lidguard mcp-install --provider codex
lidguard mcp-remove --provider codex
lidguard mcp-status --provider all
lidguard mcp-install --provider all
lidguard mcp-remove --provider all
lidguard provider-mcp-status --config "C:\path\to\mcp.json"
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
lidguard provider-mcp-remove --config "C:\path\to\mcp.json"
lidguard provider-mcp-server --provider-name "ExampleProvider"
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature --temperature-mode high
lidguard suspend-history
lidguard preview-system-sound --name Asterisk
lidguard preview-current-sound
lidguard settings
lidguard settings --emergency-hibernation-temperature-mode average
lidguard settings --change-lid-action true
lidguard settings --post-stop-suspend-delay-seconds 0
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --post-stop-suspend-sound-volume-override-percent 75
lidguard settings --post-stop-suspend-sound-volume-override-percent off
lidguard settings --suspend-history-count 10
lidguard settings --suspend-history-count off
lidguard settings --session-timeout-minutes 12
lidguard settings --session-timeout-minutes off
lidguard settings --server-runtime-cleanup-delay-minutes 10
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --post-session-end-webhook-url https://example.com/lidguard-session-ended
lidguard settings --closed-lid-permission-request-decision allow
lidguard settings --prevent-away-mode-sleep true --prevent-display-sleep true --power-request-reason "LidGuard keeps agent sessions awake"
lidguard status
lidguard cleanup-orphans
```

## MCP 서버 예시

```powershell
lidguard mcp-server
```

## 빌드 검증 메모

- 로컬 build, test, publish, pack, reinstall 검증 명령은 일시적인 Windows Defender 파일 잠금 간섭 때문에 한 번 정도 실패할 수 있다.
- 이 경우에는 더 큰 복구 작업에 들어가기 전에 동일한 검증 명령을 먼저 재시도해야 하며, 원인이 이 알려진 Defender 이슈라면 보통 재시도만으로 해결된다.
- 알려진 Defender 이슈로 첫 검증 시도가 실패했다는 이유만으로 build server를 내릴 필요는 없다.

## 남은 작업

Windows CLI hook 수신 경로는 Codex, Claude Code, GitHub Copilot CLI까지 구현되어 있다. 남은 작업은 이제 lifecycle polish와 자동 회귀 검증에 더 가깝다.

- 마지막 세션 종료 뒤 남은 post-stop cleanup 작업이 끝나는 즉시 runtime이 종료되도록 구현한다.
- 이미 수동 테스트가 완료된 provider/Windows 동작에 대한 자동 회귀 테스트 또는 검증 스크립트를 추가한다. 대상은 최신 Codex hook 동작, Claude Code hook stdout 동작, GitHub Copilot CLI hook 출력 동작, GitHub Copilot CLI user-level `~/.copilot/hooks/` 로딩과 inline `~/.copilot/settings.json` hook 조합, GitHub Copilot CLI session id 안정성, 일반 사용자 권한의 `PowerReadACValueIndex`/`PowerReadDCValueIndex` 읽기/쓰기 동작, Group Policy 또는 MDM으로 전원 설정이 막힌 경우의 fallback 메시지다.
- Codex가 향후 notification 또는 기계가 읽을 수 있는 pending-state hook 표면을 제공할 때만 direct Codex soft-lock 지원을 추가한다.

## 설계 제약

- Cross-platform 가능한 로직은 일반 `LidGuard` feature folder와 namespace에 둔다.
- Windows API 호출과 Windows-only 가정은 `LidGuard`의 `*.windows.cs` 파일에 둔다.
- 사용자가 명시적으로 요청하지 않는 한 `LidGuard.csproj`에서 Nullable을 켜지 않는다.
- `ImplicitUsings`를 유지한다.
- NativeAOT/trimming 호환성을 염두에 둔다.
- JSON으로 직렬화될 수 있는 enum을 추가할 때는 enum 타입에 `JsonStringEnumConverter<TEnum>`를 붙여 숫자가 아니라 문자열로 저장되게 한다.
- 합리적인 경우 수동 interop보다 라이브러리를 선호한다.
- Windows native API는 CsWin32를 선호한다. `NativeMethods.txt`는 작고 어느 정도 정렬된 상태로 유지한다.
- 명확한 AOT-safe 이유가 없으면 reflection-heavy, dynamic-loading, runtime-marshalling-dependent 패턴을 도입하지 않는다.
- 현재 JSON 모양이 비슷하다는 이유만으로 provider 간 hook DTO를 공유하지 않는다. Hook 계약은 provider별로 분리된 타입을 유지해야 한다.
- sleep idle timeout 변경을 다시 도입하지 않는다.
- Power plan write는 power request로 대체할 수 없는 동작에만 사용한다. 현재 대상은 `LIDACTION`이다.
- 앞으로 현지화 작업을 할 때는, 기반 IPC/log/settings 값이 안정적인 영어로 유지되더라도 최종 human-facing CLI presentation은 runtime/session status message, session list summary, management output, enum display text, placeholder까지 포함해 현지화해야 하며, localized rendering을 만들 수 있을 때 raw protocol `Message` text를 user-facing terminal output에 그대로 노출하면 안 된다.
- 1.0.0 이전에는 공개 배포되지 않은 동작이나 설정을 위해 migration 전용 legacy 코드를 추가하지 않는다.

## 실패 모드

- Hook start는 성공했지만 stop이 누락됨: parent process watcher가 cleanup해야 한다.
- Runtime이 lid action 변경 후 crash됨: 향후 pending backup state가 다음 CLI 실행 때 복원해야 한다.
- 전원 설정 변경이 정책으로 거부됨: 일반 power request는 유지하고 실패를 알려야 한다.
- Hibernate가 미지원이거나 꺼져 있음: 명확하게 실패하거나 향후 안전한 fallback을 사용한다.
- 여러 provider가 동시에 실행됨: active session을 ref-count하고 마지막 세션이 끝난 뒤 복원한다.
- 세션 중 활성 power scheme이 바뀜: v1은 원래 백업한 scheme을 복원한다.

## .NET Tool 패키지 지침

- 사용자가 명시적으로 package 생성을 요청하지 않으면 `dotnet pack`을 실행하지 않는다. `dotnet pack`은 build를 수행한다.
- NuGet upload는 `C:\Users\kck41\.codex\skills\publish-nuget\SKILL.md`의 `$publish-nuget` skill을 사용한다. raw `dotnet nuget push`는 사용하지 않는다.
- `$publish-nuget` skill은 기존 `.nupkg`만 publish한다. 사용자가 publish를 요청했지만 package가 없으면 먼저 pack을 할지 물어본다.
- 사용자가 해당 파일 작업을 명시적으로 요청하지 않는 한 `C:\Data\Scripts\publish_nuget-nopause.bat`를 열거나, 검사하거나, 인용하거나, 수정하지 않는다.
- Packaging 전 `LidGuard\LidGuard.csproj`의 package version을 확인한다.
- 공개 NuGet.org upload 전 license metadata를 확인한다. `PackageLicenseExpression` 또는 `PackageLicenseFile`이 필요하다.
- Packaging에서 `DOTNET_CLI_HOME`을 설정했다면 packaging 직후 해당 임시 디렉터리를 삭제한다.
- 로컬 packaging 후 upload 전 local package source에서 설치 smoke test를 수행한다.

Package commands:

```powershell
dotnet pack .\LidGuard\LidGuard.csproj -c Release
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x64
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x86
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-arm64
```

Expected package files:

```text
artifacts\packages\lidguard.0.1.0.nupkg
artifacts\packages\lidguard.win-x64.0.1.0.nupkg
artifacts\packages\lidguard.win-x86.0.1.0.nupkg
artifacts\packages\lidguard.win-arm64.0.1.0.nupkg
```

Publish commands:

```powershell
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x86.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-arm64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.0.1.0.nupkg"
```

Local install smoke test:

```powershell
dotnet tool install --global lidguard --add-source .\artifacts\packages --version 0.1.0
lidguard --help
lidguard status
```

## 운영 메모

- `hook-install` 후 기존 Codex와 Claude config는 의도한 `lidguard.exe` 경로를 직접 가리켜야 한다.
- Claude 배포나 설정을 도와줄 때는 사용자가 LidGuard와 함께 third-party prompt hook에 의존하지 않도록 반드시 명시적이고 강하게 경고해야 한다. LidGuard는 자신의 closed-lid permission decision이나 elicitation decision만 수행할 수 있고, 무관한 Claude hook prompt를 대신 안전하게 응답할 수는 없다고 분명히 말해야 한다.
- 테스트를 추가한다면 Commons policy controller 중심의 focused unit test와 안전한 Windows service wrapper integration-style test를 선호한다.
