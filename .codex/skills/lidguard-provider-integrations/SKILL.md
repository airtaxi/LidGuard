---
name: lidguard-provider-integrations
description: "LidGuard provider integration reference. Use when working on Provider MCP, generic Provider MCP, Codex CLI hooks, Claude Code hooks, GitHub Copilot CLI hooks, hook installation/status/removal, MCP registration, provider-specific payloads, or deployment notes."
---

# LidGuard Provider Integrations

## Provider MCP Mapping

### Generic Provider MCP

- Provider enum: `AgentProvider.Mcp`.
- Provider sessions are distinguished by both `sessionId` and `providerName`.
- `provider_start_session` generates a stable Provider MCP `sessionId` by taking the first 8 lowercase hexadecimal characters from a new GUID and returns that value to the model.
- The model must keep reusing the exact `sessionId` returned by `provider_start_session` until the session is truly complete.
- Provider MCP install/remove/status commands are `lidguard provider-mcp-status --config <json-path>`, `lidguard provider-mcp-install --config <json-path> --provider-name <name>`, and `lidguard provider-mcp-remove --config <json-path>`.
- Provider MCP config is edited directly as JSON and does not reuse the Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration flows.
- Provider MCP server command: `lidguard provider-mcp-server --provider-name <name>`.
- Provider MCP start tool: `provider_start_session`.
- Provider MCP stop tool: `provider_stop_session`.
- Provider MCP soft-lock tools: `provider_set_soft_lock` and `provider_clear_soft_lock`.
- `provider_start_session` should be described to the model as a brand-new-session call that auto-generates the reusable `sessionId`.
- `provider_stop_session` should be described to the model as a pre-turn-end call only when the work is truly complete.
- `provider_set_soft_lock` should explain the soft-lock concept and instruct the model to call it before ending a turn that is about to wait for user input. The description must also explain that the tool cannot end the turn on the model's behalf.
- `provider_clear_soft_lock` should instruct the model to resume the earlier returned `sessionId` after the user replies, instead of minting a new session.
- Because all Provider MCP behavior depends on model compliance, do not promise or document it as guaranteed behavior.

## Provider Hook Mapping

### Codex CLI

- Start event: `UserPromptSubmit`.
- Permission decision event: `PermissionRequest`.
- Required stop event: `Stop`.
- Optional compatibility stop event: `SessionEnd` when a Codex build emits it.
- Command path: `lidguard codex-hook` when the global tool is available on PATH, otherwise the current executable path plus `codex-hook`.
- Snippet command: `lidguard codex-hooks config-toml`.
- Install/status/remove commands: `lidguard hook-install --provider codex`, `lidguard hook-status --provider codex`, and `lidguard hook-remove --provider codex`.
- MCP status/install/remove commands: `lidguard mcp-status codex`, `lidguard mcp-install codex`, and `lidguard mcp-remove codex`.
- Codex may require `features.codex_hooks = true`.
- Codex MCP registration delegates to `codex mcp add/remove` and writes a global stdio server entry named `lidguard`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PermissionRequest`, and `Stop`; `SessionEnd` is optional and shown separately when present.
- `codex-hook` reads Codex hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider codex`.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a structured allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`.
- For `Stop`, and for `SessionEnd` when a Codex build emits it, it sends internal `stop --provider codex`.
- Notification-driven soft-lock detection is currently unsupported for Codex because the current public hook surface does not expose a comparable `Notification` event. LidGuard instead supports Codex `request_user_input` soft-locking from transcript JSONL. Future hook-level support can be added if Codex exposes notification or machine-readable pending-state hooks later.
- Because Codex lacks a notification-style soft-lock clear signal and comparable tool activity hooks, LidGuard records `transcript_path` from `UserPromptSubmit` and monitors the transcript JSONL through the shared transcript monitor. If recent `response_item` records include a `payload.type = function_call` with `payload.name = request_user_input`, LidGuard tracks that `payload.call_id` as pending and marks the session soft-locked with reason `codex_transcript_request_user_input_pending` until a matching `payload.type = function_call_output` appears. When no stop or pending `request_user_input` signal is detected, transcript JSONL length growth or `LastWriteTimeUtc` advancement is treated as Codex provider activity, refreshing `LastActivityAt` and clearing the current soft-lock state through the standard activity path. If `transcript_path` is missing, LidGuard falls back to a unique `~/.codex/sessions` transcript match by session id. The transcript monitor combines file-system change notifications with a short metadata polling fallback. A latest-record `turn_aborted` event is handled as an interrupted turn and stops the tracked Codex session rather than refreshing activity.
- Codex hook payloads do not provide a stable parent process id, so LidGuard resolves the watched process from the hook process ancestry when `WatchParentProcess` is enabled. Codex CLI, node/npm/npx Codex wrappers, and Codex App `app-server` are valid owners. Working directory is metadata only and must not be used to find a watched process. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- Codex `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Codex task when a runtime request fails.
- This behavior is based on analyzing the `openai/codex` `codex-rs` hook source: `exit 0` with empty stdout is treated as a no-op success, while non-empty stdout may be parsed as hook JSON or interpreted as plain-text context depending on the event.

