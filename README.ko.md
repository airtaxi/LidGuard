# 에이전트 작업은 계속, 노트북 덮개는 닫아도 됩니다.

[![NuGet](https://img.shields.io/nuget/v/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![NuGet downloads](https://img.shields.io/nuget/dt/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![Pack and Publish](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml/badge.svg)](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 [English](README.md) | 한국어

LidGuard는 노트북 덮개를 닫은 뒤에도 오래 걸리는 로컬 AI 코딩 에이전트 작업이 계속되도록 돕는 전원 관리 도구입니다. Codex, Claude Code, GitHub Copilot CLI, OpenCode 같은 에이전트 세션을 추적하고 보호 중인 작업이 진행되는 동안 일반 절전과 덮개 닫힘 절전을 임시로 막은 뒤, 작업이 끝나거나 절전 진입 가능 상태가 되면 원래 운영체제 전원 정책을 복원합니다.

대부분의 절전 방지 도구는 프로세스, 타이머, 또는 컴퓨터 전체를 기준으로 동작합니다. LidGuard는 AI 코딩 에이전트 세션을 기준으로 보호합니다. 에이전트 작업을 시작하고 장치가 계속 켜져 있어도 안전한 상황에서 덮개를 닫고 일이 끝나면 LidGuard가 보호를 해제하거나 절전/최대 절전으로 전환하게 둘 수 있습니다.

## 데모

<img src=".github/Assets/Demo.gif" alt="" width="720">

## 주요 기능

- Codex, Claude Code, GitHub Copilot CLI, OpenCode 같은 로컬 AI 코딩 에이전트 작업을 인식해 절전 진입을 막습니다.
- 보호 중인 에이전트 작업이 노트북 덮개를 닫은 뒤에도 계속될 수 있도록 덮개 닫힘 동작을 임시로 바꾸고 작업 종료 후 원래 OS 전원 정책으로 복원합니다.
- Windows에서 WSL 안의 AI 도구 설정을 LidGuard가 연결해 줍니다. WSL에 LidGuard를 따로 설치하지 않아도 Windows 쪽 `lidguard.exe`를 그대로 사용할 수 있습니다.
- 보호 중인 작업이 끝난 뒤 설정에 따라 절전 모드 또는 최대 절전 모드로 전환해 불필요한 배터리 소모를 줄이는 데 도움을 줍니다.
- 지원되는 provider에서는 덮개를 닫은 상태에서 작업이 끝나려 할 때 절전 전 답장 알림을 보내고 답장이 오면 작업을 이어갈 수 있습니다.
- Windows, systemd/logind 기반 Linux, macOS 전원 제어를 지원합니다.
- SoftLock, 비활성 시간 제한, 절전 진입 전 Webhook, 진단 기능, 긴급 최대 절전 모드 같은 안전 장치를 제공합니다.

## 왜 LidGuard인가요?

기존 절전 방지 도구는 컴퓨터가 잠들지 않게 만드는 데 초점이 있지만 노트북 덮개를 닫고 에이전트를 계속 돌리고 싶은 순간을 중심으로 설계되지는 않았습니다. LidGuard는 그보다 좁고 구체적인 문제를 다룹니다. 로컬 AI 코딩 에이전트가 일을 시작하면 보호 상태를 켜고 작업이 끝났거나 절전 진입 가능 상태가 되면 보호 상태를 해제합니다.

덮개 닫기 동작을 임시로 바꿨다면 원래 전원 설정으로 복원하고 설정에 따라 작업 종료 후 절전 모드 또는 최대 절전 모드로 들어갈 수 있습니다. 목표는 긴 AI 작업을 유지하되, 작업이 끝난 뒤 노트북이 불필요하게 깨어 있는 시간을 줄이는 것입니다. LidGuard는 특정 운영체제 하나에만 맞춘 우회책이 아니라 Windows, systemd/logind 기반 Linux, macOS를 대상으로 합니다.

## 설치

### 에이전트에게 설치 맡기기

LidGuard는 로컬 AI 도구를 위한 프로그램이므로 설치도 AI 도구에게 맡길 수 있습니다. 아래 문장을 복사해서 전달하고 명령 실행 권한을 요청하면 내용을 확인한 뒤 승인해 주세요. AI 도구가 .NET 확인, NuGet 도구 설치, Codex/Claude/Copilot/OpenCode 연결, 안전 설정 질문까지 진행할 수 있습니다.

대부분의 에이전트용:

```text
Read https://raw.githubusercontent.com/airtaxi/LidGuard/master/.github/agent-install.md and install LidGuard for this machine. You may run the commands needed for installation after explaining them, and you should ask me before any administrator, sudo, password, provider hook, MCP, or safety-related settings step.
```

Codex에서는 대화형 질문 흐름을 사용할 수 있게 `/plan`으로 시작하세요:

```text
/plan
Read https://raw.githubusercontent.com/airtaxi/LidGuard/master/.github/agent-install.md and install LidGuard for this machine. You may run the commands needed for installation after explaining them, and you should ask me before any administrator, sudo, password, provider hook, MCP, or safety-related settings step.
```

에이전트용 설치 지시문은 [.github/agent-install.md](.github/agent-install.md)에 있습니다.

연결 설정을 설치해도 현재 대화에는 바로 반영되지 않을 수 있습니다. 제대로 동작하는지 확인하려면 새 세션이나 새 대화에서 시작해 주세요.

### 수동 설치

```powershell
dotnet tool install --global lidguard
```

```powershell
lidguard help
lidguard hook-install --provider all
lidguard mcp-install all
```

### 웹훅 알림 서버

LidGuard는 절전 직전, 세션 종료 후, 절전 전 답장 요청 같은 이벤트를 선택 사항인 알림 서버로 보낼 수 있습니다. `LidGuard.Notifications`는 이 웹훅을 받아 구독된 브라우저로 Web Push 알림을 보내고 답장이 필요한 경우 작은 대시보드에서 바로 응답할 수 있게 해줍니다.

설정, 보안 주의 사항, 배포 방법은 [LidGuard.Notifications README](LidGuard.Notifications/README.ko.md)를 확인하세요.

### 절전 전에 한 번 더 묻기

덮개를 닫아 둔 세션이 끝나서 LidGuard가 곧 절전으로 넘기려는 순간, 바로 잠들기 전에 짧게 멈추고 마지막 지시를 받을 수 있습니다. 알림 대시보드에서 답장하면 LidGuard는 흐름을 바로 종료하지 않고 세션을 계속 살려 둔 뒤, 그 답장을 AI 도구에 다시 전달합니다.

오래 걸리는 코딩 작업이나 에이전트 작업에서는 "끝났습니다"가 가끔 "거의 끝났는데 사용자의 판단이 필요합니다"에 더 가까울 때가 있습니다. 답장 대기 시간은 짧게 유지하고 필요하면 대시보드에서 연장하거나, 기다리지 않아도 될 때는 대기를 취소해 평소처럼 절전 흐름을 이어갈 수 있습니다.

연속 답장을 켜 두면 한 번의 확인에서 끝나지 않고 필요한 때마다 다음 지시를 이어 보낼 수 있습니다. 노트북은 가방 속에 닫아 둔 채로, 굳이 다시 꺼내 열지 않아도 휴대폰이나 브라우저에서 후속 작업을 계속 이어갈 수 있습니다.

OpenCode는 전역 플러그인으로 연결됩니다. OpenCode가 세션에 더 할 일이 없다고 알려오면 LidGuard가 답장을 기다릴 수 있습니다. 답장하면 같은 OpenCode 채팅에 그 내용을 다시 보내 작업을 이어갑니다. OpenCode는 마무리 단계를 LidGuard가 붙잡아 둘 수 없으므로, 답장이 도착할 때 OpenCode가 아직 실행 중이고 해당 채팅이 남아 있어야 합니다.

### Windows에서 WSL과 함께 쓰기

Windows에서 WSL로 Codex, Claude, Copilot, OpenCode 같은 AI 도구를 쓰는 경우, LidGuard가 WSL 안의 설정 파일을 Windows용 `lidguard.exe`로 연결해 줄 수 있습니다. WSL 안에 LidGuard를 다시 설치할 필요는 없습니다. LidGuard는 현재 Windows 실행 파일 경로를 WSL에서 읽을 수 있는 경로로 바꿔 설정합니다.

```powershell
lidguard wsl-hook-install --provider all
lidguard wsl-mcp-install all
lidguard wsl-provider-mcp-install --config "~/.example/mcp.json" --provider-name "ExampleProvider"
```

특정 WSL 배포판을 지정하려면 `--distro <name>`을 전달하세요. 생략하면 `wsl.exe`가 기본 배포판을 사용합니다.

### 다른 AI 도구 연결

AI 도구가 LidGuard를 직접 지원하지 않더라도, 사용자 지정 도구 서버를 등록할 수 있다면 별도 연결 방식으로 시도할 수 있습니다. 이 방식은 모델이 정해진 시점에 LidGuard 도구를 직접 호출해야 하므로 동작이 보장되지는 않습니다.

```powershell
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
```

서버를 등록한 뒤에는 모델이 `provider_start_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`, `provider_stop_session`을 언제 호출해야 하는지 알 수 있도록 [ProviderMcpModelPrompt.md](ProviderMcpModelPrompt.md)를 지시문으로 전달하세요.

이 연결 방식은 모델이 제때 LidGuard 도구를 호출해야 하므로 항상 보장되지는 않습니다. 또한 LidGuard가 새 운영체제를 지원하게 되는 것은 아닙니다. 실제 전원 제어는 여전히 Windows, systemd/logind 기반 Linux, macOS에서만 지원됩니다. 절전 전 답장 알림은 이 별도 연결 방식에서는 사용할 수 없습니다.

## 현재 지원 상태

현재 LidGuard는 Windows의 Codex 환경에서 공식적으로 테스트되었습니다. Linux, macOS, Claude Code, GitHub Copilot CLI, OpenCode 지원은 구현되어 있지만 더 다양한 실제 사용 환경에서의 검증은 아직 진행 중입니다. 사용 사례 공유와 pull request를 환영합니다.

## 전체 기능

- 현재 상태 표시: 보호 중인 세션 수, 연결된 도구와 세션 식별값, 감시 중인 프로세스 번호, 사용자 입력 대기 여부, 작업 폴더, 시작 시각, 마지막 활동 시각, 덮개 상태, 보이는 모니터 수, 실시간 터미널 상태 화면을 확인할 수 있습니다.
- AI 도구 연결: Codex, Claude Code, GitHub Copilot CLI, OpenCode 연결 설정을 설치, 확인, 제거하고 이벤트 기록을 볼 수 있습니다. Windows에서는 WSL 안에서 쓰는 AI 도구 설정도 Windows용 LidGuard로 연결할 수 있으며 사용자 지정 도구 서버를 등록할 수 있는 다른 도구도 별도 연결 방식으로 시도할 수 있습니다.
- 잠들지 않게 하기: 시스템 절전 방지, 화면 절전 방지, Windows의 백그라운드 작업 유지 모드, Windows 전원 설정 화면에 표시할 절전 방지 사유 문구를 설정할 수 있습니다.
- 덮개 닫힘 처리: Windows에서는 덮개를 닫아도 아무 동작을 하지 않도록 임시 변경하고 Linux와 macOS에서는 각 운영체제 방식으로 덮개 닫힘 절전을 막습니다. 보호가 끝나면 원래 설정으로 되돌립니다.
- 작업 감시와 정리: AI 도구의 부모 프로세스를 감시하고 이미 끝난 세션을 정리하며 오래 활동이 없는 세션은 절전 방지를 풀 수 있습니다. 모든 세션이 끝난 뒤 LidGuard가 얼마 뒤 자동 종료될지도 정할 수 있습니다.
- 작업 완료 후 절전: 작업이 끝났거나 더 이상 컴퓨터를 깨워 둘 필요가 없을 때 절전 모드와 최대 절전 모드 중 하나를 선택할 수 있습니다. 바로 전환할지, 몇 초 기다릴지, 조건이 바뀌면 취소할지도 처리합니다.
- 절전 전 답장 알림: 지원되는 도구에서 덮개를 닫은 상태로 작업이 끝나려 할 때 "절전 전에 한 번 물어보기" 알림을 보낼 수 있습니다. 답장이 오면 작업을 이어가고 기본 설정에서는 이어진 작업이 나중에 다시 끝나려 할 때 한 번 더 물어봅니다.
- 절전 전 알림 소리: 절전 또는 최대 절전으로 들어가기 전에 소리를 끄거나, 기본 시스템 소리나 `.wav` 파일을 재생할 수 있습니다. 재생 중에만 임시로 음량을 1-100%로 바꾸고 끝나면 이전 음량과 음소거 상태를 복원할 수 있습니다.
- 사용자 입력 대기 상태: AI 도구가 사용자 입력을 기다리거나 일정 시간 활동이 없으면 세션은 남겨 둔 채 절전 방지만 풀 수 있습니다. 덮개가 닫힌 상태의 PermissionRequest는 `deny`, `allow`, `ask` 중에서 선택할 수 있습니다. `ask`는 세션을 soft-lock하고 provider의 일반 권한 요청 흐름을 유지하며 `allow`는 사용자가 화면을 보지 않는 동안 권한이 필요한 작업을 승인할 수 있습니다. 더 안전한 기본값은 `deny`입니다.
- 고온 긴급 최대 절전: 덮개가 닫힌 상태에서 온도를 감시하다가 설정한 섭씨 온도에 도달하면 즉시 최대 절전을 요청할 수 있습니다. 온도는 낮은 값, 평균값, 높은 값 기준 중에서 고를 수 있고 최대 절전이 실패하면 절전을 한 번 더 시도합니다.
- 웹훅과 알림: 절전 직전 알림, 작업 완료 알림, 절전 전 답장 알림을 지정한 웹 주소로 보낼 수 있습니다. LidGuard는 답장이 왔는지도 주기적으로 확인합니다. 선택 사항인 `LidGuard.Notifications` 서버를 쓰면 브라우저 푸시 알림을 받고 대시보드에서 답장할 수 있습니다.
- 진단과 기록: 현재 덮개 상태, 보이는 모니터 수, 현재 온도, 실시간 상태 화면, 절전 요청 기록, 연결 이벤트 기록, 세션 실행 기록, 예외 기록을 확인할 수 있습니다. 절전 기록을 몇 개까지 보관할지도 정할 수 있습니다.
- 플랫폼 설정과 언어: Linux 권한 설정 명령, macOS 권한 설정 명령, 명령줄 표시 언어 설정, 운영체제별 기본 설정/로그 저장 경로를 제공합니다.
- 배포 방식: Windows, systemd/logind 기반 Linux, macOS용 .NET 10 전역 도구로 배포됩니다.

## 문서

- [명령줄 상세 문서](LidGuard/README.ko.md)
- [웹훅 알림 서버](LidGuard.Notifications/README.ko.md)

## 안전 및 책임

LidGuard는 전원 관리 보조 도구이며 어떤 환경에서도 노트북을 방치해도 안전하다는 보장이 아닙니다. 실행 중인 노트북을 가방, 파우치, 서랍처럼 열이 갇히는 공간에 넣기 전에는 반드시 사용자가 직접 장치가 식어 있고 안정적이며 안전한 상태인지 확인해야 합니다.

덮개가 닫힌 상태의 권한 요청을 자동 허용하도록 설정하는 것도 신중해야 합니다. 이 설정을 켜면 사용자가 화면을 보고 있지 않을 때도 권한이 필요한 AI 작업이 계속 진행될 수 있습니다. `ask`로 설정했더라도 로컬 transcript를 읽을 수 없는 cloud 또는 desktop provider 환경에서는 관측 가능한 activity/tool hook, 세션 갱신, 정리 경로로만 soft-lock이 풀릴 수 있습니다.

사용자 입력 대기 감지, AI 도구 연결, 프로세스 감시, 운영체제 동작, 명령줄 동작, 온도 센서, 권한, 펌웨어, 전원 정책은 모두 예상과 다르게 실패하거나 변경될 수 있습니다. 고온 긴급 최대 절전과 절전 흐름은 최선의 안전장치일 뿐이며 사용자가 직접 장치 상태를 확인하는 일을 대신하지 않습니다.

덮개를 닫은 뒤 절전 전 답장 알림을 켜면, LidGuard가 답장 대기 시간 동안 노트북을 의도적으로 계속 깨워 둡니다. 이 시간만큼 열이 더 날 수 있으므로 대기 시간을 짧게 하고 장치 상태를 직접 확인해야 합니다.

Managed hook 정리는 hook 프로세스의 부모 프로세스를 따라가며 감시할 대상 프로세스를 찾습니다. 가능하면 Codex App `app-server`, Codex CLI, Claude Code, GitHub Copilot CLI, OpenCode 프로세스를 감시합니다. 작업 폴더는 상태 표시, 로그, transcript fallback, webhook 정보에만 사용하며 감시할 프로세스를 찾는 데 사용하지 않습니다. 감시하던 부모 프로세스가 종료되면 LidGuard는 이를 provider가 정상 종료를 보고한 것이 아니라 취소 정리로 처리합니다.

장치 상태와 발열 위험을 확인할 책임은 사용자에게 있습니다. 이를 지키지 않아 발생한 기기 손상, 데이터 손실, 재산 손실 또는 기타 손해는 사용자 책임입니다.

## 현지화

한국어와 영어는 개발자가 직접 검수하고 관리할 수 있지만 다른 언어는 검수가 불가능하여 기계 번역에 의존하고 있습니다.

새로운 언어 추가 제안이나 번역 수정 제안은 Issue로 문의해 주시기 바랍니다. 해당 언어가 모국어여서 직접 검수가 가능하신 경우 Pull Request(PR)도 언제나 환영합니다!

## 기여

기여는 환영합니다. 변경 범위는 명확하게 유지하고 전원 제어와 관련된 동작은 신중히 검증해 주세요. 비밀 값이나 개인 장치 설정은 커밋하지 마세요.

## 라이선스

LidGuard는 [MIT 라이선스](LICENSE.txt)로 배포됩니다.

## 제작자

[이호원 (airtaxi)](https://github.com/airtaxi)이 만들었습니다.

OpenAI Codex의 도움을 받아 제작되었습니다.