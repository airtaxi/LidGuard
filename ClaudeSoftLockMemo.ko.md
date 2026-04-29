# Claude Code 소프트락 분석 메모

분석 대상 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src`

작성 목적은 LidGuard가 Claude Code 세션을 지키는 동안 `Stop` 이벤트만 기다리면 놓칠 수 있는 사용자 입력 대기 상태를 정리하는 것이다.

## 문제 정의

LidGuard의 기본 모델은 다음 흐름이다.

1. Claude `UserPromptSubmit` hook에서 세션 시작을 감지한다.
2. LidGuard runtime이 Windows 절전 방지와 lid close 정책 변경을 유지한다.
3. Claude `Stop`, `StopFailure`, `SessionEnd` hook에서 세션 종료를 감지한다.
4. 마지막 세션이 끝나면 power request를 해제하고 lid action을 복구한다.

문제는 Claude가 작업 도중 사용자 입력을 기다리는 상태에 들어가면, 아직 turn이 끝나지 않았기 때문에 `Stop`이 오지 않는다는 점이다. 노트북 lid가 닫힌 상태라면 사용자는 승인/거절을 할 수 없고, LidGuard는 세션이 살아 있다고 판단해 절전 방지를 계속 유지할 수 있다.

이 문서에서는 이런 상태를 "소프트락"이라고 부른다.

소프트락의 기준:

- Claude 프로세스는 살아 있다.
- LidGuard 세션도 active 상태다.
- Claude는 모델 작업을 계속하는 것이 아니라 사용자 입력, 승인, 확인, 또는 외부 응답을 기다린다.
- `Stop`/`StopFailure`/`SessionEnd`가 아직 발생하지 않는다.
- 사용자가 lid를 닫은 상태라면 정상적으로 응답할 수 없다.

## 현재 이미 커버되는 영역

### Stop, StopFailure, SessionEnd

Claude의 정상 turn 종료는 `Stop`으로 잡을 수 있다. API 오류 등으로 일반 `Stop` hook을 실행하지 않는 일부 경로는 `StopFailure`를 fire-and-forget으로 실행한다.

LidGuard가 이미 `Stop`, `StopFailure`, `SessionEnd`를 stop 처리에 매핑하고 있다면 다음은 기본적으로 커버된다.

- 정상 응답 완료
- stop hook을 돌릴 수 없는 API 오류 경로
- 세션 종료 또는 종료 처리

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\query.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\utils\hooks.ts`

단, 이들은 "끝난 뒤 정리" 이벤트다. Claude가 중간에 사용자 입력을 기다리는 상태는 별도로 봐야 한다.

### PermissionRequest

`PermissionRequest`를 lid-closed guard로 처리한 것은 핵심적으로 맞다.

Claude의 일반 tool permission dialog는 `PermissionRequest` hook을 통해 프로그램적으로 allow/deny가 가능하다. lid가 닫힌 상태에서 LidGuard가 structured output을 반환하면 Claude가 사용자 입력 dialog를 띄우거나 기다리지 않도록 만들 수 있다.

`PermissionRequest`로 커버되는 대표 상황:

- Bash, file edit 등 일반 tool 승인 요청
- `PreToolUse` hook이나 permission 정책이 최종적으로 `ask`를 만든 경우의 tool permission 흐름
- `AskUserQuestionTool`
- plan 관련 승인 UI 중 tool permission 흐름을 타는 항목
- worker가 아니라 일반 REPL에서 발생하는 대부분의 tool permission prompt

`AskUserQuestionTool`도 별도 사용자 질문 도구처럼 보이지만, 소스상 `requiresUserInteraction()`이 `true`이고 `checkPermissions()`가 `ask`를 반환하므로 tool permission 경로로 들어간다. 따라서 LidGuard의 `PermissionRequest` guard 범위로 보는 것이 맞다.

주의할 점:

- `PermissionRequest` hook 함수 자체는 `requestPrompt` 인자를 받을 수 있는 형태지만, 일반 permission context의 `runHooks()` 호출에서는 `requestPrompt`를 넘기지 않는다.
- 즉 일반 `PermissionRequest` hook이 다시 Claude prompt dialog를 띄우는 구조는 기본 경로에서 보이지 않는다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\toolPermission\PermissionContext.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\toolPermission\handlers\interactiveHandler.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\tools\AskUserQuestionTool\AskUserQuestionTool.tsx`
- `C:\Users\kck41\Downloads\claude-code-main\src\types\hooks.ts`

## 프로그램이 추가로 커버할 수 있는 영역

이 섹션의 항목들은 Claude hook이나 Claude 설정 설치 경로를 확장하면 LidGuard가 직접 대응할 수 있다.

### MCP Elicitation

가장 중요한 추가 후보는 `Elicitation`이다.

MCP 서버는 Claude에게 사용자 입력 또는 확인을 요청할 수 있다. Claude는 먼저 `Elicitation` hook을 실행하고, hook이 응답을 주지 않으면 `elicitation.queue`에 request를 넣고 `ElicitationDialog`를 띄워 사용자의 응답을 기다린다.

이 대기 상태는 `PermissionRequest`가 아니다.

따라서 현재 LidGuard가 `PermissionRequest`만 guard한다면, MCP 서버가 elicitation을 요청하는 순간 lid가 닫힌 노트북에서 다음 상황이 가능하다.

1. LidGuard 세션은 active다.
2. Claude는 MCP elicitation dialog 응답을 기다린다.
3. 아직 `Stop`은 발생하지 않는다.
4. 사용자는 lid가 닫힌 상태라 응답할 수 없다.
5. 절전 방지가 계속 유지될 수 있다.

Claude hook output schema에는 `Elicitation` 전용 structured output이 있다.

예시 정책:

```json
{
  "hookSpecificOutput": {
    "hookEventName": "Elicitation",
    "action": "cancel"
  },
  "reason": "LidGuard: laptop lid is closed."
}
```

또는 정책상 사용자가 명확히 거절한 것처럼 다루고 싶다면 `decline`을 사용할 수 있다.

권장 동작:

- lid open: stdout을 비워서 Claude 기본 elicitation flow를 유지한다.
- lid closed + 설정이 deny/cancel 계열: `Elicitation` hook에서 `cancel` 또는 `decline`을 반환한다.
- lid closed + 명시적으로 allow 성격의 정책이 필요하다면, content를 합성해야 하는 케이스가 있어서 일반적으로 권장하지 않는다.
- `ElicitationResult`는 사용자가 응답한 뒤의 후처리 hook에 가깝기 때문에 소프트락 방지의 1차 대상은 아니다.

구현 후보:

- Claude hook installer가 `Elicitation` 이벤트에도 `lidguard claude-hook`을 설치한다.
- `claude-hook` parser가 `hook_event_name = "Elicitation"`을 인식한다.
- runtime lid state를 조회한다.
- lid가 닫힌 경우 configured policy에 따라 structured JSON을 출력한다.
- lid가 열려 있거나 runtime 상태를 알 수 없으면 빈 stdout으로 no-op 처리한다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\services\mcp\elicitationHandler.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`
- `C:\Users\kck41\Downloads\claude-code-main\src\types\hooks.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\entrypoints\sdk\coreTypes.ts`

### Hook prompt request 일부

Claude에는 hook command가 stdout으로 prompt request JSON을 출력하고, Claude가 `PromptDialog`를 띄운 뒤 응답을 hook stdin으로 돌려주는 기능이 있다. 이 기능은 `HOOK_PROMPTS` feature가 켜져 있을 때 REPL의 `requestPrompt`를 통해 활성화된다.

이 prompt request는 별도 hook event가 아니라, 기존 hook 실행 중 hook command가 사용자에게 추가 입력을 요청하는 프로토콜이다.

LidGuard 자신의 hook은 prompt request를 출력하지 않도록 만들 수 있다. 이 부분은 프로그램이 완전히 통제 가능하다.

또한 LidGuard가 설치하는 hook command가 다음 원칙을 지키면 LidGuard 자체가 소프트락을 만들 위험은 낮다.

- hook에서 추가 사용자 입력을 요청하지 않는다.
- lid open/closed 판단이 불가능하면 빈 stdout으로 no-op 한다.
- 실패해도 Claude 작업을 막지 않는다.
- hook timeout에 의존하지 않는다.