Reference:

- https://developers.openai.com/codex/hooks
- https://github.com/openai/codex

### Claude Code

- Start event: `UserPromptSubmit`.
- Activity telemetry events: `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `SubagentStart`, `SubagentStop`, `TaskCreated`, and `TaskCompleted`.
- Permission decision event: `PermissionRequest`.
- MCP elicitation event: `Elicitation`.
- Soft-lock notification event: `Notification`.
- Stop events: `Stop`, `StopFailure`, `SessionEnd`.
- Command path: `lidguard claude-hook` when the global tool is available on PATH, otherwise the current executable path plus `claude-hook`.
- Snippet command: `lidguard claude-hooks settings-json`.
- Install/status/remove commands: `lidguard hook-install --provider claude`, `lidguard hook-status --provider claude`, and `lidguard hook-remove --provider claude`.
- MCP status/install/remove commands: `lidguard mcp-status claude`, `lidguard mcp-install claude`, and `lidguard mcp-remove claude`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `SubagentStart`, `SubagentStop`, `TaskCreated`, `TaskCompleted`, `Stop`, `StopFailure`, `Elicitation`, `PermissionRequest`, `Notification`, and `SessionEnd`.
- Default config path: `CLAUDE_CONFIG_DIR\settings.json` when `CLAUDE_CONFIG_DIR` is set, otherwise `%USERPROFILE%\.claude\settings.json`.
- Claude MCP registration uses the user-scope global config at `%USERPROFILE%\.claude.json` and delegates to `claude mcp add/remove --scope user`.
- Windows hook config uses `shell = "powershell"` in Claude `settings.json` command hooks.
- Based on analysis of a locally retained Claude Code source snapshot, command hooks treat `exit code 0` with empty stdout as a successful no-op, while non-empty stdout may be interpreted as hook JSON or plain-text output depending on the execution path.
- Based on the same local source snapshot analysis, `PermissionRequest` only becomes a programmatic allow/deny when the hook returns structured JSON with `hookSpecificOutput.decision`; LidGuard also sets `interrupt: true` on those closed-lid decisions so Claude stops the interactive permission path immediately. Empty stdout keeps the normal permission flow.
- `claude-hook` reads Claude hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider claude` with `transcript_path` when Claude provides one, except when the prompt is a Claude `<task-notification>` payload. Task notifications are provider work completion signals and must not start or refresh a new Claude session.
- For `PreToolUse`, `PostToolUse`, and non-interrupt `PostToolUseFailure`, it records provider activity and clears the current session soft-lock state for non-`AskUserQuestion` tools.
- For `PostToolUse`, it also tracks Claude background work when `tool_input.run_in_background = true` for `Bash`, `PowerShell`, `Agent`, or legacy `Task`, and when `Monitor` starts.
- For `SubagentStart` and `SubagentStop`, it tracks active Claude subagents in hook-local state and records provider activity.
- For `TaskCreated`, it tracks Claude-created tasks as active provider work. For `TaskCompleted`, it treats matching background task identifiers as completed and records provider activity. Background task completion is also reconciled from Claude transcript `task_notification` records, XML-like `<task-notification>` payloads delivered through `UserPromptSubmit`, and explicit `TaskStop` tool use clears matching tracked work.
- For `PostToolUseFailure` with `is_interrupt: true`, it sends internal `stop --provider claude` immediately.
- For `Elicitation`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a structured `cancel` only when the lid is closed and the visible display monitor count is `0`.
- For `Notification`, `permission_prompt` and `elicitation_dialog` mark the session soft-locked, while `elicitation_complete` and `elicitation_response` clear the current soft-lock state.
- Claude transcript JSONL changes are monitored through the same shared transcript monitor used by Codex. If `transcript_path` is missing, LidGuard falls back to a unique `~/.claude/projects` transcript match by session id; a latest user text marker of `[Request interrupted by user]` or `[Request interrupted by user for tool use]` stops the tracked Claude session instead of refreshing activity.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a Claude-specific structured allow/deny decision with `interrupt: true` from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`.
- When working on Claude Code-related setup, support, or documentation, explicitly and strongly warn the user not to use third-party prompt-style hooks alongside LidGuard. Explain that LidGuard must only answer its own closed-lid `PermissionRequest` and `Elicitation` paths and must not be presented as able to answer or proxy third-party hook prompts.
- For `Stop`, it first checks tracked Claude subagents and background tasks. If any remain active, it still sends internal `stop --provider claude`, but marks the request as pending provider work so the runtime keeps the session active, preserves keep-awake behavior, cancels any pending suspend, and skips `PostSessionEnd`/`PreSuspend` behavior. Once `SubagentStop`, `TaskCompleted`, `TaskStop`, transcript `task_notification`, or `UserPromptSubmit` `<task-notification>` signals clear the last pending work item, `claude-hook` sends a final internal `stop --provider claude` with the original session-end reason.
- For `StopFailure` and `SessionEnd`, it sends internal `stop --provider claude`.
- The analyzed Claude hook input provides `session_id` and `cwd`, but not a stable parent process id in the payload. LidGuard resolves the watched process from hook process ancestry when `WatchParentProcess` is enabled, and keeps `cwd` only as status/log/transcript/webhook metadata. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- Claude `Elicitation` exits successfully with structured JSON stdout only for effective closed-lid `cancel`; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.
- Claude `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.

