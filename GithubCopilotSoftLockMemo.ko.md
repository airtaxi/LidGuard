# GitHub Copilot 소프트락 분석 메모

분석 대상 문서와 소스:

- GitHub Copilot CLI command reference
  - `https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference`
- Hooks configuration
  - `https://docs.github.com/en/copilot/reference/hooks-configuration`
- Using hooks with GitHub Copilot CLI
  - `https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks`
- Working with hooks
  - `https://docs.github.com/en/copilot/how-tos/copilot-sdk/use-copilot-sdk/working-with-hooks`
- GitHub Copilot SDK source
  - upstream: `https://github.com/github/copilot-sdk`
  - 확인 commit: `02ff69bd6c16b293736b40f4ca89613082143939`
  - 확인 파일:
    - `docs/features/hooks.md`
    - `docs/hooks/session-lifecycle.md`
    - `docs/troubleshooting/compatibility.md`
    - `nodejs/src/types.ts`
    - `test/scenarios/callbacks/user-input/README.md`
    - `nodejs/test/e2e/ask_user.test.ts`
    - `nodejs/test/e2e/ui_elicitation.test.ts`
- 비교 문서:
  - `E:\Repos\LidGuard\ClaudeSoftLockMemo.ko.md`
- 확인 시점: `2026-04-29`

이 문서는 원래 GHCP hook 지원 구현 전에 보는 참고 문서였고, 2026-04-29 기준으로 v1 softlock orchestration 구현 반영 상태까지 함께 기록한다.

핵심 목적은 다음 세 가지다.

1. GHCP CLI hook만으로 지금 당장 직접 guard 가능한 케이스를 분리한다.
2. 현재 CLI hook 표면만으로는 직접 guard하기 어려운 limitation을 분리한다.
3. Claude 메모와 비교해 GHCP에서 더 쉬운 점과 더 어려운 점을 구현 관점으로 정리한다.

## 한 줄 결론

GHCP는 v1에서 Codex/Claude와 같은 turn 모델로 가는 것이 가장 자연스럽다.

- 시작: `userPromptSubmitted`
- 종료: `agentStop`
- 1차 closed-lid guard: `permissionRequest`
- 추가 guard: `preToolUse`의 `ask_user`
- 감지 전용 신호: `notification(permission_prompt|elicitation_dialog)`
- 보조 telemetry: `sessionStart`, `sessionEnd`, `errorOccurred`

즉 GHCP는 `sessionEnd` 중심 모델보다 `userPromptSubmitted` -> `agentStop` 모델이 맞고, 구현 전 문서/상태 점검에서 non-LidGuard `agentStop` hook 존재를 강하게 경고하는 것이 핵심이다.

2026-04-29 기준 LidGuard 저장소에는 이 v1 모델이 구현 반영되어 있다.

## 문제 정의

LidGuard의 일반 모델은 다음과 같다.

1. provider hook에서 세션 또는 turn 시작을 감지한다.
2. Windows 절전 방지와 lid close 정책 변경을 유지한다.
3. turn 또는 세션 종료를 감지한다.
4. 마지막 보호 구간이 끝나면 power request를 해제하고 lid action을 복구한다.

GHCP에서 소프트락은 다음 상태를 뜻한다.

- GHCP 프로세스와 세션은 살아 있다.
- 현재 turn은 아직 끝나지 않았다.
- 사용자가 `permission_prompt`, `ask_user`, `elicitation_dialog` 같은 응답을 해야 해서 모델 진행이 멈췄다.
- lid가 닫혀 있으면 사용자는 응답할 수 없다.
- `agentStop`이 아직 오지 않으므로 LidGuard가 계속 절전 방지를 유지할 수 있다.

중요한 점은 GHCP에서 session lifetime과 turn lifetime이 분리된다는 것이다.

- `sessionStart` / `sessionEnd`는 세션 수명에 가깝다.
- `agentStop`은 메인 agent turn 종료에 가깝다.
- 공식 CLI의 `/keep-alive on|busy`도 이 구분이 실제 제품 개념임을 보여준다.

따라서 GHCP에서 LidGuard의 keep-awake 기준은 세션 수명보다 turn 수명에 두는 편이 맞다.

## 권장 운영 모델

이 문서 기준의 권장 v1 모델은 다음과 같다.

### 시작 기준

- `userPromptSubmitted`

이유:

