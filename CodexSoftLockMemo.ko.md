# Codex 소프트락 분석 메모

분석 대상 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs`
- upstream: `https://github.com/openai/codex/tree/main/codex-rs`
- 확인 commit: `e1ec9e63a07843e2320845fe2741c5caa78dbc98`
- 확인 시점: 2026-04-29

작성 목적은 LidGuard가 Codex 세션을 지키는 동안 `Stop` 이벤트만 기다리면 놓칠 수 있는 사용자 입력 대기 상태를 정리하는 것이다.

이 문서는 다음 두 관점으로 나눈다.

- LidGuard가 현재 Codex hook 또는 LidGuard runtime 정책으로 커버할 수 있는 영역
- 현재 Codex hook만으로는 LidGuard가 직접 커버하기 어려운 영역

또한 관련 기능이 under-development 상태인지, 선제적으로 대응할 필요가 있는지도 함께 정리한다.

## 문제 정의

LidGuard의 기본 모델은 다음 흐름이다.

1. Codex `UserPromptSubmit` hook에서 세션 시작을 감지한다.
2. LidGuard runtime이 Windows 절전 방지와 lid close 정책 변경을 유지한다.
3. Codex `Stop` hook에서 세션 종료를 감지한다.
4. 마지막 세션이 끝나면 power request를 해제하고 lid action을 복구한다.

문제는 Codex가 작업 도중 사용자 입력, 승인, 확인, 또는 클라이언트 응답을 기다리는 상태에 들어가면 아직 turn이 끝나지 않았기 때문에 `Stop`이 오지 않는다는 점이다. 노트북 lid가 닫힌 상태라면 사용자는 승인/거절을 할 수 없고, LidGuard는 세션이 살아 있다고 판단해 절전 방지를 계속 유지할 수 있다.

이 문서에서는 이런 상태를 "소프트락"이라고 부른다.

소프트락의 기준:

- Codex 프로세스는 살아 있다.
- LidGuard 세션도 active 상태다.
- Codex는 모델 작업을 계속하는 것이 아니라 사용자 입력, 승인, 확인, 또는 외부 응답을 기다린다.
- `Stop`이 아직 발생하지 않는다.
- 사용자가 lid를 닫은 상태라면 정상적으로 응답할 수 없다.

## Codex hook 표면

현재 Codex `codex-rs` 소스 기준으로 config에서 노출되는 hook event는 다음 6개다.

- `PreToolUse`
- `PermissionRequest`
- `PostToolUse`
- `SessionStart`
- `UserPromptSubmit`
- `Stop`

중요한 관찰:

- 현재 소스 기준 `SessionEnd` hook event는 보이지 않는다.
- `StopFailure` hook event도 보이지 않는다.
- Claude Code처럼 `Elicitation` 전용 hook event가 있지 않다.
- `RequestUserInput`, `RequestPermissions`, `ElicitationRequest`, `DynamicToolCallRequest`는 Codex 내부 event 또는 app-server request로 존재하지만, Codex hook event로 직접 노출되지는 않는다.
- command hook은 timeout이 있다. config에 timeout이 없으면 현재 discovery path에서 기본 `600`초를 사용한다. 따라서 hook command 자체가 영구 대기하지는 않지만, 최대 10분 동안 turn을 붙잡을 수 있다.
- hook handler 실행은 `join_all`로 병렬 실행된다. 따라서 같은 `UserPromptSubmit` event에 다른 hook이 block하더라도 LidGuard hook이 이미 start를 보낼 수 있다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\config\src\hook_config.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\engine\dispatcher.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\engine\discovery.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\engine\command_runner.rs`

## 현재 이미 커버되는 영역

### Stop [구현됨]

Codex의 정상 turn 종료는 `Stop` hook으로 잡을 수 있다.

LidGuard가 `Stop`을 stop 처리에 매핑하고 있다면 다음은 기본적으로 커버된다.

- 모델 응답 완료
- 더 이상 follow-up tool/model loop가 필요 없는 정상 turn 종료
- 일반적인 세션 active count 감소
- 마지막 세션 종료 후 power request 해제와 lid action 복구

단, `Stop`은 "끝난 뒤 정리" 이벤트다. Codex가 중간에 사용자 입력을 기다리는 상태는 `Stop` 전에 발생하므로 별도로 봐야 한다.

추가 주의:

- Codex의 `Stop` hook은 다른 hook이 `decision: "block"` 또는 exit code `2`를 반환하면 continuation prompt를 만들고 모델이 계속 진행할 수 있다.
- LidGuard가 `Stop`을 받자마자 stop 처리하면, 다른 `Stop` hook이 continuation을 만든 경우 "Codex는 계속 도는데 LidGuard 보호가 먼저 꺼지는" 반대 방향 문제가 생길 수 있다.
- 이 문제는 노트북이 계속 깨어 있는 소프트락과는 반대 성격이지만, `Stop`을 세션 최종 종료로 단정하면 안 된다는 신호다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\events\stop.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\session\turn.rs`

