---
name: lidguard-provider-integrations
description: "LidGuard provider integration reference. Use when working on Provider MCP, generic Provider MCP, Codex CLI hooks, Claude Code hooks, GitHub Copilot CLI hooks, OpenCode hooks, hook installation/status/removal, MCP registration, provider-specific payloads, or deployment notes."
---

# LidGuard Provider Integrations

## AgentProvider Enum

- File: `LidGuard/Sessions/AgentProvider.cs`.
- Enum ordering: `Unknown = -1`, `Codex = 0`, then `Claude`, `GitHubCopilot`, and `OpenCode` in increasing order, then `Custom`, then `Mcp`.
- When adding a new named provider, insert it **immediately before `Custom`** with the next consecutive integer value.
- The `[JsonConverter(typeof(JsonStringEnumConverter<AgentProvider>))]` attribute means all serialization uses string names, so the integer values are only for ordering and do not affect persistence. Renumbering existing members is safe as long as every reference uses the named enum member.

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
- Ask-before-sleep reply continuation is not implemented for Provider MCP and must never be described as supported there.

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
- Codex may require `features.hooks = true`.
- Codex MCP registration delegates to `codex mcp add/remove` and writes a global stdio server entry named `lidguard`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PermissionRequest`, and `Stop`; `SessionEnd` is optional and shown separately when present.
- `codex-hook` reads Codex hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider codex`.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count. When the lid is closed and the visible display monitor count is `0`, Deny and Allow return structured decisions from `LidGuardSettings.ClosedLidPermissionRequestDecision`; Ask marks the session soft-locked with reason `closed_lid_permission_request_ask` and returns empty stdout so Codex's normal approval prompt continues.
- For `Stop`, and for `SessionEnd` when a Codex build emits it, it sends internal `stop --provider codex`.
- Only the `Stop` event may request ask-before-sleep reply continuation. `SessionEnd` remains a plain stop path. When Codex provides `stop_hook_active = true`, LidGuard repeats or skips the next reply wait according to `RepeatClosedLidStopFollowUp`; explain this to users as "ask again next time the continued work tries to finish." If repeating is enabled, LidGuard waits through `postStopSuspendDelaySeconds` first and starts the follow-up webhook only afterward. If repeating is disabled, the continued Stop should also skip the normal post-stop suspend delay and proceed as an immediate suspend attempt.
- Notification-driven soft-lock detection is currently unsupported for Codex because the current public hook surface does not expose a comparable `Notification` event. LidGuard instead supports Codex `request_user_input` soft-locking from transcript JSONL. Future hook-level support can be added if Codex exposes notification or machine-readable pending-state hooks later.
- Because Codex lacks a notification-style soft-lock clear signal and comparable tool activity hooks, LidGuard records `transcript_path` from `UserPromptSubmit` and monitors the transcript JSONL through the shared transcript monitor. If recent `response_item` records include a `payload.type = function_call` with `payload.name = request_user_input`, LidGuard tracks that `payload.call_id` as pending and marks the session soft-locked with reason `codex_transcript_request_user_input_pending` until a matching `payload.type = function_call_output` appears. When no stop or pending `request_user_input` signal is detected, transcript JSONL length growth or `LastWriteTimeUtc` advancement is treated as Codex provider activity, refreshing `LastActivityAt` and clearing the current soft-lock state through the standard activity path. If `transcript_path` is missing, LidGuard falls back to a unique `~/.codex/sessions` transcript match by session id. The transcript monitor combines file-system change notifications with a short metadata polling fallback. A latest-record `turn_aborted` event is handled as an interrupted turn and stops the tracked Codex session rather than refreshing activity.
- Codex hook payloads do not provide a stable parent process id, so LidGuard resolves the watched process from the hook process ancestry when `WatchParentProcess` is enabled. Codex CLI, node/npm/npx Codex wrappers, and Codex App `app-server` are valid owners. Working directory is metadata only and must not be used to find a watched process. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- Codex `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid Deny/Allow decisions; when Ask is configured, the lid is open, lid state is unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Codex task when a runtime request fails.
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
- Windows hook config uses `shell = "powershell"` and Linux/macOS hook config uses `shell = "bash"` in Claude `settings.json` command hooks.
- Based on analysis of a locally retained Claude Code source snapshot, command hooks treat `exit code 0` with empty stdout as a successful no-op, while non-empty stdout may be interpreted as hook JSON or plain-text output depending on the execution path.
- Based on the same local source snapshot analysis, `PermissionRequest` only becomes a programmatic allow/deny when the hook returns structured JSON with `hookSpecificOutput.decision`; LidGuard also sets `interrupt: true` on those closed-lid decisions so Claude stops the interactive permission path immediately. Empty stdout keeps the normal permission flow.
- `claude-hook` reads Claude hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider claude` with `transcript_path` when Claude provides one, except when the prompt is a Claude `<task-notification>` payload. Task notifications are provider work completion signals and must not start or refresh a new Claude session.
- For `PreToolUse`, `PostToolUse`, and non-interrupt `PostToolUseFailure`, it records provider activity and clears the current session soft-lock state for non-`AskUserQuestion` tools.
- For `PostToolUse`, it also tracks Claude background work when `tool_input.run_in_background = true` for `Bash`, `PowerShell`, `Agent`, or legacy `Task`, and when `Monitor` starts.
- For `SubagentStart` and `SubagentStop`, it tracks active Claude subagents in hook-local state and records provider activity.
- For `TaskCreated`, it tracks Claude-created tasks as active provider work. For `TaskCompleted`, it treats matching background task identifiers as completed and records provider activity. Background task completion is also reconciled from Claude transcript `task_notification` records, XML-like `<task-notification>` payloads delivered through `UserPromptSubmit`, and explicit `TaskStop` tool use clears matching tracked work. Queued Claude `queue-operation` task-notification content is not enough to clear pending work because Claude may still need to deliver that notification through a follow-up turn.
- For `PostToolUseFailure` with `is_interrupt: true`, it sends internal `stop --provider claude` immediately.
- For `Elicitation`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a structured `cancel` only when the lid is closed and the visible display monitor count is `0`.
- For `Notification`, `permission_prompt` and `elicitation_dialog` mark the session soft-locked, while `elicitation_complete` and `elicitation_response` clear the current soft-lock state.
- Claude transcript JSONL changes are monitored through the same shared transcript monitor used by Codex. If `transcript_path` is missing, LidGuard falls back to a unique `~/.claude/projects` transcript match by session id; a latest user text marker of `[Request interrupted by user]` or `[Request interrupted by user for tool use]` stops the tracked Claude session instead of refreshing activity.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count. When the lid is closed and the visible display monitor count is `0`, Deny and Allow return a Claude-specific structured decision with `interrupt: true` from `LidGuardSettings.ClosedLidPermissionRequestDecision`; Ask marks the session soft-locked with reason `closed_lid_permission_request_ask` and returns empty stdout so Claude's normal permission flow continues.
- For Claude Code on web/desktop cloud sessions where LidGuard cannot read a local transcript JSONL file, transcript length growth cannot clear a soft-lock. In that case, LidGuard can only clear the soft-lock through observable activity/tool hooks, notification resolution hooks, session stop/update paths, or manual/runtime cleanup.
- When working on Claude Code-related setup, support, or documentation, explicitly and strongly warn the user not to use third-party prompt-style hooks alongside LidGuard. Explain that LidGuard must only answer its own closed-lid `PermissionRequest` and `Elicitation` paths and must not be presented as able to answer or proxy third-party hook prompts.
- For `Stop`, it first checks tracked Claude subagents and background tasks. If any remain active, it still sends internal `stop --provider claude`, but marks the request as pending provider work so the runtime keeps the session active, preserves keep-awake behavior, cancels any pending suspend, and skips `PostSessionEnd`/`PreSuspend` behavior. `SubagentStop`, `TaskCompleted`, `TaskStop`, transcript `task_notification`, and `UserPromptSubmit` `<task-notification>` signals clear matching tracked work but do not send the final internal stop immediately; the next Claude `Stop` after Claude finishes processing those completion signals enters the normal final stop path.
- Only Claude `Stop` may request ask-before-sleep reply continuation. `StopFailure` and `SessionEnd` must not do so. When Claude provides `stop_hook_active = true`, LidGuard repeats or skips the next reply wait according to `RepeatClosedLidStopFollowUp`; explain this to users as "ask again next time the continued work tries to finish." If repeating is enabled, LidGuard waits through `postStopSuspendDelaySeconds` first and starts the follow-up webhook only afterward. If repeating is disabled, the continued Stop should also skip the normal post-stop suspend delay and proceed as an immediate suspend attempt.
- For `StopFailure` and `SessionEnd`, it sends internal `stop --provider claude`.
- The analyzed Claude hook input provides `session_id` and `cwd`, but not a stable parent process id in the payload. LidGuard resolves the watched process from hook process ancestry when `WatchParentProcess` is enabled, and keeps `cwd` only as status/log/transcript/webhook metadata. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- Claude `Elicitation` exits successfully with structured JSON stdout only for effective closed-lid `cancel`; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.
- Claude `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid Deny/Allow decisions; when Ask is configured, the lid is open, lid state is unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.

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
- Managed GitHub Copilot CLI command hook entries use `powershell` on Windows and `bash` on Linux/macOS.
- `hook-install` and `hook-status` require `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, `permissionRequest`, `agentStop`, `subagentStart`, `subagentStop`, `errorOccurred`, and a filtered `notification` hook.
- Because official Copilot CLI docs allow `agentStop` hooks to return `decision: "block"` with a `reason` continuation prompt, `hook-install` and `hook-status` should warn when non-LidGuard `agentStop` hooks are present.
- Based on the official Copilot CLI hooks documentation, passive hooks such as `sessionStart` may be implemented as logging-only shell commands with no JSON output, so `exit code 0` with empty stdout is a valid no-op pattern for non-decision hooks.
- Based on the official hooks configuration reference, `preToolUse` output JSON is optional and omitting output allows the tool by default, so structured JSON should only be returned when LidGuard intentionally wants to influence a hook decision.
- Even if a future GitHub Copilot CLI hook output ends up looking similar to another provider's current hook JSON, keep a dedicated GitHub Copilot CLI hook output type. Hook contracts are provider-specific and are not standardized across CLIs.
- `copilot-hook` takes the configured event name from the command line because camelCase GitHub Copilot CLI hook payloads do not consistently include the event name in stdin JSON.
- For `userPromptSubmitted`, it sends internal `start --provider copilot` with `transcriptPath` / `transcript_path` when Copilot provides one.
- For `permissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count. When the lid is closed and the visible display monitor count is `0`, Deny and Allow return a GitHub Copilot CLI decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` with `interrupt: true`; Ask marks the session soft-locked with reason `closed_lid_permission_request_ask` and returns empty stdout so Copilot's normal permission flow continues.
- For `preToolUse`, it does not stop the runtime; it denies `ask_user` only when the lid is closed and the visible display monitor count is `0`, so the agent cannot soft-lock waiting for user input that cannot be answered, and it clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it records tool completion activity and clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it also tracks GitHub Copilot CLI background work when `task` starts a background agent, when `bash` or `powershell` starts an async/detached shell, when `write_agent` resumes a background agent, and when `read_agent` returns a terminal idle/completed/failed state for a tracked background agent.
- For `subagentStart` and `subagentStop`, it tracks active GitHub Copilot CLI subagents in hook-local state and records provider activity.
- For `notification`, it marks the session soft-locked when GitHub Copilot CLI reports `permission_prompt` or `elicitation_dialog`; it treats `shell_completed`, `shell_detached_completed`, `agent_completed`, and `agent_idle` as background-work completion signals.
- For `agentStop` and `sessionEnd`, it first checks tracked GitHub Copilot CLI subagents and background tasks. If any remain active, it still sends internal `stop --provider copilot`, but marks the request as pending provider work so the runtime keeps the session active, preserves keep-awake behavior, cancels any pending suspend, and skips `PostSessionEnd`/`PreSuspend` behavior. `subagentStop`, completion notification, `read_agent`, and session-state JSONL reconciliation clear matching tracked work but do not send the final internal stop immediately; the next GitHub Copilot CLI `agentStop` or `sessionEnd` after completion processing enters the normal final stop path.
- GitHub Copilot CLI ask-before-sleep reply continuation is allowed only for `agentStop` and the VS Code-compatible `Stop` alias. `sessionEnd` must remain a plain stop path. When GitHub Copilot reports `stopHookActive`, LidGuard repeats or skips the next reply wait according to `RepeatClosedLidStopFollowUp`; explain this to users as "ask again next time the continued work tries to finish." If repeating is enabled, LidGuard waits through `postStopSuspendDelaySeconds` first and starts the follow-up webhook only afterward. If repeating is disabled, the continued Stop should also skip the normal post-stop suspend delay and proceed as an immediate suspend attempt.
- GitHub Copilot CLI session-state JSONL changes are monitored through the shared transcript monitor. If `transcriptPath` / `transcript_path` is missing, LidGuard falls back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`; a latest top-level `type` of `abort` stops the tracked Copilot session instead of refreshing activity. Other JSONL appends or `LastWriteTimeUtc` advancements refresh `LastActivityAt` with reason `github_copilot_session_event_activity_detected` and clear the current soft-lock state.
- For `sessionStart` and `errorOccurred`, it records telemetry only.
- GitHub Copilot CLI hook input currently does not provide a stable parent process id in the documented payloads. LidGuard resolves the watched process from hook process ancestry when `WatchParentProcess` is enabled, accepting Copilot CLI, `gh ... copilot`, and node/npm/npx Copilot wrappers. Working directory remains metadata only. Watched parent process exit and orphan cleanup are cancel paths that suppress `PostSessionEnd` and any new `PreSuspend` webhook they would schedule.
- GitHub Copilot CLI `permissionRequest` exits successfully with structured JSON stdout only for effective closed-lid Deny/Allow decisions; when Ask is configured, the lid is open, lid state is unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout so the normal permission flow continues.
- GitHub Copilot CLI `preToolUse` exits successfully with structured JSON stdout only for effective closed-lid `ask_user` denial; otherwise it exits successfully with empty stdout so normal tool handling continues.

Reference:

- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference
- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference

### OpenCode

- Provider enum: `AgentProvider.OpenCode`.
- Start event: `chat.message`.
- Permission decision event: `permission.ask`.
- Activity events: `tool.execute.before`, `tool.execute.after`, `permission.replied`, `question.replied`, `question.rejected`, `question.v2.replied`, and `question.v2.rejected`.
- Soft-lock events: `permission.asked`, `question.asked`, and `question.v2.asked`.
- Stop trigger events: `session.idle`, `session.status` when the status is `idle`, `session.deleted`, and `session.error`.
- `message.part.updated` is tracked internally by the managed plugin to cache the last visible assistant text (`part.type === "text"` only); it does not invoke the runtime activity hook. `reasoning` and `tool` parts are ignored for this cache.
- Stop trigger payloads include `lastAssistantMessage` populated from this cache, which LidGuard forwards to runtime as `LastAssistantMessage` and surfaces in session-end, pre-suspend, and stop-follow-up webhooks. The cache is cleared immediately after the stop event is sent so stale messages are not forwarded to subsequent stop events.
- Command path: `lidguard opencode-hook --event <event-name>` when the global tool is available on PATH, otherwise the current executable path plus `opencode-hook --event <event-name>`.
- Snippet command: `lidguard opencode-hooks plugin-js`.
- Install/status/remove commands: `lidguard hook-install --provider opencode`, `lidguard hook-status --provider opencode`, and `lidguard hook-remove --provider opencode`.
- MCP status/install/remove commands: `lidguard mcp-status opencode`, `lidguard mcp-install opencode`, and `lidguard mcp-remove opencode`.
- Default plugin path: `OPENCODE_CONFIG_DIR\plugins\lidguard.js` when `OPENCODE_CONFIG_DIR` is set, otherwise `%USERPROFILE%\.config\opencode\plugins\lidguard.js`.
- Default MCP configuration path: `OPENCODE_CONFIG` when set, otherwise `OPENCODE_CONFIG_DIR\opencode.json` if it exists, otherwise `OPENCODE_CONFIG_DIR\opencode.jsonc`.
- OpenCode MCP registration edits the global/user OpenCode config directly under the `mcp` object with a `lidguard` entry shaped as `{ "type": "local", "command": [<lidguard>, "mcp-server"], "enabled": true }`.
- OpenCode MCP status must not report the managed server as installed unless the entry is `type = local`, is not disabled, points at the current LidGuard executable, and contains `mcp-server` in the command array.
- `hook-install` writes a managed global OpenCode plugin file from `LidGuard/Assets/OpenCode/lidguard.js`; `hook-status` checks the managed plugin version marker so older generated plugins need update; `hook-remove` removes that managed plugin file when it becomes empty.
- When selected provider `all` is used for native hook management, treat an existing OpenCode config directory as enough to include OpenCode even if the `plugins` subdirectory does not exist yet.
- `opencode-hook` reads plugin JSON from stdin and uses the command-line `--event` value as the authoritative event name.
- For `chat.message`, it sends internal `start --provider opencode`.
- For `permission.ask`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count. When the lid is closed and the visible display monitor count is `0`, Deny and Allow return OpenCode-specific structured JSON with `status` of `deny` or `allow`; Ask marks the session soft-locked with reason `closed_lid_permission_request_ask` and returns empty stdout so OpenCode's normal permission flow continues. The observed OpenCode plugin type contract only exposes `output.status` for `permission.ask`, so do not promise or wire a deny message there unless a future OpenCode contract adds one.
- For activity events, it records provider activity and clears the current session soft-lock state.
- For soft-lock events, it marks the session soft-locked with the event name as the reason.
- For stop trigger events, it sends internal `stop --provider opencode`. Only `session.idle` and `session.status` with `idle` are treated as normal provider session ends.
- OpenCode ask-before-sleep reply continuation is not implemented because the current plugin event surface does not expose a verified blocking Stop-hook continuation contract.
- OpenCode plugin payloads do not provide a stable parent process id, so LidGuard resolves the watched process from hook process ancestry when `WatchParentProcess` is enabled, accepting OpenCode CLI and bun/node/npm/npx wrapper processes. Working directory remains metadata only.
- OpenCode `permission.ask` exits successfully with structured JSON stdout only for effective closed-lid Deny/Allow decisions; when Ask is configured, the lid is open, lid state is unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout so the normal permission flow continues.

## Operational Notes

- Existing Codex, Claude, GitHub Copilot, and OpenCode config should point directly to the intended `lidguard.exe` path after managed hook or MCP install.
- Windows WSL integration commands must install or inspect configuration inside the selected/default WSL distro while pointing hook, MCP, and Provider MCP commands back to the current Windows `lidguard.exe` through its `wslpath`-converted absolute path.
- WSL hook commands are `wsl-hook-status`, `wsl-hook-install`, and `wsl-hook-remove` with `--provider codex|claude|copilot|opencode|all` and optional `--distro <name>`.
- WSL hook snippet commands are `wsl-codex-hooks`, `wsl-claude-hooks`, `wsl-copilot-hooks`, and `wsl-opencode-hooks`; generated Claude and GitHub Copilot hook config must use the `bash` shell, not Windows `powershell`.
- Settings-triggered automatic WSL managed hook refresh must normalize `wsl.exe --list --quiet` output before using distro names, including removing NUL characters from UTF-16LE-style output, and an empty distro name must mean the default WSL distro rather than a named empty distro.
- WSL provider-specific MCP aliases are `wsl-codex-mcp-status/install/remove`, `wsl-claude-mcp-status/install/remove`, `wsl-copilot-mcp-status/install/remove`, and `wsl-opencode-mcp-status/install/remove`. The generic selected-provider forms are `wsl-mcp-status/install/remove [codex|claude|copilot|opencode|all]`.
- WSL generic Provider MCP direct JSON commands are `wsl-provider-mcp-status`, `wsl-provider-mcp-install`, and `wsl-provider-mcp-remove`, using WSL-side JSON config paths and a server entry that runs the Windows `lidguard.exe` WSL path.
- WSL hook status must treat a managed hook that points at an older `lidguard.exe` version path as needing update, matching MCP refresh behavior.
- When helping a user with Claude deployment or configuration, explicitly and strongly warn them not to rely on third-party prompt hooks with LidGuard. State that LidGuard can only make its own closed-lid permission or elicitation decisions and cannot safely respond on behalf of unrelated Claude hook prompts.
- If tests are added, prefer focused unit tests around Commons policy controllers and small integration-style tests around Windows service wrappers where safe.