- 사용자가 실제로 turn을 시작한 시점이다.
- `sessionStart`보다 더 보수적이고, idle chat 세션을 보호 범위에서 뺄 수 있다.
- Codex/Claude와도 운영 모델을 맞추기 쉽다.

### 종료 기준

- `agentStop`

이유:

- 공식 command reference가 `agentStop`을 "main agent finishes a turn"으로 설명한다.
- `permissionRequest`, `ask_user`, `elicitation_dialog` 대기 중에는 아직 `agentStop`이 오지 않는다.
- 따라서 turn이 진짜 끝났을 때 keep-awake를 내리는 기준으로 가장 자연스럽다.

### 보조 기준

- `permissionRequest`
- `preToolUse(toolName=ask_user)`
- `notification(permission_prompt|elicitation_dialog)`

이들은 종료 기준이 아니라 "turn은 아직 안 끝났는데 사용자를 기다리고 있다"는 중간 상태 신호다.

### 보조 telemetry

- `sessionStart`
- `sessionEnd`
- `errorOccurred`

이들은 keep-awake의 primary signal이 아니라 다음 용도로만 제한하는 편이 좋다.

- 진단 로그
- crash/orphan 추적
- 후속 cleanup telemetry

## 지금 당장 guard 가능한 케이스

이 섹션은 GHCP CLI hook만으로 바로 구현 가능한 영역만 적는다.

### 1. `permissionRequest` [구현됨]

이 경로는 GHCP CLI에서 가장 강한 closed-lid guard다.

공식 문서상 `permissionRequest`는:

- rule-based allow/deny보다 뒤지만
- user permission dialog를 띄우기 전
- programmatic allow/deny를 넣을 수 있는 지점이다.

지금 당장 guard 가능한 대표 상황:

- shell 실행 승인
- edit/create 같은 쓰기 승인
- URL 접근 승인
- 일반 permission service를 거치는 tool 승인

권장 동작:

- lid open: empty output으로 no-op
- lid closed: 설정에 따라 structured allow/deny
- 강한 정책이 필요하면 `interrupt: true`까지 검토

구현 결론:

- GHCP에서도 `PermissionRequest` 계열은 Claude 메모와 비슷하게 1차 guard 대상이다.
- 다만 GHCP에서는 hook 이름이 `permissionRequest`이고, Claude처럼 `AskUserQuestionTool`이 이 경로로 흡수된다고 문서화된 것은 아니다.

### 2. `ask_user` via `preToolUse` [구현됨]

이 점이 GHCP CLI의 가장 큰 강점이다.

공식 tool names for hook matching에 `ask_user`가 직접 나온다.

즉 CLI hook-only에서도 다음이 가능하다.

- lid open: `ask_user` 허용
- lid closed: `preToolUse`에서 `ask_user` deny

이 경로는 "사용자 질문 dialog가 뜨기 전에" 막을 수 있다는 점이 중요하다.

Claude 비교 인사이트:

- Claude 메모에서는 `AskUserQuestionTool`이 별도 사용자 질문 도구처럼 보여도 실제로는 `PermissionRequest` 흐름으로 흡수되는 쪽에 가깝다.
- GHCP는 `ask_user`가 명시적인 tool surface로 보인다.
- 즉 GHCP는 raw CLI hook-only에서도 질문 대기를 더 직접적으로 guard할 수 있다.

### 3. `agentStop` [구현됨]

keep-awake를 내릴 1차 종료 edge로는 `agentStop`을 바로 써도 된다.

단서:

- 공식 문서상 `agentStop` / `subagentStop`은 `decision: "block"`과 `reason`으로 continuation을 만들 수 있다.
- 하지만 이 위험은 "다른 hook이 실제로 continuation을 만드는 경우"로 좁혀 생각하는 것이 맞다.

즉 다음 조건이면 `agentStop`을 종료 edge로 써도 실용적이다.

- LidGuard 자신의 `agentStop` hook은 `allow` 또는 empty output만 반환한다.
- `hook-install` / `hook-status`에서 non-LidGuard `agentStop` hook 존재를 경고한다.

구현 결론:

- GHCP는 `agentStop`을 기본 종료 edge로 삼는다.
- continuation risk는 런타임 stop 기준을 `sessionEnd`로 바꾸는 대신, 상태 점검 경고로 푸는 편이 더 낫다.

### 4. `notification(permission_prompt|elicitation_dialog)` [구현됨]