### PermissionRequest [구현됨]

`PermissionRequest`를 lid-closed guard로 처리한 것은 핵심적으로 맞다.

Codex의 `PermissionRequest` hook은 approval path에서 guardian 또는 사용자 승인 UI가 뜨기 전에 실행된다. hook stdout이 비어 있으면 Codex의 기본 approval flow가 계속되고, structured allow/deny JSON을 반환하면 프로그램적으로 승인 또는 거절할 수 있다.

`PermissionRequest`로 커버되는 대표 상황:

- 일반 shell command 승인
- `apply_patch` 승인
- unified exec 승인
- exec policy amendment 또는 elevated execution 성격의 승인
- network approval
- MCP tool approval

특히 MCP tool approval은 현재 소스상 `run_permission_request_hooks()`를 먼저 호출한다. 따라서 lid가 이미 닫힌 상태에서 MCP tool approval이 발생하면 LidGuard의 `PermissionRequest` decision으로 사용자 approval UI 진입을 막을 수 있다.

권장 동작:

- lid open: stdout을 비워서 Codex 기본 approval flow를 유지한다.
- lid closed + 설정이 deny: structured deny를 반환한다.
- lid closed + 설정이 allow: structured allow를 반환한다.
- runtime status를 알 수 없음: 빈 stdout으로 no-op하고, 진단만 로컬에 남긴다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\events\permission_request.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\orchestrator.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\network_approval.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\mcp_tool_call.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\runtimes\apply_patch.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\runtimes\shell.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\runtimes\unified_exec.rs`

## 프로그램이 커버할 수 있는 영역

이 섹션의 항목들은 현재 Codex hook 또는 LidGuard runtime 정책으로 대응할 수 있다.

### Hook 기반으로 직접 커버 가능한 영역

#### PermissionRequest 계열 approval

현재 구현 방향 그대로 가장 강한 커버리지다.

프로그램이 직접 할 수 있는 일:

- `PermissionRequest` hook을 설치한다.
- runtime lid state를 조회한다.
- lid가 닫힌 경우 설정에 따라 allow/deny structured output을 반환한다.
- lid가 열려 있거나 알 수 없으면 빈 stdout으로 no-op한다.

이 방식은 "approval UI가 뜨기 전에 lid가 이미 닫혀 있는 상황"을 직접 해결한다.

#### 정상 Stop cleanup [구현됨]

정상 turn 종료 후 cleanup은 `Stop` hook으로 처리할 수 있다.

프로그램이 직접 할 수 있는 일:

- `Stop` hook을 설치한다.
- `Stop` 수신 시 해당 Codex session을 stop 처리한다.
- 마지막 active session이 사라지면 power request와 lid action을 복구한다.
- stop IPC 실패는 로컬 event log에 남긴다.

주의:

- 현재 Codex source 기준으로는 `SessionEnd`가 hook event로 보이지 않는다.
- 따라서 Codex provider의 "필수 stop hook"은 현재 소스 기준 `Stop` 중심으로 재검토해야 한다.

구현 메모:

- 2026-04-29 기준 LidGuard 저장소는 Codex `hook-install`, `hook-status`, 기본 snippet에서 `Stop`을 필수 stop hook으로 보고, `SessionEnd`는 있으면 받는 선택적 호환 hook으로 정리되어 있다.

#### LidGuard hook 자체의 no-prompt/no-blocking 원칙 [구현됨]

LidGuard가 설치하는 hook command는 사용자 입력을 추가로 요구하지 않아야 한다.

프로그램이 직접 할 수 있는 일:

- LidGuard hook command는 stdin hook JSON만 읽고, 별도 prompt를 만들지 않는다.
- runtime 상태를 알 수 없으면 Codex 작업을 막지 않고 no-op한다.
- 실패해도 stdout에 잘못된 structured JSON을 내지 않는다.
- hook timeout에 의존하지 않도록 빠르게 종료한다.

이 원칙은 LidGuard 자체가 소프트락 원인이 되는 것을 막는다.

### Runtime 정책으로 간접 커버 가능한 영역

아래 항목들은 Codex hook으로 "Codex에게 대신 응답"하기는 어렵지만, LidGuard runtime 정책으로 "노트북이 계속 깨어 있는 상태"는 줄일 수 있다.

#### Parent process watcher [구현됨]

Hook stop이 빠지거나 Codex 프로세스가 먼저 종료된 경우는 process watcher로 정리할 수 있다.

프로그램이 직접 할 수 있는 일:

- `UserPromptSubmit`에서 session start와 함께 가능한 parent process를 추적한다.
- parent process가 종료되면 session을 stop 처리한다.
- 중복 `Stop`과 watcher cleanup은 idempotent하게 처리한다.

이 방식은 Codex가 실제로 종료되었는데 hook stop이 누락된 경우에 유효하다. 다만 Codex가 살아 있는 입력 대기 상태에는 적용되지 않는다.

#### Closed-lid active-session timeout

Codex hook만으로 직접 응답할 수 없는 대기 상태를 완화하려면 LidGuard runtime이 lid close 자체를 정책 신호로 볼 수 있다.

가능한 정책 예시:

- active session이 있는 동안 lid가 닫힌다.
- 일정 grace period 동안 새 `Stop`이나 process 종료가 없다.
- 그 상태가 계속되면 설정에 따라 다음 중 하나를 수행한다.
  - power request를 해제하고 lid action을 복구한다.
  - session을 stale/soft-locked로 표시하고 runtime protection을 중지한다.
  - opt-in 설정일 때 sleep 또는 hibernate를 시도한다.

이 정책은 Codex의 pending prompt에 대신 답하는 것은 아니다. 대신 "사용자가 lid를 닫아 응답할 수 없는 상태에서 LidGuard가 무기한 절전 방지를 유지하는 것"을 막는다.

중요한 운영 판단:

- `PermissionRequest` prompt가 뜬 뒤 사용자가 lid를 닫는 edge-trigger 한계는 사용자 과실 또는 운용 한계로 보는 것이 맞다.
- LidGuard가 모든 이미 표시된 UI prompt를 되돌아가서 자동 거절할 수는 없다.
- 다만 closed-lid timeout은 사용자가 lid를 닫은 뒤 시스템이 계속 깨어 있는 문제를 완화할 수 있다.

#### App-server 상태 감시

Codex app-server에는 thread status와 active flag가 있다.

현재 확인된 active flag:

- `WaitingOnApproval`
- `WaitingOnUserInput`

app-server의 bespoke event handling은 다음 event들을 status로 반영한다.

- `ExecApprovalRequest`
- `ApplyPatchApprovalRequest`
- `ElicitationRequest`
- `RequestPermissions`
- `RequestUserInput`

프로그램이 Codex app-server와 통합할 수 있다면, hook-only 방식보다 더 정확하게 "현재 사용자를 기다리는지"를 알 수 있다.

가능한 정책:

- `WaitingOnApproval` 또는 `WaitingOnUserInput` 상태가 일정 시간 유지된다.
- 동시에 Windows lid state가 closed다.
- LidGuard가 active session을 soft-locked로 판단하고 protection 해제 또는 suspend 정책을 적용한다.

단, 현재 LidGuard의 provider hook 구조만으로는 이 상태를 직접 구독하지 못한다. 별도 Codex app-server integration 또는 upstream 상태 API가 필요하다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\app-server\src\thread_status.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\app-server\src\bespoke_event_handling.rs`

