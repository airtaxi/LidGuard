# LidGuard 작업 기준

이 문서는 사용자가 읽기 위한 `AGENTS.md`의 한국어판이다.

`AGENTS.md`가 LidGuard의 제품 방향, 기술 설계, 현재 구현 상태, 다음 작업의 기준 문서다. `AGENTS.md`를 의미 있게 수정할 때는 이 파일도 같은 턴에서 함께 갱신해야 한다.

## 필수 규칙

- 사용자가 명시적으로 요청하지 않으면 `git commit` 또는 `git push`를 절대 실행하지 않는다.
- 커밋은 영어로 작성한다.
- 사용자가 명시적으로 요청하지 않으면 빌드를 실행하지 않는다. 단, 변경 규모가 매우 큰 경우는 예외다.
- 작업 중 애매한 점이 있으면 즉시 사용자에게 물어보고, 가능하면 선택지를 제공한다.

## 문서 정책

- `AGENTS.md`가 LidGuard의 단일 기준 문서다.
- `AGENTS.ko.md`는 사용자가 읽기 위한 한국어 미러 문서다.
- `Plan.md`는 중복 계획 문서를 없애기 위해 삭제했다.
- 핵심 동작이나 설계를 바꿀 때는 다른 계획 문서를 다시 만들지 말고 `AGENTS.md`와 `AGENTS.ko.md`를 함께 갱신한다.

## 제품 목표

LidGuard는 Codex, Claude Code, GitHub Copilot CLI처럼 오래 실행되는 로컬 AI 코딩 에이전트를 위한 Windows 우선 유틸리티다.

목표는 에이전트 세션이 하나 이상 활성 상태일 때 Windows가 잠들지 않도록 유지하고, 세션이 끝나면 사용자의 원래 전원 정책을 복원하는 것이다.

- 에이전트 세션은 provider hook을 통해 시작된다.
- LidGuard는 활성 세션을 감지하고 추적한다.
- 활성 세션이 하나 이상 있으면 `PowerRequestSystemRequired`와 `PowerRequestAwayModeRequired`로 idle sleep을 막는다.
- 선택 설정으로 활성 전원 계획의 덮개 닫힘 동작을 임시로 `Do Nothing`으로 변경한다.
- 세션이 멈추면 모든 임시 전원 설정을 사용자의 원래 값으로 복원한다.
- 마지막 활성 세션이 끝난 뒤 노트북 덮개가 닫혀 있으면 LidGuard는 항상 suspend를 요청해야 한다.
- suspend 모드는 계속 설정 가능하며 기본값은 Sleep, 선택지는 Hibernate다.
- 마지막 세션 종료 후 suspend까지의 대기 시간도 설정 가능하며 기본값은 10초, `0`이면 즉시 suspend다.
- 마지막 세션 종료 후 재생할 소리도 선택 설정이며 기본값은 off이고, 지원되는 SystemSounds 이름 또는 재생 가능한 `.wav` 경로를 사용할 수 있다.

핵심 설계 원칙은 일반 idle sleep과 lid-close sleep을 분리해서 처리하는 것이다. 일반 idle sleep은 power request로 막고, lid-close sleep은 일반 절전 방지 API로 안정적으로 막을 수 없으므로 `LIDACTION` 정책을 백업, 변경, 복원한다.

## 저장소 구조

- `LidGuardLib.Commons`
  - .NET 10 라이브러리다.
  - 공통, 플랫폼 중립 모델과 정책을 담는다.
  - 현재 csproj에서는 nullable을 의도적으로 켜지 않는다.
  - `ImplicitUsings`가 켜져 있다.
  - NativeAOT/trimming 호환 플래그가 켜져 있다.
- `LidGuardLib.Windows`
  - `net10.0` 대상 Windows 구현 라이브러리다.
  - Windows 전용 구현을 담는다.
  - AOT 호환성을 위해 CsWin32를 `CsWin32RunAsBuildTask=true`, `DisableRuntimeMarshalling=true`로 사용한다.