Reference:

- https://code.claude.com/docs/en/hooks

### GitHub Copilot CLI

- Start event: `userPromptSubmitted`.
- Stop events: `agentStop`, `sessionEnd`, and session-state JSONL `abort`.
- Closed-lid permission decision event: `permissionRequest`.
- Closed-lid ask-user guard event: `preToolUse` when `toolName` is `ask_user`.
- Activity and work tracking events: `postToolUse`, `subagentStart`, and `subagentStop`.
- Soft-lock and work-completion notification event: `notification` with `notification_type` / `notificationType` of `permission_prompt`, `elicitation_dialog`, `shell_completed`, `shell_detached_completed`, `agent_completed`, or `agent_idle`.
- Telemetry-only events: `sessionStart` and `errorOccurred`.
- Command path: `lidguard copilot-hook --event <event-name>` when the global tool is available on PATH, otherwise the current executable path plus `copilot-hook --event <event-name>`.
- Snippet command: `lidguard copilot-hooks config-json`.
- Install/status/remove commands: `lidguard hook-install --provider copilot`, `lidguard hook-status --provider copilot`, and `lidguard hook-remove --provider copilot`.
- MCP status/install/remove commands: `lidguard mcp-status copilot`, `lidguard mcp-install copilot`, and `lidguard mcp-remove copilot`.
- Default global config path: `COPILOT_HOME\hooks\lidguard-copilot-cli.json` when `COPILOT_HOME` is set, otherwise `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`.
- GitHub Copilot CLI MCP registration delegates to `copilot mcp add/remove` and uses the user config file `%USERPROFILE%\.copilot\mcp-config.json`.
- GitHub Copilot CLI also supports inline user hooks in `~/.copilot/settings.json`; repository hooks in `.github/hooks/` and repository Copilot settings are loaded alongside user hooks, so `hook-install` and `hook-status` inspect those sources for conflicts.
- `hook-install` and `hook-status` require `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, `permissionRequest`, `agentStop`, `subagentStart`, `subagentStop`, `errorOccurred`, and a filtered `notification` hook.
- Because official Copilot CLI docs allow `agentStop` hooks to return `decision: "block"` with a `reason` continuation prompt, `hook-install` and `hook-status` should warn when non-LidGuard `agentStop` hooks are present.
- Based on the official Copilot CLI hooks documentation, passive hooks such as `sessionStart` may be implemented as logging-only shell commands with no JSON output, so `exit code 0` with empty stdout is a valid no-op pattern for non-decision hooks.
- Based on the official hooks configuration reference, `preToolUse` output JSON is optional and omitting output allows the tool by default, so structured JSON should only be returned when LidGuard intentionally wants to influence a hook decision.
- Even if a future GitHub Copilot CLI hook output ends up looking similar to another provider's current hook JSON, keep a dedicated GitHub Copilot CLI hook output type. Hook contracts are provider-specific and are not standardized across CLIs.
- `copilot-hook` takes the configured event name from the command line because camelCase GitHub Copilot CLI hook payloads do not consistently include the event name in stdin JSON.
- For `userPromptSubmitted`, it sends internal `start --provider copilot` with `transcriptPath` / `transcript_path` when Copilot provides one.
- For `permissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a GitHub Copilot CLI allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`, and it includes `interrupt: true`.
- For `preToolUse`, it does not stop the runtime; it denies `ask_user` only when the lid is closed and the visible display monitor count is `0`, so the agent cannot soft-lock waiting for user input that cannot be answered, and it clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it records tool completion activity and clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it also tracks GitHub Copilot CLI background work when `task` starts a background agent, when `bash` or `powershell` starts an async/detached shell, when `write_agent` resumes a background agent, and when `read_agent` returns a terminal idle/completed/failed state for a tracked background agent.
- For `subagentStart` and `subagentStop`, it tracks active GitHub Copilot CLI subagents in hook-local state and records provider activity.
- For `notification`, it marks the session soft-locked when GitHub Copilot CLI reports `permission_prompt` or `elicitation_dialog`; it treats `shell_completed`, `shell_detached_completed`, `agent_completed`, and `agent_idle` as background-work completion signals.
- For `agentStop` and `sessionEnd`, it first checks tracked GitHub Copilot CLI subagents and background tasks. If any remain active, it still sends internal `stop --provider copilot`, but marks the request as pending provider work so the runtime keeps the session active, preserves keep-awake behavior, cancels any pending suspend, and skips `PostSessionEnd`/`PreSuspend` behavior. Once `subagentStop`, completion notification, `read_agent`, or session-state JSONL reconciliation clears the last pending work item, `copilot-hook` sends a final internal `stop --provider copilot` with the original session-end reason.
- GitHub Copilot CLI session-state JSONL changes are monitored through the shared transcript monitor. If `transcriptPath` / `transcript_path` is missing, LidGuard falls back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`; a latest top-level `type` of `abort` stops the tracked Copilot session instead of refreshing activity. Other JSONL appends or `LastWriteTimeUtc` advancements refresh `LastActivityAt` with reason `github_copilot_session_event_activity_detected` and clear the current soft-lock state.
- For `sessionStart` and `errorOccurred`, it records telemetry only.
- GitHub Copilot CLI hook input currently does not provide a stable parent process id in the documented payloads. LidGuard resolves the watched process from hook process ancestry when `WatchParentProcess` is enabled, accepting Copilot CLI, `gh ... copilot`, and node/npm/npx Copilot wrappers. Working directory remains metadata only. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- GitHub Copilot CLI `permissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout so the normal permission flow continues.
- GitHub Copilot CLI `preToolUse` exits successfully with structured JSON stdout only for effective closed-lid `ask_user` denial; otherwise it exits successfully with empty stdout so normal tool handling continues.

Reference:

- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference
- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference

## Operational Notes

- Existing Codex and Claude config should point directly to the intended `lidguard.exe` path after `hook-install`.
- When helping a user with Claude deployment or configuration, explicitly and strongly warn them not to rely on third-party prompt hooks with LidGuard. State that LidGuard can only make its own closed-lid permission or elicitation decisions and cannot safely respond on behalf of unrelated Claude hook prompts.
- If tests are added, prefer focused unit tests around Commons policy controllers and small integration-style tests around Windows service wrappers where safe.