이 경로는 직접 guard는 아니고 감지 전용이다.

공식 문서상 notification types에는 다음이 있다.

- `permission_prompt`
- `elicitation_dialog`
- `agent_idle`
- `agent_completed`

지금 당장 가능한 활용:

- lid closed 상태에서 prompt/dlg 대기 진단 로그를 남긴다.
- softlock suspicion timer를 시작한다.
- event log 또는 hook-events에 남긴다.

현재 구현 상태:

- [완료] `permission_prompt`와 `elicitation_dialog` notification을 session soft-lock state로 기록한다.
- [완료] 남아 있는 active session이 전부 soft-lock이면 runtime이 protection을 해제하고 닫힌 lid 기준 suspend 흐름을 시작한다.
- [완료] `preToolUse`와 `postToolUse`의 non-`ask_user` activity를 감지하면 현재 soft-lock 상태를 해제한다.

직접 못 하는 일:

- 이미 열린 prompt나 dialog에 대신 응답하는 것
- dialog를 강제로 닫는 것
- 사용자의 응답 후 재개를 전용 hook으로 받는 것

### 5. parent process watcher [부분 완화 가능]

이 항목은 GHCP CLI hook이 직접 guard하는 경로는 아니지만, 현재 LidGuard에 이미 있는 런타임 기능으로 완화 가능한 영역이다.

이미 구현된 공통 동작:

- hook start 직후 parent process 또는 그에 준하는 agent 프로세스를 추적한다.
- stop hook이 누락되더라도 agent 프로세스가 실제로 종료되면 cleanup을 시도한다.
- 같은 세션에 대해 stop과 watcher cleanup이 겹쳐도 idempotent하게 처리하는 것이 전제다.

GHCP 구현 참고 결론:

- GHCP 프로세스가 crash 또는 강제 종료되어 `agentStop`이 오지 않는 경우는 parent process watcher가 완화할 수 있다.
- terminal 강제 종료나 wrapper 종료 후 실제 agent 종료 같은 missed-stop 계열도 watcher의 적용 대상이다.
- 다만 이 기능이 있으려면 GHCP hook input이 안정적인 parent process id를 주는지 확인해야 하고, 없다면 Codex/Claude처럼 working directory 기반 resolver fallback이 필요하다.

중요한 한계:

- agent 프로세스가 살아 있는 채로 `permission_prompt`, `ask_user`, `elicitation_dialog`를 기다리는 소프트락에는 watcher가 도움이 되지 않는다.
- LidGuard runtime 자체가 crash한 경우도 watcher가 해결하지 못한다.

## 현재 리미테이션

이 섹션은 GHCP CLI hook만으로는 직접 guard하기 어려운 영역이다.

### 1. `elicitation_dialog` 자체에 대한 직접 응답 경로 없음

Claude와의 가장 큰 차이점이다.

Claude 메모에서는:

- `Elicitation`이 별도 공식 hook event다.
- structured `cancel` 또는 `decline`을 직접 반환할 수 있다.
- 그래서 MCP elicitation은 이미 구현 가능한 guard 대상으로 승격됐다.

반면 GHCP CLI에서는:

- `elicitation_dialog`가 `notification` type으로만 보인다.
- "dialog를 띄우기 전에 structured cancel을 반환하는 전용 hook"이 문서에 보이지 않는다.

결론:

- GHCP CLI만으로는 `elicitation_dialog`를 Claude의 `Elicitation`처럼 직접 guard할 수 없다.
- v1에서는 감지와 타임아웃 완화만 가능하다.

### 2. 이미 열린 prompt/dialog edge case

다음 상황은 GHCP에서도 그대로 남는다.

1. permission prompt 또는 elicitation dialog가 이미 떴다.
2. 그 직후 사용자가 lid를 닫았다.
3. LidGuard가 notification을 보더라도 이미 dialog는 열린 뒤다.

이 경우는:

- 대기 상태는 감지할 수 있어도
- prompt를 회수하거나 대신 응답하기 어렵다.

이는 Claude 메모의 third-party prompt 또는 sandbox prompt와 성격이 비슷하다.

### 3. "사용자 응답 후 재개" 전용 hook 없음

CLI 기준으로는:

- 대기 시작 신호는 어느 정도 보인다.
- 그러나 사용자가 응답해서 다시 turn이 재개되는 순간을 알려주는 전용 hook은 문서에 없다.

