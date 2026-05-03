# 에이전트 작업은 계속, 노트북 덮개는 닫아도 됩니다.

[![NuGet](https://img.shields.io/nuget/v/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![NuGet downloads](https://img.shields.io/nuget/dt/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![Pack and Publish](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml/badge.svg)](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 [English](README.md) | 한국어

LidGuard는 내 컴퓨터에서 돌아가는 AI 코딩 도구가 작업을 끝낼 때까지 컴퓨터가 잠들지 않게 해 주는 전원 관리 도구입니다. 작업이 끝났거나 사용자 입력을 기다리는 상태가 되면 임시로 바꿨던 전원 설정을 원래대로 돌리고, 조건이 맞으면 절전 모드나 최대 절전 모드로 전환합니다.

현재 공식 검증은 Windows에서 Codex를 사용할 때를 중심으로 진행되었습니다. Linux, macOS, Claude Code, GitHub Copilot CLI 지원도 구현되어 있지만 공식 테스트 범위는 아직 제한적입니다.

## 주요 기능

- 컴퓨터가 저절로 잠드는 것뿐 아니라 노트북 덮개를 닫을 때의 동작까지 함께 다룹니다.
- Codex, Claude Code, GitHub Copilot CLI와 연결해 어떤 AI 작업이 아직 진행 중인지 추적합니다.
- 사용자 입력을 기다리는 상태, 오래 멈춰 있는 세션, 작업 종료 후 절전 또는 최대 절전 전환을 지원합니다.
- 절전 직전과 작업 완료 시점에 웹훅 알림을 보낼 수 있고, 별도 알림 서버로 휴대폰이나 다른 기기에서 알림을 받을 수 있습니다.
- Windows, systemd/logind 기반 Linux, macOS에서 동작하며 .NET 전역 도구로 설치합니다.
- 명령줄 화면 언어를 자동, 영어, 한국어 또는 다른 언어/지역 설정으로 바꿀 수 있습니다.

## 설치

### 에이전트에게 설치 맡기기

LidGuard는 로컬 AI 도구를 위한 프로그램이므로 설치도 AI 도구에게 맡길 수 있습니다. 아래 문장을 복사해서 전달하고, 명령 실행 권한을 요청하면 내용을 확인한 뒤 승인해 주세요. AI 도구가 .NET 확인, NuGet 도구 설치, Codex/Claude/Copilot 연결, 안전 설정 질문까지 진행할 수 있습니다.

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

### 다른 AI 도구 연결

AI 도구가 LidGuard를 직접 지원하지 않더라도, 사용자 지정 도구 서버를 등록할 수 있다면 별도 연결 방식으로 시도할 수 있습니다. 이 방식은 모델이 정해진 시점에 LidGuard 도구를 직접 호출해야 하므로 동작이 보장되지는 않습니다.

```powershell
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
```

서버를 등록한 뒤에는 모델이 `provider_start_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`, `provider_stop_session`을 언제 호출해야 하는지 알 수 있도록 [ProviderMcpModelPrompt.md](ProviderMcpModelPrompt.md)를 지시문으로 전달하세요.

이 연결 방식의 동작은 보장되지 않습니다. 올바른 동작은 모델이 적절한 시점에 LidGuard 도구를 호출하는지에 전적으로 달려 있으며, LidGuard의 운영체제 지원 범위를 Windows, systemd/logind 기반 Linux, macOS 밖으로 넓히지는 않습니다.

## 전체 기능

- 현재 상태 표시: 보호 중인 세션 수, 연결된 도구와 세션 식별값, 감시 중인 프로세스 번호, 사용자 입력 대기 여부, 작업 폴더, 시작 시각, 마지막 활동 시각, 덮개 상태, 보이는 모니터 수를 확인할 수 있습니다.
- AI 도구 연결: Codex, Claude Code, GitHub Copilot CLI 연결 설정을 설치, 확인, 제거하고 이벤트 기록을 볼 수 있습니다. 사용자 지정 도구 서버를 등록할 수 있는 다른 도구도 별도 연결 방식으로 시도할 수 있습니다.
- 잠들지 않게 하기: 시스템 절전 방지, 화면 절전 방지, Windows의 백그라운드 작업 유지 모드, Windows 전원 설정 화면에 표시할 절전 방지 사유 문구를 설정할 수 있습니다.
- 덮개 닫힘 처리: Windows에서는 덮개를 닫아도 아무 동작을 하지 않도록 임시 변경하고, Linux와 macOS에서는 각 운영체제 방식으로 덮개 닫힘 절전을 막습니다. 보호가 끝나면 원래 설정으로 되돌립니다.
- 작업 감시와 정리: AI 도구의 부모 프로세스를 감시하고, 이미 끝난 세션을 정리하며, 오래 활동이 없는 세션은 절전 방지를 풀 수 있습니다. 모든 세션이 끝난 뒤 LidGuard가 얼마 뒤 자동 종료될지도 정할 수 있습니다.
- 작업 완료 후 절전: 작업이 끝났거나 더 이상 컴퓨터를 깨워 둘 필요가 없을 때 절전 모드와 최대 절전 모드 중 하나를 선택할 수 있습니다. 바로 전환할지, 몇 초 기다릴지, 조건이 바뀌면 취소할지도 처리합니다.
- 절전 전 알림 소리: 절전 또는 최대 절전으로 들어가기 전에 소리를 끄거나, 기본 시스템 소리나 `.wav` 파일을 재생할 수 있습니다. 재생 중에만 임시로 음량을 1-100%로 바꾸고, 끝나면 이전 음량과 음소거 상태를 복원할 수 있습니다.
- 사용자 입력 대기 상태: AI 도구가 사용자 입력을 기다리거나 일정 시간 활동이 없으면 세션은 남겨 둔 채 절전 방지만 풀 수 있습니다. 덮개가 닫힌 상태의 권한 요청은 자동 거부 또는 자동 허용 중에서 선택할 수 있습니다. 자동 허용은 사용자가 화면을 보지 않는 동안 권한이 필요한 작업을 승인할 수 있으므로 신중히 써야 하며, 더 안전한 기본값은 자동 거부입니다.
- 고온 긴급 최대 절전: 덮개가 닫힌 상태에서 온도를 감시하다가 설정한 섭씨 온도에 도달하면 즉시 최대 절전을 요청할 수 있습니다. 온도는 낮은 값, 평균값, 높은 값 기준 중에서 고를 수 있고, 최대 절전이 실패하면 절전을 한 번 더 시도합니다.
- 웹훅과 알림: 절전 직전 알림과 작업 완료 알림을 지정한 웹 주소로 보낼 수 있습니다. 선택 사항인 `LidGuard.Notifications` 서버를 쓰면 브라우저 푸시 알림으로 받을 수 있습니다.
- 진단과 기록: 현재 덮개 상태, 보이는 모니터 수, 현재 온도, 절전 요청 기록, 연결 이벤트 기록, 세션 실행 기록, 예외 기록을 확인할 수 있습니다. 절전 기록을 몇 개까지 보관할지도 정할 수 있습니다.
- 플랫폼 설정과 언어: Linux 권한 설정 명령, macOS 권한 설정 명령, 명령줄 표시 언어 설정, 운영체제별 기본 설정/로그 저장 경로를 제공합니다.
- 배포 방식: Windows, systemd/logind 기반 Linux, macOS용 .NET 10 전역 도구로 배포됩니다.

## 문서

- [명령줄 상세 문서](LidGuard/README.md)
- [웹훅 알림 서버](LidGuard.Notifications/README.md)

## 안전 및 책임

LidGuard는 전원 관리 보조 도구이며, 어떤 환경에서도 노트북을 방치해도 안전하다는 보장이 아닙니다. 실행 중인 노트북을 가방, 파우치, 서랍처럼 열이 갇히는 공간에 넣기 전에는 반드시 사용자가 직접 장치가 식어 있고 안정적이며 안전한 상태인지 확인해야 합니다.

덮개가 닫힌 상태의 권한 요청을 자동 허용하도록 설정하는 것도 신중해야 합니다. 이 설정을 켜면 사용자가 화면을 보고 있지 않을 때도 권한이 필요한 AI 작업이 계속 진행될 수 있습니다.

사용자 입력 대기 감지, AI 도구 연결, 프로세스 감시, 운영체제 동작, 명령줄 동작, 온도 센서, 권한, 펌웨어, 전원 정책은 모두 예상과 다르게 실패하거나 변경될 수 있습니다. 고온 긴급 최대 절전과 절전 흐름은 최선의 안전장치일 뿐이며, 사용자가 직접 장치 상태를 확인하는 일을 대신하지 않습니다.

Codex 연결 정리에는 별도 제한이 있습니다. Codex 앱은 같은 작업 폴더에 `process=none` 세션을 남길 수 있습니다. LidGuard는 확인된 프로세스 또는 바로 위 부모 프로세스가 운영체제별로 허용된 셸인 명령줄 세션에 대해서만 작업 폴더 기준 정리를 사용하며, 이 정리 경로는 `process=none` Codex 세션을 제거하지 않습니다.

장치 상태와 발열 위험을 확인할 책임은 사용자에게 있습니다. 이를 지키지 않아 발생한 기기 손상, 데이터 손실, 재산 손실 또는 기타 손해는 사용자 책임입니다.

## 기여

기여는 환영합니다. 변경 범위는 명확하게 유지하고, 전원 제어와 관련된 동작은 신중히 검증해 주세요. 비밀 값이나 개인 장치 설정은 커밋하지 마세요.

## 라이선스

LidGuard는 [MIT 라이선스](LICENSE.txt)로 배포됩니다.

## 제작자

[이호원 (airtaxi)](https://github.com/airtaxi)이 만들었습니다.

OpenAI Codex의 도움을 받아 제작되었습니다.