## 프로그램이 직접 커버하기 어려운 영역

이 섹션의 항목들은 현재 Codex hook API만으로는 LidGuard가 안정적으로 대신 응답하기 어렵다. 별도 runtime timeout, app-server integration, upstream hook 추가, 또는 사용자의 설정 제한이 필요하다.

### RequestUserInput

가장 중요한 미커버 후보는 `RequestUserInput`이다.

Codex에는 `request_user_input` tool handler가 있고, 이 handler는 `session.request_user_input()`을 호출한다. `Session::request_user_input()`은 pending user input oneshot을 등록하고 `EventMsg::RequestUserInput`을 보낸 뒤 응답을 기다린다.

이 대기 상태는 `PermissionRequest`가 아니다.

따라서 현재 LidGuard가 `PermissionRequest`만 guard한다면 다음 상황이 가능하다.

1. Codex turn이 시작되어 LidGuard session이 active가 된다.
2. 모델 또는 내부 기능이 `request_user_input`을 호출한다.
3. Codex는 `RequestUserInput` 응답을 기다린다.
4. 아직 `Stop`은 발생하지 않는다.
5. 사용자가 lid를 닫은 상태라면 응답할 수 없다.
6. LidGuard는 active session으로 보고 절전 방지를 계속 유지할 수 있다.