즉 다음은 직접 보기 어렵다.

- permission dialog 승인 후 즉시 재개됨
- elicitation dialog 응답 후 즉시 재개됨
- ask_user 응답 후 다시 모델 loop로 돌아감

실전적으로는 이후 tool hook이나 최종 `agentStop`으로만 간접 추정해야 한다.

### 4. non-LidGuard `agentStop` hook continuation risk

이것은 GHCP 고유의 구현 리스크다.

공식 문서상 다른 `agentStop` hook이:

- `decision: "block"`을 반환하고
- `reason`으로 continuation prompt를 주면
- 다음 turn이 강제로 이어질 수 있다.

결론:

- `agentStop` 자체를 버릴 이유는 없다.
- 대신 `hook-install` / `hook-status`가 non-LidGuard `agentStop` hook을 경고해야 한다.

`subagentStop`에 대해서는 현재 문서 기준으로 v1 핵심 종료 기준으로 볼 필요가 낮다.

### 5. `read`와 `hook` permission kinds는 `permissionRequest` 이전 short-circuit

공식 command reference는 `read`와 `hook` permission kinds가 `permissionRequest` 전에 short-circuit된다고 적는다.

즉 다음 전제는 틀리다.

- "모든 승인성 대기 상태를 `permissionRequest`로 잡을 수 있다."

이 항목은 지금 당장 GHCP에서 큰 소프트락 원인이라고 단정할 수는 없지만, `permissionRequest` coverage를 과신하면 안 된다는 의미다.

### 6. raw CLI 문서 불일치

공식 문서끼리도 서술이 어긋난다.

- `sessionStart` output 처리
- `preToolUse`의 `allow` / `ask` / `modifiedArgs`

결론:

- deny path와 no-op path는 비교적 믿을 수 있다.
- 그 외 richer output 동작은 최신 빌드 실측 전까지 보수적으로 본다.

### 7. LidGuard runtime crash

process crash를 하나로 뭉뚱그리면 안 된다.

에이전트 프로세스 종료 쪽은 위의 parent process watcher가 어느 정도 완화한다. 현재 직접 남는 hard limitation은 LidGuard runtime 자체 crash다.

남는 문제:

- runtime이 강제 종료되면 hook cleanup이 안 올 수 있다.
- power plan backup이 남을 수 있다.
- `sessionEnd` 또는 callback cleanup도 보장되지 않는다.

즉 GHCP에서 truly hard한 crash recovery 문제는 hook surface보다 LidGuard 자체 resilience 문제다.

## Claude 메모와 비교한 인사이트

이 섹션은 GHCP 구현 참고 관점에서 Claude 메모와 직접 비교한 결과만 적는다.

### GHCP가 Claude보다 더 쉬운 점

#### 1. `ask_user`가 직접 보인다

Claude 메모에서는 사용자 질문 대기가 주로:

- `PermissionRequest`
- `Elicitation`
- 또는 내부 prompt/UI 상태

로 나뉘어 보인다.

반면 GHCP는:

- `ask_user`가 공식 tool surface에 직접 보인다.

따라서 raw CLI hook-only에서도:

- "질문하려고 한다"는 시점을 곧바로 잡아 deny할 수 있다.

이건 Claude보다 단순하다.

#### 2. 종료 모델을 더 단순하게 잡을 수 있다

Claude 메모는 종료 후보가:

- `Stop`
- `StopFailure`
- `SessionEnd`

로 여러 갈래다.

GHCP는 v1 구현 참고문서 관점에서:

- 시작: `userPromptSubmitted`
- 종료: `agentStop`

으로 더 단순하게 정리할 수 있다.

#### 3. agent process crash 완화는 기존 watcher를 재사용할 수 있다

Claude 메모와 마찬가지로, GHCP도 "stop hook 누락" 자체를 전부 hook 표면으로 해결할 필요는 없다.

- agent 프로세스가 진짜로 죽어 버린 경우
- terminal이 닫히거나 wrapper가 끝난 뒤 실제 agent도 종료된 경우

이 계열은 기존 parent process watcher로 완화할 수 있다.

### GHCP가 Claude보다 더 어려운 점

#### 1. `elicitation_dialog` 직접 guard가 없다

Claude는 `Elicitation` hook이 있어서 structured cancel/decline이 가능하다.

GHCP CLI는 현재 문서상:

