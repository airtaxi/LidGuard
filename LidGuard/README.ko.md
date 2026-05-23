# LidGuard CLI

🌐 [English](README.md)

LidGuard는 오래 실행되는 로컬 AI 코딩 에이전트 세션을 위한 명령줄 도구입니다. Windows 보호, systemd/logind Linux 보호, macOS 보호 기능이 구현되어 있습니다.

## 설치

```powershell
dotnet tool install --global lidguard
```

도구 패키지 ID와 명령 이름은 모두 `lidguard`입니다.

설치 후 다음 명령을 실행합니다:

```powershell
lidguard help
```

## 사용법

```powershell
lidguard <command> [options]
```

옵션은 `--name value` 또는 `--name=value` 형식을 사용합니다. Boolean 옵션은 `true/false`, `yes/no`, `on/off`, `1/0`을 받습니다.

분류된 명령 개요를 보려면 다음을 실행합니다:

```powershell
lidguard help
lidguard --help
```

특정 명령의 전체 옵션과 참고 사항을 보려면 다음을 실행합니다:

```powershell
lidguard help status
lidguard status --help
```

## 세션 제어

```powershell
lidguard start --provider codex --session "<session-id>"
lidguard stop --provider codex --session "<session-id>"
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard status
lidguard live-status
lidguard cleanup-orphans
```

`start`와 `stop`에는 `--provider`가 필요합니다. `--session`은 선택 사항이며, 생략하면 LidGuard가 provider 표시 이름과 정규화된 작업 디렉터리에서 fallback 세션 식별자를 파생합니다.

`live-status`는 runtime 구독을 열고 고정 터미널 대시보드를 1초마다 다시 그리며, runtime 이벤트가 도착하면 더 빨리 갱신할 수 있습니다. runtime이 이미 실행 중이 아닐 때 새로 시작하지 않으며, 현재 lid 상태를 포함해 `status`와 같은 runtime snapshot 값, 최근 hook 처리 line, runtime flow event, suspend history 결과를 표시합니다. runtime을 사용할 수 없거나 연결이 끊기면 interactive dashboard를 유지하고 주기적으로 재연결합니다. 종료하려면 `q`, `Escape` 또는 `Ctrl+C`를 누릅니다.

## 설정과 절전

```powershell
lidguard settings
lidguard settings --change-lid-action true --suspend-mode hibernate
lidguard settings --emergency-hibernation-temperature-mode average
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --post-stop-suspend-sound-volume-override-percent 75
lidguard settings --post-stop-suspend-sound-volume-override-percent off
lidguard settings --session-timeout-minutes 12
lidguard settings --session-timeout-minutes off
lidguard settings --server-runtime-cleanup-delay-minutes 10
lidguard settings --server-runtime-cleanup-delay-minutes 0
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --post-session-end-webhook-url https://example.com/lidguard-session-ended
lidguard settings --closed-lid-stop-follow-up-webhook-url https://example.com/lidguard-follow-up
lidguard settings --closed-lid-stop-follow-up-delay-seconds 180
lidguard settings --closed-lid-stop-follow-up-sound Asterisk --override 75
lidguard settings --closed-lid-stop-follow-up-sound off --override off
lidguard settings --repeat-closed-lid-stop-follow-up true
lidguard settings --closed-lid-permission-request-decision ask
lidguard remove-pre-suspend-webhook
lidguard remove-post-session-end-webhook
lidguard remove-closed-lid-stop-follow-up-webhook
lidguard preview-system-sound Asterisk
lidguard preview-current-sound
```

옵션 없이 `settings`를 실행하면 대화형 설정 편집을 시작합니다. 세션 타임아웃 기본값은 12분이고, 끄려면 `--session-timeout-minutes off`를 사용합니다. 런타임 자동 종료는 모든 정리가 끝난 뒤 10분이 기본값입니다. 즉시 종료하려면 `--server-runtime-cleanup-delay-minutes 0`, 계속 켜 두려면 `off`를 사용합니다. 덮개 닫힘 PermissionRequest 결정은 `deny`, `allow`, `ask`를 받습니다. `ask`는 보호를 잠시 풀고 provider의 일반 권한 요청 화면을 그대로 쓰게 합니다.

절전 전 답장 알림은 "절전 전에 나에게 한 번 물어보기" 흐름입니다. `--closed-lid-stop-follow-up-webhook-url`은 알림을 보낼 URL이고, `--closed-lid-stop-follow-up-delay-seconds`는 답장을 기다릴 시간입니다. 기본값은 180초입니다. 0이면 답장 알림을 끄고, 답장 알림을 쓰려면 20초 이상으로 설정하세요. `--closed-lid-stop-follow-up-sound`는 답장 webhook 시작과 poll URL 검증이 끝난 뒤 한 번 재생됩니다. `--override`는 `--closed-lid-stop-follow-up-sound-volume-override-percent`의 짧은 별칭이며, 둘 다 지정하면 긴 옵션 값이 우선합니다. `--post-stop-suspend-delay-seconds`는 별도 안전 대기 시간입니다. 작업이 끝난 직후 바로 이어진 메시지를 받을 수 있도록 잠깐 기다린 뒤 절전이나 답장 대기를 시작합니다. 절전 전 답장 알림을 켰다면 10초 이상으로 두세요. `--repeat-closed-lid-stop-follow-up true`가 기본값입니다. 답장으로 작업을 이어간 뒤 그 작업이 다시 끝나려 할 때도 LidGuard가 한 번 더 물어볼 수 있다는 뜻입니다. 한 번만 물어보면 충분하면 `false`로 바꾸면 됩니다. 답장 알림 URL이나 두 대기 시간을 바꾸면 가능한 범위에서 AI 도구 쪽 제한 시간도 자동으로 맞춥니다.