하지만 다른 third-party hook이 prompt request를 출력하는 경우는 별도 문제다. 이 경우는 아래 "직접 커버하기 어려운 영역"에 해당한다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\utils\hooks.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`

## 프로그램이 직접 커버하기 어려운 영역

이 섹션의 항목들은 현재 Claude hook API만으로는 LidGuard가 안정적으로 대신 응답하기 어렵다. 별도 설정, 상태 감시, upstream 변경, 또는 제한적 휴리스틱이 필요하다.

### Sandbox network permission

가장 큰 미해결 후보는 sandbox network permission이다.

Claude의 sandbox network 접근이 새 host에 대해 사용자 승인을 요구하면 `sandboxPermissionRequestQueue`에 request를 넣고 `SandboxPermissionRequest` UI를 띄운다. worker 모드에서는 mailbox를 통해 leader에게 sandbox permission request를 보낸 뒤 응답을 기다릴 수 있다.

이 경로는 `PermissionRequest` hook이 아니다.

따라서 LidGuard가 `PermissionRequest`를 guard해도 다음 상황이 남는다.

1. Claude가 sandbox 안에서 네트워크 접근을 시도한다.
2. Claude가 host 접근 허용 여부를 사용자에게 묻는다.
3. lid가 닫혀 있어 사용자는 응답할 수 없다.
4. `Stop`은 아직 오지 않는다.
5. LidGuard는 세션 active 상태를 유지할 수 있다.

추가로 주의할 점:

- REPL의 `hasActivePrompt`에는 `sandboxPermissionRequestQueue`가 포함된다.
- 하지만 `isWaitingForApproval`에는 로컬 `sandboxPermissionRequestQueue`가 포함되어 있지 않다.
- 따라서 단순히 Claude의 waiting 상태나 `waitingFor` 문자열만 보고 감시하면 sandbox permission dialog를 놓칠 가능성이 있다.

현재 hook만으로 가능한 직접 대응:

- 명확한 `SandboxPermissionRequest` hook event가 보이지 않는다.
- `PermissionRequest` structured output으로 이 dialog를 대신 처리하는 경로도 보이지 않는다.

가능한 우회/완화:

- Claude sandbox network 정책을 사전에 보수적으로 설정해 prompt가 뜨지 않게 한다.
- LidGuard 문서에 sandbox network permission은 직접 guard 불가 항목으로 명시한다.
- `hook-status` 또는 진단 명령에서 sandbox 관련 위험을 안내한다.
- Claude가 향후 sandbox permission hook 또는 machine-readable session state를 제공하면 그 경로를 사용한다.
- 상태 감시를 붙인다면 `isWaitingForApproval`만 믿지 말고 `sandboxPermissionRequestQueue`에 해당하는 신호가 노출되는지 별도 검증해야 한다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`
- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\useSwarmPermissionPoller.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\useInboxPoller.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\utils\swarm\permissionSync.ts`

### Third-party hook prompt request

LidGuard가 설치한 hook이 아니라, 사용자가 설정한 다른 Claude hook이 prompt request를 출력할 수 있다.

예를 들어 `UserPromptSubmit`, `PreToolUse`, `Stop` 계열 hook이 prompt request JSON을 출력하면 Claude는 prompt dialog를 띄우고 사용자의 선택을 기다릴 수 있다.

LidGuard가 이 상황을 직접 막기 어려운 이유:

- prompt request는 별도 hook event가 아니다.
- 다른 hook command 내부에서 발생한다.
- LidGuard hook이 같은 event에 설치되어 있어도, 다른 hook이 사용자 입력을 기다리면 Claude의 hook 실행 전체가 timeout 전까지 지연될 수 있다.
- command hook timeout 기본값은 10분이지만, hook 설정에 따라 달라질 수 있다.

가능한 완화:

- LidGuard가 생성하는 hook은 절대 prompt request를 사용하지 않는다.
- `hook-status`에서 Claude 설정에 LidGuard 외 다른 hook이 있음을 감지하면 "third-party hook prompt는 LidGuard가 대신 응답할 수 없다"고 안내한다.
- 사용자가 lid-closed guard를 강하게 원하면 prompt-producing hook을 비활성화하도록 문서화한다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\utils\hooks.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`

### Swarm/teammate worker permission

Claude의 swarm/teammate 기능에서는 worker가 직접 사용자에게 묻지 않고 leader에게 permission request를 mailbox로 전달할 수 있다.

일반 tool permission과 겹치는 부분도 있지만, worker/leader 경로는 다음처럼 별도 상태를 만든다.

- worker 쪽: `pendingWorkerRequest`
- leader 쪽: `ToolUseConfirmQueue` 또는 `workerSandboxPermissions.queue`
- sandbox 관련 worker request: `workerSandboxPermissions.queue`

일반 tool permission은 최종적으로 `PermissionRequest`와 닿는 경우가 많지만, swarm worker forwarding 경로는 항상 LidGuard hook이 선행된다고 단정하기 어렵다. 특히 sandbox worker permission은 앞의 sandbox network permission과 같은 이유로 직접 guard가 어렵다.

우선순위는 일반 사용 시 낮지만, 팀/worker 기능을 지원 대상으로 넣는다면 별도 검증이 필요하다.

가능한 완화:

- v1에서는 "single local Claude session" 중심으로 지원 범위를 명시한다.
- teammate/swarm 기능은 known limitation으로 둔다.
- 향후 Claude가 worker permission 상태를 안정적으로 노출하면 LidGuard가 상태 기반 cleanup 또는 policy decision을 붙인다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\toolPermission\handlers\swarmWorkerHandler.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\hooks\useInboxPoller.ts`
- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`

### Cost dialog, idle-return, onboarding, local JSX dialog

