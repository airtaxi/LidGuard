# LidGuard CLI

🌐 [English](README.md)

LidGuard는 오래 실행되는 로컬 AI 코딩 에이전트 세션을 위한 명령줄 도구입니다. 현재 Windows 보호 기능이 구현되어 있으며, macOS와 Linux 지원은 예정되어 있고 지금은 지원 예정 메시지를 출력한 뒤 성공 코드로 종료합니다.

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
lidguard cleanup-orphans
```

`start`와 `stop`에는 `--provider`가 필요합니다. `--session`은 선택 사항이며, 생략하면 LidGuard가 provider 표시 이름과 정규화된 작업 디렉터리에서 fallback 세션 식별자를 파생합니다.

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
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard remove-pre-suspend-webhook
lidguard preview-system-sound --name Asterisk
lidguard preview-current-sound
```

옵션 없이 `settings`를 실행하면 대화형 설정 편집을 시작합니다. 세션 타임아웃 기본값은 12분입니다. 비활성 세션 타임아웃 soft-lock을 끄려면 `--session-timeout-minutes off`를 전달하고, 마지막 활동 이후 지정한 분 수가 지나면 세션을 soft-lock 상태로 전환하려면 1 이상의 값을 전달합니다. Server runtime cleanup 지연 시간 기본값은 모든 세션이 사라지고 pending cleanup이 끝난 뒤 10분입니다. 즉시 종료하려면 `--server-runtime-cleanup-delay-minutes off`를 전달합니다. Emergency Hibernation 온도 모드 기본값은 `Average`이며 `Low`, `Average`, `High`로 바꿀 수 있습니다. 선택 사항인 post-stop suspend sound volume override는 `off` 또는 1부터 100까지의 percent 값을 받습니다. 켜져 있으면 소리가 재생되는 동안 기본 출력 장치의 master volume을 임시로 설정한 뒤 이전 volume과 mute 상태를 복원합니다. `preview-system-sound`와 `preview-current-sound`는 저장된 override 설정을 사용하고 재생이 끝날 때까지 기다립니다. post-stop suspend sound가 설정되어 있지 않으면 `preview-current-sound`가 설정 안내를 출력합니다. 설정된 webhook URL을 지우려면 `remove-pre-suspend-webhook`을 사용합니다.

## 진단

```powershell
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature --temperature-mode high
```

`current-lid-state`는 LidGuard가 덮개 닫힘 정책 판단에 사용하는 동일한 Windows lid-state source를 통해 현재 덮개 스위치 상태를 `Open`, `Closed`, `Unknown`으로 출력합니다.

`current-monitor-count`는 LidGuard가 덮개 닫힘 절전 정책 판단에 사용하는 동일한 기본 Windows 모니터 visibility check로 현재 visible display monitor count를 출력합니다. LidGuard는 `SM_CMONITORS`에서 시작한 뒤 Windows WMI가 보고하는 inactive monitor connection을 제외합니다. 내부 노트북 패널 connection은 최종 suspend eligibility check에서만 제외됩니다.

`current-temperature`는 선택한 집계 모드로 현재 인식된 system thermal-zone 온도를 Celsius로 출력합니다. `--temperature-mode default|low|average|high`를 사용하면 저장된 설정을 재사용하거나 한 번의 명령에 대해서만 override할 수 있습니다. 설정 파일이 아직 없을 때 `default`는 LidGuard의 `Average` headless runtime 기본값으로 fallback합니다.

## Hook 통합

```powershell
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 20
lidguard codex-hooks
lidguard claude-hooks
lidguard copilot-hooks
```

`hook-status`, `hook-install`, `hook-remove`, `hook-events`에서 `--provider`를 생략하면 LidGuard가 provider를 물어봅니다. `--provider all`을 사용하면 LidGuard는 기본 설정 루트가 이미 존재하는 provider만 처리하고, 없는 provider는 skipped로 보고합니다.

## MCP 통합

```powershell
lidguard mcp-status --provider codex
lidguard mcp-install --provider codex
lidguard mcp-remove --provider codex
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-remove --config "<json-path>"
```

`mcp-status`, `mcp-install`, `mcp-remove`에서 `--provider`를 생략하면 LidGuard가 provider를 물어봅니다. `mcp-install`을 다시 실행하면 기존 managed LidGuard MCP server를 먼저 제거한 뒤 현재 명령으로 다시 설치하여 갱신합니다. `--provider all`을 사용하면 LidGuard는 기본 설정 루트가 이미 존재하는 provider만 처리하고, 없는 provider는 skipped로 보고합니다.

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

이 패키지는 `net10.0`을 대상으로 하며 Windows, Linux, macOS용 RID별 NativeAOT .NET tool package로 패키징됩니다. 현재 릴리스에서 구현된 runtime platform은 Windows뿐입니다.

Provider MCP 통합은 best-effort 방식입니다. 이 통합은 모델이 적절한 시점에 실제로 LidGuard MCP tool을 호출하는지에 의존하므로, LidGuard는 provider가 세션을 올바르게 시작, soft-lock, clear, stop한다고 보장할 수 없습니다.