Emergency Hibernation 온도 모드 기본값은 `Average`이며 `Low`, `Average`, `High`로 바꿀 수 있습니다. 선택 사항인 사운드 volume override들은 `off` 또는 1부터 100까지의 percent 값을 받습니다. 켜져 있으면 해당 소리가 재생되는 동안 기본 출력 장치의 master volume을 임시로 설정한 뒤 이전 volume과 mute 상태를 복원합니다. `preview-system-sound`와 `preview-current-sound`는 저장된 post-stop suspend sound override 설정을 사용하고 재생이 끝날 때까지 기다립니다. 설정된 webhook URL을 지우려면 `remove-pre-suspend-webhook`, `remove-post-session-end-webhook`, `remove-closed-lid-stop-follow-up-webhook`을 사용합니다.

## 진단

```powershell
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature high
lidguard suspend-history
lidguard linux-permission status
lidguard linux-permission check
lidguard macos-permission status
lidguard macos-permission check
```

`current-lid-state`는 LidGuard가 덮개 닫힘 정책 판단에 사용하는 동일한 플랫폼 lid-state source를 통해 현재 덮개 스위치 상태를 `Open`, `Closed`, `Unknown`으로 출력합니다.

`current-monitor-count`는 LidGuard가 덮개 닫힘 절전 정책 판단에 사용하는 동일한 기본 플랫폼 monitor visibility check로 현재 visible display monitor count를 출력합니다. 내부 노트북 패널 connection은 최종 suspend eligibility check에서만 제외됩니다.

`current-temperature`는 선택한 집계 모드로 현재 인식된 system thermal-zone 온도를 Celsius로 출력합니다. 선택 위치 인자로 `default`, `low`, `average`, `high` 중 하나를 전달하면 저장된 설정을 재사용하거나 한 번의 명령에 대해서만 override할 수 있습니다. 설정 파일이 아직 없을 때 `default`는 LidGuard의 `Average` headless runtime 기본값으로 fallback합니다.

Linux에서는 `linux-permission status`와 `linux-permission check`가 실제 suspend를 요청하지 않고 systemd/logind permission environment를 점검합니다. `linux-permission install`은 현재 사용자를 위한 LidGuard-managed polkit rule을 설치하고, `linux-permission remove`는 해당 managed rule file만 제거합니다.

macOS에서는 `macos-permission status`와 `macos-permission check`가 sleep을 요청하지 않고 `caffeinate`, `pmset`, `powermetrics` environment를 점검합니다. `macos-permission install`은 현재 사용자를 위한 LidGuard-managed sudoers rule을 설치하고, `macos-permission remove`는 해당 managed rule file만 제거합니다.

## Hook 통합

```powershell
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 20
lidguard codex-hooks
lidguard claude-hooks
lidguard copilot-hooks
lidguard wsl-hook-status --provider codex
lidguard wsl-hook-install --provider all --distro Ubuntu
lidguard wsl-hook-remove --provider claude
lidguard wsl-codex-hooks config-toml --distro Ubuntu
lidguard wsl-claude-hooks settings-json
lidguard wsl-copilot-hooks config-json
```

`hook-status`, `hook-install`, `hook-remove`, `hook-events`에서 `--provider`를 생략하면 LidGuard가 provider를 물어봅니다. `--provider all`을 사용하면 LidGuard는 기본 설정 루트가 이미 존재하는 provider만 처리하고, 없는 provider는 skipped로 보고합니다.

`wsl-*` hook 명령은 Windows 빌드에서만 사용할 수 있습니다. 이 명령은 WSL 내부의 provider 설정을 검사하거나 수정하고, hook 명령에는 WSL 경로로 변환한 현재 Windows `lidguard.exe`를 기록합니다. 특정 distro를 선택하려면 `--distro <name>`을 전달하세요. 생략하면 `wsl.exe`가 기본 distro를 사용합니다. WSL hook 상태 검사는 이전 버전의 managed `lidguard.exe` 경로를 업데이트 필요 상태로 인식하므로 재설치하면 versioned tool path가 갱신됩니다.

## MCP 통합