`RequestUserInput`이 발생할 수 있는 대표 경로:

- Plan mode의 root thread 사용자 질문
- `DefaultModeRequestUserInput` feature가 켜진 Default mode
- skill MCP dependency install prompt
- skill environment variable dependency prompt
- MCP tool approval에서 `ToolCallMcpElicitation`이 꺼져 있을 때 fallback prompt
- delegated MCP request user input forwarding 일부

현재 hook만으로 가능한 직접 대응:

- `RequestUserInput` 전용 Codex hook event가 없다.
- `PermissionRequest` structured output으로 `RequestUserInput`을 대신 처리하는 경로도 보이지 않는다.

가능한 완화:

- closed-lid active-session timeout으로 power request를 무기한 유지하지 않는다.
- app-server 상태를 구독할 수 있다면 `WaitingOnUserInput`을 softlock 신호로 사용한다.
- under-development feature가 Default mode로 확장될 경우를 추적한다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\handlers\request_user_input.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\session\mod.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\mcp_skill_dependencies.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\skills.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\protocol\src\config_types.rs`

### RequestPermissions

`RequestPermissions`는 모델이 추가 permission profile을 요청하는 tool 경로다.

현재 `RequestPermissionsTool` feature는 under-development이며 기본값이 꺼져 있다. 따라서 일반 사용자 환경에서 즉시 높은 빈도로 발생할 가능성은 낮다.

하지만 feature가 켜지면 다음 흐름이 가능하다.

1. 모델이 `request_permissions` tool을 호출한다.
2. Codex는 `Session::request_permissions()`에서 approval policy를 확인한다.
3. guardian으로 자동 review할 수 없으면 pending request를 등록한다.
4. `EventMsg::RequestPermissions`를 보내고 응답을 기다린다.
5. 아직 `Stop`은 발생하지 않는다.

이 대기 상태도 `PermissionRequest` hook이 아니다.

현재 hook만으로 가능한 직접 대응:

- `RequestPermissions` 전용 Codex hook event가 없다.
- `PermissionRequest` hook decision으로 `RequestPermissions`를 대신 승인/거절하는 경로는 보이지 않는다.

선제 대응 판단:

- 기능이 under-development이고 default off이므로 v1에서 별도 hook 대응을 만들 필요는 낮다.
- 다만 event shape는 이미 app-server에 있고 `WaitingOnApproval`로 묶인다.
- closed-lid timeout 또는 app-server 상태 감시를 만들면 이 경로도 자연스럽게 커버된다.
- 기능이 stable/default-on으로 바뀌는지 release/source tracking이 필요하다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\handlers\request_permissions.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\session\mod.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\features\src\lib.rs`