- `LidGuard`
  - `net10.0` 대상 콘솔 앱이다.
  - hook-facing CLI, in-process headless runtime, stdio MCP 서버 호스팅을 함께 포함한다.
  - root namespace는 `LidGuard`, assembly/apphost 이름은 `lidguard`다.
  - NuGet package ID `lidguard`, tool command `lidguard`인 .NET tool 배포를 준비한다.
  - 지원 패키지 RID는 `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`다.
  - Windows 동작은 구현되어 있고, macOS/Linux는 현재 지원 예정 메시지를 출력한 뒤 exit code `0`을 반환한다.
  - named pipe로 runtime에 `start`, `stop`, `remove-session`, `status`, `settings`, `cleanup-orphans` 요청을 보낸다.
  - `mcp-server` 서브커맨드로 stdio MCP 서버를 호스팅한다.
  - 기본 설정 JSON은 `%LOCALAPPDATA%\LidGuard\settings.json`에 저장한다.
- `LidGuard.slnx`
  - `LidGuardLib.Commons`, `LidGuardLib.Windows`, `LidGuard`를 포함하는 루트 solution 파일이다.

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
- `WindowsLidSwitchNotificationRegistration`이 값을 `LidSwitchState`로 변환한다.
- 즉시 sleep/hibernate는 `SeShutdownPrivilege`를 켠 뒤 `SetSuspendState`를 호출한다.
- Modern Standby 시스템에서는 `SetSuspendState(false, ...)`가 `ERROR_NOT_SUPPORTED`로 실패할 수 있다. 향후에는 display-off 방식의 fallback을 고려할 수 있다.
- 마지막 활성 세션이 끝났을 때 덮개가 닫혀 있으면, 설정된 지연 시간 후에 설정된 suspend 모드로 항상 suspend를 요청해야 한다. 지연 시간이 `0`이면 즉시 suspend다.
- post-stop suspend sound가 설정되어 있으면, LidGuard는 먼저 지연 시간을 기다리고, 지정된 소리를 끝까지 재생한 뒤, 다시 덮개/세션 상태를 확인하고 suspend를 요청해야 한다.

### 프로세스 종료 감시

Hook stop 이벤트가 누락될 수 있으므로 LidGuard는 에이전트 프로세스도 감시한다.

- hook이 parent process id를 제공할 수 있으면 그 값을 우선 사용한다.
- parent process id가 없으면 hook working directory와 `ICommandLineProcessResolver`를 사용한다.
- Windows에서는 대상 프로세스를 synchronize/query 권한으로 열고 `WaitForSingleObject`로 기다린다.
- 같은 세션에 대해 여러 cleanup 신호가 와도 첫 번째 cleanup만 의미 있게 처리되어야 한다.
- provider가 짧게 사라지는 wrapper 프로세스를 실행하고 실제 agent를 다른 프로세스로 넘긴다면 provider별 프로세스 선택 정책이 필요할 수 있다.

## Runtime 동작

### 현재 Windows CLI 경로

- `LidGuard`는 `start`, `stop`, `remove-session`, `status`, `settings`, `cleanup-orphans`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `preview-system-sound`, `mcp-server`를 파싱한다.
- `start`, `codex-hook`와 `claude-hook`의 `UserPromptSubmit` 경로, 그리고 `copilot-hook`의 `userPromptSubmitted` 경로는 저장된 기본 설정을 읽고 start IPC 요청에 포함한다.
- `remove-session`은 session identifier로 활성 세션을 수동 삭제하고, `--provider`가 생략되면 같은 session identifier를 가진 모든 활성 세션을 삭제한다.
- `settings`는 기본 설정을 출력/수정하고, 실행 중인 runtime이 있으면 즉시 반영한다.
- `hook-install`, `hook-status`, `hook-remove`, `hook-events`는 `--provider`가 생략되면 `codex`, `claude`, `copilot`, `all` 중 선택을 요청한다.
- `--provider all`은 기본 configuration root가 이미 존재하는 provider에 대해서만 설치/제거/상태 확인/hook event 출력을 수행하고, 없는 provider는 skipped로 보고한다.
- provider 파라미터를 받는 새 CLI 명령을 추가할 때는 provider 생략 시 조용히 기본값을 쓰지 말고 사용자에게 선택을 요청한다.
- runtime이 없으면 `start`가 detached `run-server`를 실행한다.
- `run-server`는 named mutex `Local\LidGuard.Runtime.v1`을 획득한다.
- `run-server`는 hook caller가 stdout/stderr pipe를 읽다가 hang되지 않도록 상속된 stdout/stderr에서 분리된다.
- runtime 통신은 local named pipe를 사용한다.
- 세션 실행 이벤트는 `%LOCALAPPDATA%\LidGuard\session-execution.log`에 JSON lines로 기록하며 최신 500개를 유지한다.
- 기본 설정은 `%LOCALAPPDATA%\LidGuard\settings.json`에 저장한다.