- `elicitation_dialog`를 notification으로만 본다.
- dialog 자체를 취소하는 전용 hook이 없다.

이 점은 Claude보다 명확히 불리하다.

#### 2. continuation risk가 `agentStop`에 걸려 있다

Claude 메모에서는 stop 계열 hook이 여러 개이긴 하지만, GHCP처럼 "turn 끝 hook이 continuation prompt를 만들 수 있다"는 문서화가 더 직접적이지 않다.

GHCP는:

- `agentStop`을 기본 종료 edge로 쓰고 싶어도
- 다른 `agentStop` hook이 있으면 continuation risk를 경고해야 한다.

즉 stop edge는 단순하지만, hook 공존 리스크는 더 크다.

#### 3. 사용자 응답 후 재개 관찰이 약하다

Claude는 적어도 `Elicitation`과 `PermissionRequest`라는 named hook surface가 명확하다.

GHCP CLI는:

- 대기 시작은 보이지만
- 응답 후 재개는 전용 hook이 없다.

그래서 "지금 대기 중인지"는 어느 정도 볼 수 있어도, "사용자 응답 후 다시 돌기 시작했다"는 것은 더 간접적으로만 판단된다.

## 구현 체크리스트

GHCP 훅 지원 구현 전에 필요한 체크리스트는 다음과 같다.

### v1에서 바로 구현할 것

1. [완료] `userPromptSubmitted`를 start event로 파싱한다.
2. [완료] `agentStop`을 stop event로 파싱한다.
3. [완료] `permissionRequest`에서 closed-lid allow/deny를 구현한다.
4. [완료] `preToolUse(toolName=ask_user)`에서 closed-lid deny를 구현한다.
5. [완료] `notification(permission_prompt|elicitation_dialog)`를 session soft-lock state와 event log에 기록한다.
6. [완료] `hook-install`과 `hook-status`에서 non-LidGuard `agentStop` hook 존재를 경고한다.
7. [미완료] GHCP hook input에서 parent process id를 직접 받을 수 있는지 확인하고, 없으면 working directory 기반 resolver fallback을 적용한다.
8. [완료] `postToolUse`와 non-`ask_user` `preToolUse` activity로 현재 soft-lock 상태를 해제한다.
9. [완료] active session이 전부 soft-lock이면 runtime이 suspend 흐름을 시작한다.

### v1에서 보조로 둘 것

1. `sessionStart` / `sessionEnd`는 telemetry로만 기록한다.
2. `errorOccurred`는 stop이 아니라 진단용으로만 본다.
3. `subagentStop`은 종료 기준으로 쓰지 않는다.

### 구현 전에 실측할 것

1. raw CLI `preToolUse`에서 `ask_user`가 실제로 항상 잡히는지
2. raw CLI `permissionRequest`가 어떤 tool kinds에서 빠지는지
3. `notification(permission_prompt|elicitation_dialog)`가 항상 뜨는지
4. non-LidGuard `agentStop` hook이 실제로 continuation을 만들 때 LidGuard 종료 타이밍이 어떻게 꼬이는지
5. `sessionEnd`가 실제 idle chat 세션에서는 얼마나 늦게 오는지
6. GHCP hook payload가 안정적인 parent process id를 주는지

## 최종 요약

GHCP 구현 참고문서로서의 결론은 다음이다.

- GHCP v1의 primary 모델은 `userPromptSubmitted` 시작, `agentStop` 종료다.
- `permissionRequest`는 지금 당장 guard 가능한 1차 경로다.
- `ask_user`는 GHCP에서 raw CLI hook-only로도 직접 guard 가능한 중요한 추가 경로다.
- `notification(permission_prompt|elicitation_dialog)`는 현재 구현에서 session soft-lock state를 만드는 감지 경로다.
- `preToolUse`와 `postToolUse` activity는 현재 soft-lock 해제 신호로 쓴다.
- stop 누락이나 agent process crash는 existing parent process watcher로 부분 완화 가능하다.
- `sessionEnd`는 keep-awake 종료 기준이 아니라 telemetry로만 보는 것이 맞다.
- 다른 `agentStop` hook이 continuation을 만들 수 있으므로 `hook-install` / `hook-status`에서 경고해야 한다.
- Claude와 비교하면 GHCP는 `ask_user` guard는 더 쉽지만, `elicitation_dialog` 직접 guard는 더 어렵다.