### MCP Elicitation

MCP 서버는 클라이언트에게 추가 입력 또는 확인을 요청할 수 있다. Codex는 이를 `EventMsg::ElicitationRequest`로 전달하고 응답을 기다린다.

Claude Code와 달리 현재 Codex hook surface에는 `Elicitation` 전용 hook event가 없다.

구분이 중요하다.

- MCP tool approval: `PermissionRequest` hook을 먼저 거치므로 lid-closed 상태에서 LidGuard가 deny하면 상당 부분 커버된다.
- MCP server elicitation: 별도 `ElicitationRequest`로 발생하며 `PermissionRequest` hook을 거치지 않는 경로가 있다.
- `tool_suggest`: tool suggestion confirmation에 MCP elicitation을 사용한다.

소프트락 가능 흐름:

1. Codex turn이 active다.
2. MCP 서버 또는 tool suggestion 경로가 elicitation을 요청한다.
3. Codex는 `ElicitationRequest` 응답을 기다린다.
4. 아직 `Stop`은 발생하지 않는다.
5. lid가 닫힌 상태라면 사용자는 응답할 수 없다.

현재 hook만으로 가능한 직접 대응:

- Codex `Elicitation` hook event가 없으므로 hook-only 방식으로 structured cancel/decline을 반환할 수 없다.
- app-server request에 직접 응답할 수 있는 integration이 없다면 LidGuard가 대신 답하기 어렵다.

가능한 완화:

- closed-lid active-session timeout으로 무기한 절전 방지를 막는다.
- app-server 상태를 구독할 수 있다면 `WaitingOnApproval`을 softlock 신호로 사용한다.
- Codex upstream이 `Elicitation` hook 또는 machine-readable pending state API를 제공하면 직접 지원한다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\session\mcp.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\codex-mcp\src\elicitation.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\handlers\tool_suggest.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\mcp_tool_call.rs`

### DynamicToolCallRequest

Dynamic tool call은 `EventMsg::DynamicToolCallRequest`를 보내고 client/tool response를 기다린다.

이 경로는 보통 사용자 prompt라기보다 앱/클라이언트 도구 응답 대기다. 그래도 외부 client가 응답하지 않으면 turn 중간 대기가 될 수 있다.

현재 hook만으로 가능한 직접 대응:

- `DynamicToolCallRequest` 전용 hook event가 없다.
- LidGuard가 tool client response를 대신 만들 수 없다.

우선순위 판단:

- 일반적인 "사용자가 lid를 닫아 approval dialog에 응답하지 못하는" 문제와는 거리가 있다.
- v1에서는 known residual risk로 두고, app-server 상태나 timeout 기반 정책으로만 간접 완화하는 것이 적절하다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\core\src\tools\handlers\dynamic.rs`

### Third-party hook block 또는 prompt

사용자가 LidGuard 외 다른 Codex hook을 설치한 경우, 그 hook이 `UserPromptSubmit`, `PreToolUse`, `Stop` 등에서 block하거나 오래 실행될 수 있다.

특히 `UserPromptSubmit` hook은 LidGuard start와 같은 event에서 실행된다. Codex hook dispatcher는 matching handlers를 병렬 실행하므로, 다른 hook이 block하더라도 LidGuard hook이 이미 start IPC를 보낼 수 있다.

가능한 문제:

1. `UserPromptSubmit`이 발생한다.
2. LidGuard hook이 start를 보낸다.
3. 다른 hook이 block 또는 stop을 만든다.
4. Codex turn은 모델 실행까지 가지 않거나 중단된다.
5. `Stop`은 발생하지 않을 수 있다.
6. LidGuard session만 active로 남을 수 있다.

