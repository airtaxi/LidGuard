# 문서 대비 코드 누락 체크

비교 대상: `AGENTS.md`, `AGENTS.ko.md`, `LidGuard/README.md`, `CodexSoftLockMemo.ko.md`, `ClaudeSoftLockMemo.ko.md`, `GithubCopilotSoftLockMemo.ko.md`, `ProviderMcpModelPrompt*.md`

## 코드에 빠진 부분

1. Runtime idle shutdown lifecycle 정책

- 문서 근거: `AGENTS.md`의 `Missing Work`에 runtime idle shutdown lifecycle 정책 추가가 남아 있음.
- 코드 확인: `LidGuardCommandLineApplication.RunServerAsync`는 `LidGuardPipeServer.RunAsync()`를 취소 토큰으로 계속 실행하며, 활성 세션이 없어졌을 때 일정 시간 뒤 runtime을 종료하는 idle timer 또는 shutdown 조건이 보이지 않음.
- 영향: runtime이 마지막 세션 종료 뒤에도 프로세스로 계속 남을 수 있음.

2. Codex 직접 soft-lock 감지/대응

- 문서 근거: `CodexSoftLockMemo.ko.md`는 `RequestUserInput`, `RequestPermissions`, MCP `ElicitationRequest` 등 `PermissionRequest` hook 밖의 대기 상태를 직접 커버하기 어렵다고 정리하고, `AGENTS.md`는 Codex가 notification 또는 machine-readable pending-state hook surface를 제공할 때만 direct Codex soft-lock 지원을 추가한다고 적음.
- 코드 확인: Codex는 `UserPromptSubmit`, `Stop`, `PermissionRequest` 중심 hook 처리와 transcript activity 기반 soft-lock 해제 보조 경로는 있으나, Codex pending-state/app-server/notification 기반의 직접 soft-lock 감지 경로는 없음.
- 영향: Codex가 사용자 입력 대기 상태에 들어갔을 때 LidGuard가 이를 직접 soft-lock으로 전환하는 기능은 아직 없음. 단, 문서상 조건부 구현 항목이라 현재 provider 표면이 없으면 의도된 보류 상태로 보임.

3. 테스트 코드 구현

- 문서 근거: `AGENTS.md`의 `Missing Work`에는 최신 provider/Windows 환경 검증 항목들이 남아 있음.
- 확인 내용: 해당 검증은 수동 테스트로 이미 완료된 상태이며, 현재 코드 기준으로 남은 것은 테스트 코드/자동화 구현임.
- 코드 확인: 저장소에 별도 테스트 프로젝트나 검증 자동화 스크립트가 없음.
- 영향: 회귀 검증을 자동으로 반복할 수 없으므로, 향후 변경 시 같은 검증을 수동으로 다시 수행해야 함.