### MCP 서버

- `LidGuard`는 `lidguard mcp-server`를 통해 로컬 자동화 클라이언트를 위한 stdio MCP 서버를 호스팅한다.
- `get_settings_status`, `list_sessions`, `update_settings`, `remove_session`을 노출한다.
- `list_sessions`는 전체 settings payload 없이 활성 세션 목록과 runtime lid/session state를 반환한다.
- `update_settings`는 여러 설정 필드를 한 요청에서 함께 받아 저장한다.
- `remove_session`은 session identifier로 활성 세션을 수동 삭제하고, 필요하면 provider 하나로 범위를 좁힌다.
- MCP 설정 변경은 CLI와 같은 named-pipe 클라이언트와 설정 저장소를 사용해 동기화하고, runtime이 없다고 해서 `run-server`를 새로 띄우지는 않는다.
- MCP 서버 로그는 stdio 트래픽을 깨지 않도록 stderr에만 남겨야 한다.

### 활성 세션 정책

- 세션 상태는 active session 기준으로 ref-count된다.
- 활성 세션이 하나 이상이면 공유 `SystemRequired`, `AwayModeRequired` power request를 유지한다.
- 선택적 lid action 변경은 한 번 백업하고 마지막 활성 세션이 끝난 뒤 복원한다.
- 같은 세션에 여러 stop 신호가 와도 cleanup side effect가 반복되면 안 된다.
- persistent pending backup state는 아직 없으며, 다음 resilience 우선순위다.

### 설정 기본값

- 일반 idle sleep 방지: 켜짐.
- away-mode sleep 방지: 켜짐.
- display sleep 방지: 꺼짐.
- 임시 lid close action 변경: headless CLI runtime에서는 켜짐이며 AC/DC에 함께 적용.
- post-stop suspend 지연 시간: 기본 10초, `0`이면 즉시 suspend.
- post-stop suspend 모드: Sleep 기본, Hibernate 선택 가능.
- post-stop suspend sound: 기본 off.
- 덮개 닫힘 상태의 PermissionRequest 결정: 기본 Deny, Allow 선택 가능.
- PermissionRequest hook은 runtime이 덮개 닫힘 상태를 보고할 때만 구조화된 allow/deny 결정을 반환하고, 그 외에는 빈 stdout으로 provider의 기본 권한 흐름을 유지한다.
- 현재 Claude와 GitHub Copilot CLI의 closed-lid `PermissionRequest` 출력은 `interrupt: true`도 함께 넣는다. 앞으로 다른 provider hook이 비슷한 JSON 모양을 쓰더라도 provider가 다르면 공용 DTO로 합치지 말고 provider별 hook 출력 타입을 유지한다.
- Claude `Elicitation` hook은 runtime이 덮개 닫힘 상태를 보고할 때만 구조화된 `cancel`을 반환하고, 그 외에는 빈 stdout으로 Claude의 기본 elicitation 흐름을 유지한다.
- parent process watchdog: 켜짐.

## 구현된 컴포넌트

### Commons

- `Sessions`
  - `AgentProvider`
  - `LidGuardSessionKey`
  - `LidGuardSessionStartRequest`
  - `LidGuardSessionStopRequest`
  - `LidGuardSessionSnapshot`
  - `LidGuardSessionRegistry`
- `Settings`
  - `ClosedLidPermissionRequestDecision`
  - `LidGuardSettings`
  - `LidGuardSettings.Default`
  - `LidGuardSettings.HeadlessRuntimeDefault`
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
- `Control`
  - `LidGuardControlService`
  - `LidGuardControlSnapshot`
  - `LidGuardSessionRemovalOutcome`
  - `LidGuardSettingsPatch`
  - `LidGuardSettingsUpdateOutcome`

`LidGuardControlService`는 저장된 설정을 읽고/쓰고, CLI entrypoint 없이도 실행 중 runtime에 업데이트된 설정을 밀어 넣을 수 있다.

### Windows

- `WindowsPowerRequestService`
  - `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest`를 사용한다.
  - system-required, away-mode-required, display-required request를 지원한다.