현재 hook만으로 가능한 직접 대응:

- LidGuard가 다른 hook의 결과를 알 수 없다.
- hook이 병렬 실행되므로 LidGuard hook을 뒤에 배치하는 것만으로 해결하기 어렵다.
- hook command timeout이 있어 영구 대기는 아니지만 기본 600초까지 지연될 수 있다.

가능한 완화:

- `hook-status`에서 LidGuard 외 hook 존재 여부를 진단한다.
- LidGuard 자체 hook은 no-prompt/no-blocking 원칙을 지킨다.
- start 직후 Codex process watcher를 반드시 붙인다.
- closed-lid active-session timeout을 둔다.

참고 소스:

- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\events\user_prompt_submit.rs`
- `C:\Users\kck41\AppData\Local\Temp\openai-codex-main-for-lidguard-analysis\codex-rs\hooks\src\engine\dispatcher.rs`

### Stop hook block에 따른 premature cleanup

이 항목은 "노트북이 계속 깨어 있음" 소프트락은 아니다. 하지만 Codex provider 설계에서 별도로 조심해야 한다.

Codex `Stop` hook은 다른 hook이 block하면 continuation prompt를 만들고 모델이 계속 진행할 수 있다. LidGuard가 `Stop`을 받자마자 protection을 해제하면 실제 Codex 작업은 계속되는데 LidGuard는 꺼져 있는 상태가 될 수 있다.

현재 hook만으로 가능한 직접 대응:

- LidGuard hook은 다른 `Stop` hook outcome을 알 수 없다.
- `Stop` event 하나만 보고 "Codex가 완전히 끝났다"고 확정하기 어렵다.

가능한 완화:

- process watcher는 계속 유지하되, `Stop` cleanup 정책은 현행처럼 단순하게 둘지 검토한다.
- app-server 상태 감시가 가능하면 `Stop` 이후에도 thread가 다시 active가 되는지 확인한다.
- 문서에는 `Stop`은 turn 종료 신호이지 전체 프로세스 종료 신호가 아님을 명시한다.

## under-development 기능별 선제 대응 판단

### 요약 표

| 기능 | 현재 stage/default | 소프트락 관련성 | 선제 대응 판단 |
| --- | --- | --- | --- |
| `RequestPermissionsTool` | UnderDevelopment / default off | `RequestPermissions` 대기 생성 가능 | 별도 hook 구현은 보류. source tracking과 runtime timeout/app-server 감시로 대비 |
| `DefaultModeRequestUserInput` | UnderDevelopment / default off | Default mode에서도 `request_user_input` 허용 가능 | 중요. 다만 Plan mode에서 이미 같은 대기 구조가 있으므로 closed-lid timeout은 선제 가치 있음 |
| `SkillEnvVarDependencyPrompt` | UnderDevelopment / default off | skill env var 입력 prompt 생성 | 낮음. 같은 `RequestUserInput` 완화책으로 커버 |
| `SkillMcpDependencyInstall` | Stable / default on | missing MCP server install prompt 생성 가능 | 현재 기능으로 취급. 빈도는 낮지만 `RequestUserInput` 계열에 포함 |
| `ToolCallMcpElicitation` | Stable / default on | MCP tool approval UI가 elicitation으로 갈 수 있음 | 현재 기능으로 취급. 단 tool approval은 `PermissionRequest`가 먼저 실행됨 |
| `ToolSuggest` | Stable / default on | tool suggestion confirmation이 elicitation 사용 | 현재 기능으로 취급. hook-only 직접 대응 불가 |
| `PluginHooks` | UnderDevelopment / default off | third-party hook prompt/block 가능성 증가 | 보류. LidGuard 외 hook 진단과 문서화 우선 |
| `MultiAgentV2`, `SpawnCsv` | UnderDevelopment / default off | delegated user input/permission forwarding 가능성 | 후순위. multi-agent 지원 범위 확장 시 재검증 |
| `EnableMcpApps` | UnderDevelopment / default off | MCP/app surface 증가 | 직접 대응은 보류. MCP elicitation 정책으로 간접 대비 |

### RequestUserInput 관련 under-development

`DefaultModeRequestUserInput`은 under-development이고 default off다. 하지만 `RequestUserInput` 자체는 Plan mode에서 이미 의미가 있고, skill dependency prompt 같은 내부 경로에서도 쓰인다.

따라서 대응 판단은 다음과 같다.

- "Default mode까지 열릴 때까지 아무것도 안 해도 된다"는 판단은 위험하다.
- hook-only로 직접 응답할 방법은 없으므로, 별도 Codex hook 구현을 선제적으로 만들 수는 없다.
- closed-lid active-session timeout은 지금부터 설계해도 가치가 있다.
- app-server status integration을 검토한다면 `WaitingOnUserInput`을 1순위 신호로 봐야 한다.

### RequestPermissionsTool

`RequestPermissionsTool`은 under-development이고 default off다.

선제 대응 판단:

- 당장 v1 필수 구현 대상은 아니다.
- 하지만 기능이 stable/default-on이 되면 `PermissionRequest` guard만으로는 충분하지 않다.
- runtime timeout 또는 app-server `WaitingOnApproval` 감시가 있으면 별도 구현 없이 자연스럽게 완화된다.
- release/source tracking 항목으로 남기는 것이 좋다.

### MCP elicitation

`ToolCallMcpElicitation`은 stable이고 default on이다. MCP server elicitation 자체도 현재 소스에 존재한다.

선제 대응 판단:

- under-development가 아니므로 현재 위험으로 봐야 한다.
- 다만 MCP tool approval은 `PermissionRequest`를 먼저 거치므로 현재 guard가 상당 부분 방어한다.
- `tool_suggest`와 MCP server initiated elicitation은 hook-only로 직접 처리하기 어렵다.
- app-server status integration 또는 closed-lid timeout의 주요 근거로 봐야 한다.

### PluginHooks와 third-party hook

`PluginHooks`는 under-development이고 default off다. 하지만 사용자가 직접 설정한 command hook은 이미 존재할 수 있다.

선제 대응 판단:

- LidGuard가 다른 hook prompt를 대신 처리하는 것은 현실적이지 않다.
- `hook-status`에서 LidGuard 외 hook 존재 여부를 보여주는 진단이 더 적절하다.
- LidGuard 자체 hook이 prompt를 만들지 않는 원칙은 계속 유지한다.

## 권장 구현 우선순위

### 1. Codex hook event 기준 업데이트 [구현됨]

현재 Codex source 기준으로는 `SessionEnd` hook event가 보이지 않는다.

권장:

- Codex provider 문서와 installer/status logic에서 `SessionEnd` 전제를 재검토한다.
- 현재 source 기준 필수 stop event는 `Stop`으로 보는 것이 안전하다.
- 향후 Codex가 `SessionEnd`를 추가하면 다시 반영한다.

구현 메모:

- 2026-04-29 기준 이 항목은 반영됐다.
- 현재 LidGuard는 Codex `Stop`을 필수 stop hook으로 보고, `SessionEnd`는 선택적 호환 hook으로만 취급한다.
- Codex hook parser는 `SessionEnd`가 실제로 들어오면 계속 stop trigger로 처리하지만, installer/status/snippet은 더 이상 이를 필수로 요구하지 않는다.

### 2. closed-lid active-session timeout 설계

hook-only 방식으로는 `RequestUserInput`, `RequestPermissions`, MCP `ElicitationRequest`에 직접 답할 수 없다.

가장 현실적인 공통 완화책은 runtime의 closed-lid timeout이다.

권장 정책:

- active session 중 lid closed를 감지한다.
- `PermissionRequest`처럼 즉시 직접 결정 가능한 hook은 기존대로 처리한다.
- hook으로 처리할 수 없는 대기 상태를 위해 grace period를 둔다.
- grace period 이후에도 session이 active이고 process가 살아 있으면 softlock 의심 상태로 기록한다.
- 설정에 따라 protection 해제, cleanup, sleep/hibernate를 수행한다.

이 정책은 사용자 과실로 볼 수 있는 "prompt가 뜬 뒤 lid를 닫은 경우"까지 포함해, 노트북이 가방 안에서 계속 깨어 있는 문제를 완화한다.

### 3. app-server status integration 검토

가능하다면 hook보다 더 정확한 방법은 Codex app-server의 thread status를 보는 것이다.

관심 신호:

- `ThreadStatus::Active`
- `ThreadActiveFlag::WaitingOnApproval`
- `ThreadActiveFlag::WaitingOnUserInput`

이 경로가 가능하면 다음을 구분할 수 있다.

- 실제로 모델/도구가 진행 중인 active 상태
- 사용자의 승인/입력을 기다리는 active 상태
- idle 또는 unloaded 상태

다만 현재 LidGuard의 hook-facing CLI 모델과는 별도 integration이므로 v1 즉시 구현 대상인지 별도 판단이 필요하다.

### 4. under-development 기능 tracking

다음 기능은 Codex 업데이트 때 다시 확인해야 한다.

- `RequestPermissionsTool`
- `DefaultModeRequestUserInput`
- `SkillEnvVarDependencyPrompt`
- `PluginHooks`
- `MultiAgentV2`
- `SpawnCsv`
- `EnableMcpApps`

특히 `RequestPermissionsTool` 또는 `DefaultModeRequestUserInput`이 stable/default-on으로 바뀌면 `PermissionRequest` guard만으로는 부족하다는 문서를 업데이트해야 한다.

### 5. third-party hook 진단

LidGuard 외 hook이 있으면 start/stop 흐름이 왜곡될 수 있다.

권장:

- `hook-status`가 LidGuard managed hook 외 다른 hook 존재 여부를 표시한다.
- 다른 hook이 block하거나 오래 실행하면 LidGuard가 대신 응답할 수 없다고 안내한다.
- LidGuard 자체 hook은 계속 no-prompt/no-blocking을 유지한다.

## 요약

`PermissionRequest` guard는 Codex 소프트락 대응의 핵심이다. 일반 tool approval, network approval, MCP tool approval의 상당 부분은 이 경로로 커버된다.

현재까지 이 메모 기준으로 실제 구현까지 반영된 추가 항목은 Codex hook event 기준 업데이트다. 반면 closed-lid active-session timeout, Codex app-server status integration, third-party hook 진단 강화는 아직 미구현이다.

하지만 `PermissionRequest`만으로는 충분하지 않다. 현재 Codex source에는 `RequestUserInput`, `RequestPermissions`, MCP `ElicitationRequest`, `DynamicToolCallRequest`처럼 `PermissionRequest` hook을 거치지 않고 응답을 기다리는 경로가 있다.

프로그램이 hook으로 직접 커버할 수 있는 것은 주로 `PermissionRequest`와 정상 `Stop` cleanup이다. 프로그램이 hook-only로 직접 커버하기 어려운 것은 `RequestUserInput`, `RequestPermissions`, MCP server elicitation, dynamic tool response, third-party hook block이다.

under-development 기능 중 당장 가장 주의할 것은 `DefaultModeRequestUserInput`과 `RequestPermissionsTool`이다. 둘 다 default off이므로 즉시 별도 hook 구현 대상은 아니지만, 기능이 켜지면 `PermissionRequest` guard 밖의 대기 상태가 늘어난다. 반면 MCP elicitation 관련 일부 경로는 이미 stable/default-on 영역이므로 현재 위험으로 봐야 한다.

현실적인 다음 방어선은 Codex hook을 더 늘리는 것이 아니라 LidGuard runtime의 closed-lid active-session timeout 또는 Codex app-server status integration이다. 특히 `WaitingOnApproval`/`WaitingOnUserInput` 상태를 볼 수 있다면 hook-only보다 정확한 softlock 판단이 가능하다.
