# 에이전트는 계속 달리고, 노트북 덮개는 닫아도 됩니다.

[![NuGet](https://img.shields.io/nuget/v/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![NuGet downloads](https://img.shields.io/nuget/dt/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![Pack and Publish](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml/badge.svg)](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 [English](README.md) | 한국어

LidGuard는 로컬 AI 코딩 에이전트 세션이 아직 작업 중일 때 시스템을 깨어 있게 유지하고, 세션이 끝나거나 절전 가능한 SoftLock 상태가 되면 운영체제의 전원 정책으로 안전하게 되돌립니다. 에이전트를 실행하고, 노트북 덮개를 닫고, 작업이 끝나면 LidGuard가 Sleep 또는 Hibernate 흐름으로 복귀시키는 사용 흐름을 목표로 합니다.

LidGuard는 현재 Windows의 Codex 환경에서 공식 검증되었습니다. Linux, macOS, Claude Code, GitHub Copilot CLI 지원도 구현되어 있지만 공식 테스트 범위는 아직 제한적입니다.

## 주요 기능

- 일반 유휴 절전 방지뿐 아니라 노트북 덮개 닫힘 정책까지 다룹니다.
- Provider hook을 통해 Codex, Claude Code, GitHub Copilot CLI 세션을 추적합니다.
- SoftLock, 비활성 세션 타임아웃, 세션 종료 후 자동 Sleep 또는 Hibernate를 지원합니다.
- `PreSuspend` 및 `PostSessionEnd` 웹훅을 보내며, 선택적으로 `LidGuard.Notifications` Web Push 서버와 연동할 수 있습니다.
- Windows, systemd/logind Linux, macOS 전원 제어를 구현했고 NativeAOT .NET tool로 패키징됩니다.
- 사람에게 보이는 CLI UI 언어를 `auto`, `en`, `ko` 또는 다른 culture 이름으로 선택할 수 있습니다.

## 설치

```powershell
dotnet tool install --global lidguard
```

```powershell
lidguard help
lidguard hook-install --provider codex
lidguard settings --ui-culture ko
```

## 문서

- [CLI 상세 문서](LidGuard/README.md)
- [웹훅 알림 서버](LidGuard.Notifications/README.md)

## 안전 및 책임

LidGuard는 전원 관리 보조 도구이며, 어떤 환경에서도 노트북을 방치해도 안전하다는 보장이 아닙니다. 실행 중인 노트북을 가방, 파우치, 서랍처럼 열이 갇히는 공간에 넣기 전에는 반드시 사용자가 직접 장치가 식어 있고 안정적이며 안전한 상태인지 확인해야 합니다.

SoftLock 감지, provider hook, 프로세스 감시, 운영체제 동작, CLI 동작, 온도 센서, 권한, 펌웨어, 전원 정책은 모두 예상과 다르게 실패하거나 변경될 수 있습니다. Emergency Hibernation과 절전 흐름은 최선의 안전장치일 뿐이며, 사용자가 직접 장치 상태를 확인하는 일을 대신하지 않습니다.

Codex hook 정리에는 별도 제한이 있습니다. Codex App은 같은 작업 디렉터리에 `process=none` 세션을 남길 수 있습니다. LidGuard는 해석된 프로세스 또는 직접 부모 프로세스가 플랫폼에서 승인된 셸인 shell-hosted CLI 세션에 대해서만 Codex 작업 디렉터리 watchdog fallback을 사용하며, 이 정리 경로는 `process=none` Codex 세션을 제거하지 않습니다.

장치 상태와 발열 위험을 확인할 책임은 사용자에게 있습니다. 이를 지키지 않아 발생한 기기 손상, 데이터 손실, 재산 손실 또는 기타 손해는 사용자 책임입니다.

## 기여

Pull request는 환영합니다. 변경 범위는 명확하게 유지하고, 전원 제어와 관련된 동작은 신중히 검증해 주세요. 비밀 값이나 로컬 장치 설정은 커밋하지 마세요.

## 라이선스

LidGuard는 [MIT License](LICENSE.txt)로 배포됩니다.

## 제작자

[이호원 (airtaxi)](https://github.com/airtaxi)이 만들었습니다.

OpenAI Codex의 도움을 받아 제작되었습니다.