- `WindowsLidActionService`
  - 활성 전원 계획의 `LIDACTION`을 읽고 쓴다.
- `WindowsProcessExitWatcher`
  - synchronize/query 권한으로 프로세스를 연다.
  - `WaitForSingleObject`로 기다린다.
- `WindowsCommandLineProcessResolver`
  - hook이 parent process id를 제공하지 않을 때 사용한다.
  - hook working directory와 일치하는 CLI 계열 프로세스를 찾는다.
  - command line이 `LidGuard codex-hook`, `lidguard codex-hook`, `LidGuard claude-hook`, `lidguard claude-hook`만 실행하는 transient shell 프로세스는 제외한다.
  - AOT 친화성을 위해 WMI 대신 remote process PEB에서 current directory를 읽는다.
  - 후보 프로세스 이름은 `codex`, `claude`, `copilot`, `cmd`, `pwsh`, `powershell`, `node`, `dotnet`, `gh`다.
- `WindowsLidSwitchNotificationRegistration`
  - `GUID_LIDSWITCH_STATE_CHANGE`를 등록한다.
  - broadcast 값을 `LidSwitchState`로 변환한다.
- `WindowsSystemSuspendService`
  - `SeShutdownPrivilege`를 활성화한다.
  - sleep/hibernate를 위해 `SetSuspendState`를 호출한다.
- `WindowsLidGuardRuntimePlatform`
  - Windows 전원/프로세스 서비스를 Commons runtime platform abstraction에 맞춘다.
  - Windows-only 서비스가 생성되기 전에 미지원 플랫폼을 보고한다.
- `WindowsCodexHookInstaller`
  - `%USERPROFILE%\.codex\config.toml` 또는 `CODEX_HOME\config.toml`을 해석한다.
  - LidGuard-managed Codex hook block을 설치, 제거, 검사한다.
  - managed block marker가 없으면 status는 필수 hook event 안의 유효한 `lidguard ... codex-hook` command 항목을 감지하고, remove는 있을 경우 선택적 `SessionEnd` hook도 함께 정리한다.
  - 설정된 경우 기존 config 파일을 쓰기 전에 백업한다.
- `WindowsCodexHookEventLog`
  - Codex hook 진단 로그를 기록한다.
- `WindowsClaudeHookInstaller`
  - `CLAUDE_CONFIG_DIR\settings.json` 또는 `%USERPROFILE%\.claude\settings.json`을 해석한다.
  - `settings.json` 안의 LidGuard-managed Claude hook 항목을 설치, 제거, 검사한다.
  - 설정된 경우 기존 config 파일을 쓰기 전에 백업한다.
- `WindowsClaudeHookEventLog`
  - Claude hook 진단 로그를 기록한다.
- `WindowsGitHubCopilotHookInstaller`
  - `COPILOT_HOME\hooks\lidguard-copilot-cli.json` 또는 `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`을 해석한다.
  - 기본적으로 LidGuard-managed 전역 GitHub Copilot CLI hook 파일을 설치, 제거, 검사한다.
  - user-level hooks, user settings, repository hooks, repository Copilot settings 안의 non-LidGuard `agentStop` hook을 스캔해 continuation risk를 경고한다.
  - 설정된 경우 기존 hook 파일을 쓰기 전에 백업한다.
- `WindowsGitHubCopilotHookEventLog`
  - GitHub Copilot CLI hook 진단 로그를 기록한다.

### MCP

- `LidGuardMcpServerCommand`
  - 메인 `lidguard` 실행 파일에서 stdio MCP 서버를 호스팅한다.
- `LidGuardSettingsMcpTools`
  - `get_settings_status`를 노출한다.
  - 전체 settings payload 없이 활성 세션 목록을 가져오는 `list_sessions`를 노출한다.
  - 한 번의 호출에서 여러 설정 필드를 바꿀 수 있는 `update_settings`를 노출한다.
  - session identifier 기반 수동 활성 세션 삭제와 선택적 provider 필터를 위한 `remove_session`을 노출한다.
- `LidGuard`의 MCP 호스팅
  - 공식 C# SDK의 `WithStdioServerTransport()`와 `WithTools<LidGuardSettingsMcpTools>()`를 사용한다.
  - MCP stdio 응답이 깨지지 않도록 host 로그를 stderr에 유지한다.