Claude REPL에는 cost threshold dialog, idle-return dialog, onboarding, model switch, plugin hint, desktop upsell, local JSX slash command UI 같은 사용자 입력 dialog도 있다.

이들은 실제로 사용자 입력을 기다릴 수 있지만 LidGuard의 핵심 소프트락 범위로 바로 넣기에는 우선순위가 낮다.

이유:

- cost dialog는 `!isLoading && showCostDialog`일 때 표시되므로, 일반적으로 active model turn 중간 대기라기보다 turn 이후 UX에 가깝다.
- onboarding이나 setup류는 보통 `UserPromptSubmit`으로 시작된 agent session 전에 나타난다.
- local JSX slash command는 사용자가 명령을 직접 실행한 UI 흐름에 가깝고, 장시간 agent 작업 보호 문제와 직접 관련성이 낮다.

단, Claude 내부 UI가 바뀌면 이 판단은 다시 확인해야 한다.

참고 소스:

- `C:\Users\kck41\Downloads\claude-code-main\src\screens\REPL.tsx`

## 권장 구현 우선순위

### 1. Elicitation guard 추가

가장 먼저 추가할 만한 항목이다.

이유:

- Claude가 공식 hook event로 노출한다.
- structured output schema가 있다.
- MCP 서버가 사용자 확인을 요구하는 경로는 실제 agent 작업 중 발생할 수 있다.
- `PermissionRequest`와 같은 방식으로 lid-closed only decision을 적용하기 쉽다.

권장 세부 작업:

- Claude hook installer가 `Elicitation` 이벤트를 설치하도록 확장한다.
- `claude-hooks --format settings-json` snippet에도 `Elicitation`을 포함한다.
- `claude-hook`에서 `Elicitation` input을 파싱한다.
- runtime status/lid state를 조회한다.
- lid closed일 때 `cancel` 또는 `decline`을 반환한다.
- lid open, unknown, runtime unavailable일 때는 빈 stdout으로 no-op 한다.
- event log에 Elicitation guard decision을 남긴다.

`ElicitationResult`는 일단 설치하지 않아도 된다. 응답 이후 후처리 성격이라 소프트락 해소에는 직접 필요하지 않다.

### 2. Sandbox network permission은 limitation으로 문서화

현재 Claude source 기준으로는 sandbox network permission을 LidGuard hook으로 바로 처리할 수 있는 안정적인 경로가 보이지 않는다.

권장:

- `AGENTS.md`와 사용자 문서에 known limitation으로 추가한다.
- hook status 진단에서 sandbox network permission은 직접 guard 대상이 아님을 표시한다.
- Claude 설정으로 prompt가 생기지 않게 할 수 있는 옵션이 있는지 별도 조사한다.
- upstream hook/status API가 생기면 지원한다.

### 3. Third-party hook prompt 위험 안내

LidGuard 자체 hook은 prompt를 만들지 않으면 된다. 다만 사용자 환경의 다른 hook이 prompt request를 만들면 LidGuard가 대신 응답할 수 없다.

권장:

- `hook-status`에서 LidGuard 외 hook 존재 여부를 보여준다.
- prompt-producing hook은 lid-closed automation과 충돌할 수 있음을 문서화한다.
- LidGuard가 설치하는 hook은 계속 no-prompt/no-blocking 원칙을 유지한다.

### 4. Swarm/teammate는 후순위 검증

일반적인 로컬 Claude 단일 세션에서 먼저 `PermissionRequest`와 `Elicitation`을 확실히 처리하는 것이 우선이다.

이후 팀/worker 기능을 지원 범위에 넣을 때 다음을 별도로 확인한다.

- worker permission이 worker process의 `PermissionRequest` hook을 항상 거치는지
- leader side queue를 외부에서 감지할 수 있는지
- sandbox worker permission을 정책적으로 거절/허용할 API가 있는지
- LidGuard가 다중 Claude process를 어떻게 session key로 묶을지

## 요약

`PermissionRequest` guard만으로는 충분하지 않다.

프로그램이 직접 커버할 수 있는 다음 후보는 `Elicitation`이다. MCP elicitation은 별도 hook event와 structured output이 있으므로 LidGuard가 lid-closed 상태에서 `cancel`/`decline`을 반환하도록 확장하는 것이 좋다.

프로그램이 현재 hook만으로 직접 커버하기 어려운 가장 큰 후보는 sandbox network permission이다. 이 경로는 `PermissionRequest`가 아니고, REPL의 waiting 상태 계산에서도 일부 빠져 있어 단순 상태 감시로도 놓칠 수 있다.

third-party hook prompt와 swarm/teammate worker permission은 환경 의존성이 크다. v1에서는 known limitation으로 두고, LidGuard 자체 hook이 prompt를 만들지 않도록 보장하는 것이 현실적이다.