```powershell
lidguard mcp-status codex
lidguard mcp-install codex
lidguard mcp-remove codex
lidguard wsl-mcp-status codex
lidguard wsl-mcp-install all --distro Ubuntu
lidguard wsl-mcp-remove claude
lidguard wsl-codex-mcp-install
lidguard wsl-claude-mcp-status --distro Ubuntu
lidguard wsl-copilot-mcp-remove
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-remove --config "<json-path>"
lidguard wsl-provider-mcp-status --config "~/.example/mcp.json"
lidguard wsl-provider-mcp-install --config "~/.example/mcp.json" --provider-name "<name>" --distro Ubuntu
lidguard wsl-provider-mcp-remove --config "~/.example/mcp.json"
```

`mcp-status`, `mcp-install`, `mcp-remove`에서 provider 위치 인자를 생략하면 LidGuard가 provider를 물어봅니다. `mcp-install`을 다시 실행하면 기존 managed LidGuard MCP server를 먼저 제거한 뒤 현재 명령으로 다시 설치하여 갱신합니다. `all`을 사용하면 LidGuard는 기본 설정 루트가 이미 존재하는 provider만 처리하고, 없는 provider는 skipped로 보고합니다.

WSL MCP 명령은 Windows 전용입니다. 선택한 또는 기본 WSL distro 안에서 provider CLI를 실행하지만, stdio server command에는 Windows `lidguard.exe`의 WSL 경로를 등록합니다. `wsl-codex-mcp-*`, `wsl-claude-mcp-*`, `wsl-copilot-mcp-*`는 `wsl-mcp-*`의 provider별 별칭입니다. `wsl-provider-mcp-*`는 WSL 쪽 JSON config path를 직접 편집합니다.

## Managed / 내부 명령

```powershell
lidguard mcp-server
lidguard provider-mcp-server --provider-name "<name>"
lidguard codex-hook
lidguard claude-hook
lidguard copilot-hook --event notification
```

이 명령들은 직접 사용하는 일상 CLI 명령이라기보다 managed integration과 stdio host를 위한 용도입니다.

## 설정과 로그

LidGuard는 기본 설정과 runtime log를 다음 위치에 저장합니다:

```text
%LOCALAPPDATA%\LidGuard
```

기본 설정 파일은 `settings.json`입니다. Runtime session execution event는 JSON Lines 형식으로 `session-execution.log`에 기록되며, 최신 500개 항목만 유지됩니다. 비활성 세션 타임아웃 만료는 `session-timeout-softlock-recorded`로 기록됩니다.

## 참고

이 프로젝트는 `net10.0`을 대상으로 하며 platform-specific source selection과 RuntimeIdentifier 매핑을 위해 `windows-x64`, `linux-x64`, `macos-arm64` 같은 RID 형태의 Visual Studio platform을 노출합니다. 릴리스 artifact는 Windows, Linux, macOS용 RID별 NativeAOT .NET tool package로 패키징됩니다. 현재 릴리스에서 Windows, systemd/logind Linux, macOS runtime platform이 구현되어 있습니다.

Linux에서는 idle sleep protection이 systemd/logind `sleep`, `idle` inhibitor를 사용합니다. Lid-close handling은 별도이며 `--change-lid-action true`는 `handle-lid-switch` inhibitor를 유지하고, `false`는 배포판의 lid-close 처리를 변경하지 않습니다.

macOS에서는 idle sleep protection이 `caffeinate`를 사용합니다. `--change-lid-action true`의 lid-close protection은 `pmset -a disablesleep 1`을 임시 적용하고 원래 `SleepDisabled` 상태를 pending backup으로 저장한 뒤, 보호 종료 또는 다음 CLI recovery path에서 복구합니다. Hibernate는 지원되는 `hibernatemode` 값을 임시로 `25`로 바꾸고 `pmset sleepnow`를 요청한 뒤, 원래 mode는 pending backup에 남겨 이후 CLI recovery에서 복구합니다. 온도는 먼저 Apple Silicon `IOHIDEventSystemClient` processor sensor에서 가능한 범위에서 읽고, 실패하면 `powermetrics --samplers smc` sample로 fallback합니다. sensor나 권한이 없으면 Emergency Hibernation poll을 건너뜁니다. Emergency Hibernation 요청에서 hibernate에 실패하면 LidGuard는 즉시 Sleep을 대체 동작으로 요청하고 두 결과를 모두 기록합니다.

Windows에서 WSL 통합은 설정 관리 기능입니다. WSL provider는 Windows LidGuard 실행 파일을 호출하며, LidGuard는 여전히 Windows host runtime을 보호합니다. distro 내부에 별도의 Linux 전원 관리를 추가하지는 않습니다. WSL 명령이 provider 작업을 실행하기 전에는 `wsl.exe`를 사용할 수 있는지와 선택한 또는 기본 distro가 간단한 명령을 실행할 수 있는지 먼저 확인합니다.

Provider MCP 통합은 동작이 보장되지 않는 보조 기능입니다. 모델이 적절한 시점에 실제로 LidGuard MCP tool을 호출해야만 동작하므로, LidGuard는 provider가 세션을 올바르게 시작, soft-lock, clear, stop한다고 보장할 수 없습니다.