## Provider Hook 매핑

### Codex CLI

- Start event: `UserPromptSubmit`.
- Permission decision event: `PermissionRequest`.
- 필수 stop event: `Stop`.
- 선택적 호환 stop event: Codex build가 실제로 내보낼 때의 `SessionEnd`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard codex-hook`, 아니면 현재 실행 파일 경로와 `codex-hook`.
- Snippet command: `lidguard codex-hooks --format config-toml`.
- Install/status/remove commands: `lidguard hook-install --provider codex`, `lidguard hook-status --provider codex`, `lidguard hook-remove --provider codex`.
- Codex는 `features.codex_hooks = true`가 필요할 수 있다.
- `hook-install`과 `hook-status`는 `UserPromptSubmit`, `PermissionRequest`, `Stop`을 필수로 보고, `SessionEnd`는 있을 때만 별도로 표시하는 선택 훅으로 다룬다.
- `codex-hook`은 stdin에서 Codex hook JSON을 읽고 `hook_event_name`을 runtime IPC로 매핑한다.
- `UserPromptSubmit`은 내부 `start --provider codex`로 보낸다.
- `PermissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태를 조회한 뒤, 덮개가 닫혀 있을 때만 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따른 구조화된 allow/deny 결정을 반환한다.
- `Stop`, 그리고 Codex build가 `SessionEnd`를 실제로 내보내는 경우 그 `SessionEnd`는 내부 `stop --provider codex`로 보낸다.
- Codex hook input에는 안정적인 parent process id가 없으므로 현재 구현은 working directory 기준으로 프로세스를 찾는다.
- Codex `PermissionRequest`는 덮개 닫힘 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Codex 작업 자체는 막지 않아야 한다.
- 이 동작은 `openai/codex`의 `codex-rs` hook 소스를 분석한 결과를 근거로 한다. `exit 0`과 빈 stdout은 no-op 성공으로 처리되지만, stdout이 비어 있지 않으면 이벤트 종류에 따라 hook JSON으로 파싱되거나 일반 텍스트 컨텍스트로 해석될 수 있다.

참고:

- https://developers.openai.com/codex/hooks
- https://github.com/openai/codex

### Claude Code

- Start event: `UserPromptSubmit`.
- Permission decision event: `PermissionRequest`.
- MCP elicitation 이벤트: `Elicitation`.
- Stop events: `Stop`, `StopFailure`, `SessionEnd`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard claude-hook`, 아니면 현재 실행 파일 경로와 `claude-hook`.
- Snippet command: `lidguard claude-hooks --format settings-json`.
- Install/status/remove commands: `lidguard hook-install --provider claude`, `lidguard hook-status --provider claude`, `lidguard hook-remove --provider claude`.
- `hook-install`과 `hook-status`는 `UserPromptSubmit`, `Stop`, `StopFailure`, `Elicitation`, `PermissionRequest`, `SessionEnd`를 모두 필수 managed hook으로 본다.
- 기본 config 경로: `CLAUDE_CONFIG_DIR`가 설정되어 있으면 `CLAUDE_CONFIG_DIR\settings.json`, 아니면 `%USERPROFILE%\.claude\settings.json`.
- Windows hook config는 Claude `settings.json` command hook에 `shell = "powershell"`을 사용한다.
- 로컬에 확보한 Claude Code 소스 스냅샷을 분석한 결과, command hook은 `exit code 0`과 빈 stdout을 성공한 no-op으로 처리하고, stdout이 비어 있지 않으면 실행 경로에 따라 hook JSON 또는 일반 텍스트 출력으로 해석한다.
- 같은 로컬 소스 스냅샷 분석 기준으로 `PermissionRequest`는 hook이 `hookSpecificOutput.decision`을 포함한 구조화 JSON을 반환할 때만 programmatic allow/deny가 되며, LidGuard는 이 closed-lid 결정에 `interrupt: true`도 함께 넣어 Claude의 interactive permission 경로를 즉시 끊는다. 빈 stdout이면 일반 권한 흐름이 유지된다.
- `claude-hook`은 stdin에서 Claude hook JSON을 읽고 `hook_event_name`을 runtime IPC로 매핑한다.
- `UserPromptSubmit`은 내부 `start --provider claude`로 보낸다.
- `Elicitation`은 runtime을 stop하지 않고 runtime의 덮개 상태를 조회한 뒤, 덮개가 닫혀 있을 때만 구조화된 `cancel`을 반환한다.
- `PermissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태를 조회한 뒤, 덮개가 닫혀 있을 때만 `interrupt: true`를 포함한 Claude 전용 구조화 allow/deny 결정을 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따라 반환한다.
- Claude Code 관련 설정, 지원, 문서 작업을 할 때는 사용자가 LidGuard와 함께 third-party prompt 성격의 hook을 쓰지 않도록 반드시 명시적이고 강하게 안내한다. LidGuard는 자신의 closed-lid `PermissionRequest`와 `Elicitation` 경로에 대해서만 응답해야 하며, third-party hook prompt를 대신 처리하거나 프록시할 수 있는 것처럼 설명하거나 다루면 안 된다.
- `Stop`, `StopFailure`, `SessionEnd`는 내부 `stop --provider claude`로 보낸다.
- 분석한 Claude hook input에는 `session_id`와 `cwd`는 있지만 안정적인 parent process id는 없으므로 현재 구현은 working directory 기준으로 프로세스를 찾는다.
- Claude `Elicitation`은 덮개 닫힘 상태의 `cancel`에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Claude 작업 자체는 막지 않아야 한다.
- Claude `PermissionRequest`는 덮개 닫힘 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환한다. Runtime 요청이 실패해도 진단은 로컬에만 기록하고 Claude 작업 자체는 막지 않아야 한다.

참고:

- https://code.claude.com/docs/en/hooks

### GitHub Copilot CLI

- Start event: `userPromptSubmitted`.
- Stop event: `agentStop`.
- Closed-lid permission 결정 event: `permissionRequest`.
- Closed-lid ask-user guard event: `toolName`이 `ask_user`인 `preToolUse`.
- Telemetry-only event: `sessionStart`, `sessionEnd`, `errorOccurred`, 그리고 `notification_type` / `notificationType`이 `permission_prompt` 또는 `elicitation_dialog`인 `notification`.
- Command path: 전역 tool이 PATH에 있으면 `lidguard copilot-hook --event <event-name>`, 아니면 현재 실행 파일 경로와 `copilot-hook --event <event-name>`.
- Snippet command: `lidguard copilot-hooks --format config-json`.
- Install/status/remove commands: `lidguard hook-install --provider copilot`, `lidguard hook-status --provider copilot`, `lidguard hook-remove --provider copilot`.
- 기본 전역 config 경로: `COPILOT_HOME`이 설정되어 있으면 `COPILOT_HOME\hooks\lidguard-copilot-cli.json`, 아니면 `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`.
- GitHub Copilot CLI는 `~/.copilot/settings.json`의 inline user hooks도 지원하고, `.github/hooks/` 및 repository Copilot settings는 user hooks와 함께 로드되므로 `hook-install`과 `hook-status`는 이 소스들도 충돌 검사용으로 함께 본다.
- `hook-install`과 `hook-status`는 `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `permissionRequest`, `agentStop`, `errorOccurred`, 그리고 필터된 `notification` hook을 모두 요구한다.
- 공식 Copilot CLI 문서상 `agentStop` hook은 `decision: "block"`과 `reason`으로 continuation을 만들 수 있으므로, `hook-install`과 `hook-status`는 non-LidGuard `agentStop` hook이 있을 때 경고해야 한다.
- 공식 Copilot CLI hooks 문서 기준으로 `sessionStart` 같은 수동 hook은 JSON 출력 없이 로그만 남기는 shell command로 구현할 수 있으므로, 비결정형 hook에는 `exit code 0`과 빈 stdout이 유효한 no-op 패턴이다.
- 공식 hooks configuration 레퍼런스 기준으로 `preToolUse`의 출력 JSON은 optional이며, 출력을 생략하면 기본 허용으로 처리되므로 LidGuard가 명시적으로 hook 결정을 바꾸려는 경우에만 구조화 JSON을 반환하면 된다.
- 앞으로 GitHub Copilot CLI hook 출력이 다른 provider의 현재 hook JSON과 비슷해 보이더라도 GitHub Copilot CLI 전용 hook 출력 타입을 따로 둔다. Hook 계약은 provider별이며 CLI 사이에 표준화되어 있지 않다.
- CamelCase GitHub Copilot CLI hook payload는 stdin JSON에 event 이름이 항상 들어오지 않기 때문에, `copilot-hook`은 구성된 event 이름을 command line 인자로 받는다.
- `userPromptSubmitted`는 내부 `start --provider copilot`으로 보낸다.
- `permissionRequest`는 runtime을 stop하지 않고 runtime의 덮개 상태를 조회한 뒤, 덮개가 닫혀 있을 때만 `interrupt: true`를 포함한 GitHub Copilot CLI allow/deny 결정을 `LidGuardSettings.ClosedLidPermissionRequestDecision`에 따라 반환한다.
- `preToolUse`는 runtime을 stop하지 않고, 덮개가 닫혀 있을 때만 `ask_user`를 deny해서 사용자가 응답할 수 없는 soft lock을 막는다.
- `agentStop`은 내부 `stop --provider copilot`으로 보낸다.
- `sessionStart`, `sessionEnd`, `errorOccurred`, 필터된 `notification`은 telemetry만 기록한다.
- 현재 문서화된 GitHub Copilot CLI hook payload에는 안정적인 parent process id가 없으므로 현재 구현은 working directory 기준으로 프로세스를 찾는다.
- GitHub Copilot CLI `permissionRequest`는 덮개 닫힘 상태의 결정에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 덮개가 열려 있거나, 알 수 없거나, runtime 상태 조회가 불가능하면 성공 종료하면서 빈 stdout을 반환해 일반 권한 흐름을 유지한다.
- GitHub Copilot CLI `preToolUse`는 덮개 닫힘 상태의 `ask_user` deny에 대해서만 구조화된 JSON stdout과 함께 성공 종료한다. 그 외에는 성공 종료하면서 빈 stdout을 반환해 일반 tool 처리 흐름을 유지한다.

참고:

- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference
- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference

## CLI 예시

```powershell
lidguard start --provider codex --session "<session-id>" --parent-pid 1234
lidguard stop --provider codex --session "<session-id>"
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
lidguard hook-status --provider claude
lidguard hook-install --provider claude
lidguard hook-remove --provider claude
lidguard hook-events --provider claude --count 50
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 50
lidguard preview-system-sound --name Asterisk
lidguard settings
lidguard settings --change-lid-action true
lidguard settings --post-stop-suspend-delay-seconds 0
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --closed-lid-permission-request-decision allow
lidguard settings --prevent-away-mode-sleep true --prevent-display-sleep true --power-request-reason "LidGuard keeps agent sessions awake"
lidguard status
lidguard cleanup-orphans
```

## MCP 서버 예시

```powershell
lidguard mcp-server
```

## 남은 작업

Windows CLI hook 수신 경로는 Codex, Claude Code, GitHub Copilot CLI까지 구현되어 있다. 남은 작업은 이제 resilience와 검증에 더 가깝다.

- Crash recovery를 위한 persistent pending backup state를 추가한다. 강제 runtime crash가 활성 전원 계획을 `DoNothing`에 고정한 채 남기면 안 되므로 추천되는 즉시 다음 작업이다.
- Runtime idle shutdown lifecycle 정책을 추가한다.
- 최신 Codex CLI와 Codex Desktop/App에서 Codex hook 동작을 검증한다.
- 최종 provider 연동 전에 분석한 Claude Code hook stdout 동작을 최신 배포판에서도 다시 검증한다.
- 최종 provider 연동 전에 문서로 확인한 GitHub Copilot CLI hook 출력 동작을 최신 CLI 빌드에서도 다시 검증한다.
- 최신 GitHub Copilot CLI 빌드에서 user-level `~/.copilot/hooks/` 로딩과 inline `~/.copilot/settings.json` hook 조합 동작을 다시 검증한다.
- GitHub Copilot CLI hook에서 parent process id를 얻을 수 있는지 검증한다.
- Claude Code Windows hook에서 parent process id를 얻을 수 있는지 검증한다.
- GitHub Copilot CLI session id 안정성을 검증한다.
- `PowerReadACValueIndex`/`PowerReadDCValueIndex`가 일반 사용자 권한에서 읽기/쓰기가 되는지 검증한다.
- Group Policy 또는 MDM으로 전원 설정이 막힌 경우의 fallback 메시지를 검증한다.

## 완료된 작업

1. ~~Windows hook-facing CLI project 추가.~~
2. ~~Windows-only process/power 동작을 `LidGuardLib.Windows`에 유지.~~
3. ~~CLI `start` 요청을 `LidGuardSessionStartRequest`로 정규화.~~
4. ~~CLI `stop` 요청을 `LidGuardSessionStopRequest`로 정규화.~~
5. ~~`--parent-pid`가 없을 때 hook working directory와 `ICommandLineProcessResolver` 사용.~~
6. ~~Local/headless orchestration path부터 구현.~~
7. ~~Headless runtime 설정 로딩 추가.~~
8. ~~`LidGuardLib.Commons`, `LidGuardLib.Windows`, `LidGuard`를 포함하는 solution 파일 추가.~~
9. ~~Codex hook parsing, snippet output, managed config install/remove/status helper 추가.~~
10. ~~Codex `Stop`을 stop 처리로 매핑하고, `SessionEnd`는 선택적 호환 stop trigger로 유지하며, `PermissionRequest`를 덮개 닫힘 상태에서만 설정 기반 allow/deny 결정으로 처리.~~
11. ~~Claude hook parsing, snippet output, managed `settings.json` install/remove/status helper 추가.~~
12. ~~Claude `Stop`, `StopFailure`, `SessionEnd`를 stop 처리로 매핑하고, `PermissionRequest`는 덮개 닫힘 상태에서만 설정 기반 allow/deny 결정으로 처리.~~
13. ~~LidGuard 설정을 조회하고 여러 설정을 한 번에 바꿀 수 있는 stdio MCP 서버 추가.~~
14. ~~닫힌 덮개 상태의 MCP elicitation 요청을 취소하는 Claude `Elicitation` hook guard 추가.~~
15. ~~마지막 세션 종료 시 덮개가 닫혀 있으면 항상 suspend를 요청하되, Sleep/Hibernate 모드 선택은 유지.~~
16. ~~post-stop suspend 지연 시간을 설정 가능하게 만들고, 기본값을 10초로 두며 `0`은 즉시 suspend로 처리.~~
17. ~~SystemSounds 또는 `.wav`를 사용하는 선택적 post-stop suspend 완료 소리를 추가하고, suspend 전에 그 재생이 끝날 때까지 기다리도록 구현.~~
18. ~~지원되는 SystemSounds 이름을 미리 들어볼 수 있는 `preview-system-sound` CLI 커맨드 추가.~~
19. ~~GitHub Copilot CLI hook parsing, snippet output, managed global hook install/remove/status helper 추가.~~
20. ~~GitHub Copilot CLI `userPromptSubmitted`와 `agentStop`을 start/stop 처리로 매핑하고, `permissionRequest`를 `interrupt: true`가 포함된 closed-lid-only settings-driven allow/deny 결정으로 처리하며, closed-lid `preToolUse` `ask_user`를 deny.~~

## 설계 제약

- Cross-platform 가능한 로직은 `LidGuardLib.Commons`에 둔다.
- Windows API 호출과 Windows-only 가정은 `LidGuardLib.Windows`에 둔다.
- 사용자가 명시적으로 요청하지 않는 한 현재 library csproj 파일에서 Nullable을 켜지 않는다.
- `ImplicitUsings`를 유지한다.
- NativeAOT/trimming 호환성을 염두에 둔다.
- JSON으로 직렬화될 수 있는 enum을 추가할 때는 enum 타입에 `JsonStringEnumConverter<TEnum>`를 붙여 숫자가 아니라 문자열로 저장되게 한다.
- 합리적인 경우 수동 interop보다 라이브러리를 선호한다.
- Windows native API는 CsWin32를 선호한다. `NativeMethods.txt`는 작고 어느 정도 정렬된 상태로 유지한다.
- 명확한 AOT-safe 이유가 없으면 reflection-heavy, dynamic-loading, runtime-marshalling-dependent 패턴을 도입하지 않는다.
- 현재 JSON 모양이 비슷하다는 이유만으로 provider 간 hook DTO를 공유하지 않는다. Hook 계약은 provider별로 분리된 타입을 유지해야 한다.
- sleep idle timeout 변경을 다시 도입하지 않는다.
- Power plan write는 power request로 대체할 수 없는 동작에만 사용한다. 현재 대상은 `LIDACTION`이다.
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
