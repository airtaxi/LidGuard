---
name: lidguard-session-runtime
description: "LidGuard session lifecycle reference. Use when working on active session policy, soft-locking, inactive timeouts, transcript monitoring, process exit watching, runtime cleanup, session-end webhook semantics, watchdogs, or orphan cleanup."
---

# LidGuard Session Runtime

## Process Exit Watcher

Hook stop events may be missed, so LidGuard also watches the agent process.

- Prefer a provided parent process id when hooks can supply one.
- Managed Codex, Claude Code, and GitHub Copilot CLI hooks should resolve a watched process id from the hook process ancestry on `UserPromptSubmit` / `userPromptSubmitted` when `WatchParentProcess` is enabled.
- Working directory must not be used to auto-resolve watched processes. Keep it only for status, logs, transcript fallback, and webhook payload metadata.
- If neither an explicit parent process id nor a hook ancestry owner process id is available, start or update the session with `process=none`.
- On Windows, open the target process with synchronize/query rights and wait with `WaitForSingleObject`.
- On Windows, read hook process ancestry with CsWin32/WDK `NtQueryInformationProcess(ProcessBasicInformation)`.
- On Linux, read hook process ancestry with `/proc/<pid>/stat`, `/proc/<pid>/comm`, and `/proc/<pid>/cmdline`.
- On macOS, read hook process ancestry from `ps -axo pid=,ppid=,comm=,command=`.
- On Linux and macOS, use `Process.GetProcessById().WaitForExitAsync()` for process exit watching.
- Treat the first cleanup signal as authoritative; later stop/watchdog events for the same session should be harmless.
- If a provider launches a short-lived wrapper that exits before the real agent, prefer provider-specific process selection rather than broadening the generic resolver.
- Watched parent process exit and orphan cleanup are cancel cleanup paths, not provider-reported normal session ends. They must suppress `PostSessionEnd` and any new `PreSuspend` webhook they would otherwise schedule, while preserving any pending suspend that was already scheduled or running.

## Active Session Policy

- Keep session state ref-counted by active session.
- Attach provider name to `AgentProvider.Mcp` sessions so multiple MCP-backed providers can reuse the same session identifier without colliding.
- Track last activity timestamp, soft-lock state, soft-lock reason, and soft-lock timestamp per session.
- Keep shared platform keep-awake protection alive only while at least one active session is not soft-locked.
- When all remaining active sessions are soft-locked, treat the runtime as suspend-eligible even before those sessions emit stop events.
- Refresh a session's last activity timestamp on start/update and provider activity such as new tool execution.
- Clear that session's current soft-lock state on provider activity.
- Do not refresh last activity when setting a soft-lock; soft-locking represents waiting rather than autonomous work.
- When a session reaches the configured inactive session timeout, transition it to soft-locked with reason metadata and apply the same suspend-eligibility handling as other soft-locked sessions.
- Do not auto-resolve watched processes from the working directory for any provider.
- Preserve an existing watched process for the same active session when a later start/update does not provide a new watched process id and `WatchParentProcess` is still enabled.
- Provider hook ancestry owner detection should accept Codex CLI, Codex App `app-server`, Claude Code CLI/wrappers, GitHub Copilot CLI, `gh ... copilot`, and provider-specific node/npm/npx wrappers.
- Back up optional lid action changes once and restore after the last active session stops.
- While shared protection remains applied and the lid is closed, keep the Emergency Hibernation thermal monitor polling every 10 seconds and stop it automatically once protection is restored or disabled.
- Keep multiple stop signals for the same session from causing repeated cleanup side effects.

## Transcript Activity

- Use the shared `AgentTranscriptMonitor` implementation for Codex, Claude, and GitHub Copilot CLI transcript JSONL monitoring.
- Treat transcript length growth or `LastWriteTimeUtc` advancement as session activity and clear current soft-lock state through the same activity path used by tool events, unless a provider-specific detector reports a stop or soft-lock signal first.
- For Codex, prefer hook-provided `transcript_path`; otherwise fall back to a unique `~/.codex/sessions` match by session id.
- If the latest Codex transcript record is an `event_msg` whose payload type is `turn_aborted`, treat it as an interrupted Codex turn and route the session through the normal stop path instead of recording activity.
- If recent Codex transcript records contain a pending `response_item` `function_call` named `request_user_input` without a matching `function_call_output` for the same `call_id`, mark the session soft-locked with reason `codex_transcript_request_user_input_pending`.
- For Claude, prefer hook-provided `transcript_path`; otherwise fall back to a unique `~/.claude/projects` match by session id.
- If the latest Claude transcript record is a `user` record whose text content is `[Request interrupted by user]` or `[Request interrupted by user for tool use]`, treat it as an interrupted Claude turn and route the session through the normal stop path instead of recording activity.
- Claude hook `Stop` handling also reads recent transcript JSONL to reconcile background `Bash`/`PowerShell`/`Agent`/legacy `Task`/`Monitor` work and `TaskCreated`/`TaskCompleted` work, using `task_notification` completion records, `UserPromptSubmit` `<task-notification>` payloads, and `TaskStop` tool use. When work remains, the runtime must keep the session active instead of restoring protection, scheduling suspend, sending `PreSuspend`, sending `PostSessionEnd`, or starting runtime cleanup; only the final stop after tracked work is clear may enter normal stop behavior.
- For GitHub Copilot CLI, prefer hook-provided `transcriptPath` / `transcript_path`; otherwise fall back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`.
- If the latest Copilot JSONL record has top-level `type` of `abort`, treat it as a Copilot abort signal and route the session through the normal stop path instead of recording activity.
- GitHub Copilot CLI hook `agentStop` / `sessionEnd` handling also reads recent session-state JSONL to reconcile background `task` agents and async/detached `bash` / `powershell` shell work, using `read_agent` terminal states, `subagentStart` / `subagentStop`, and completion notifications such as `shell_completed`, `shell_detached_completed`, `agent_completed`, and `agent_idle`. When work remains, the runtime must keep the session active instead of restoring protection, scheduling suspend, sending `PreSuspend`, sending `PostSessionEnd`, or starting runtime cleanup; only the final stop after tracked work is clear may enter normal stop behavior.

## Runtime Cleanup

- When the active session count reaches `0`, shut down the runtime after the configured server runtime cleanup delay once no post-stop suspend request, lid-action restore, pre-suspend webhook, post-session-end webhook, post-stop sound, or equivalent cleanup work remains pending.
- Treat `serverRuntimeCleanupDelayMinutes = off` as disabling automatic runtime exit.
- Treat `serverRuntimeCleanupDelayMinutes = 0` as immediate exit after in-flight cleanup work finishes.
- Keep unhandled runtime exceptions logged under the platform local application data directory at `log/exceptions.log`, including inner exception details.
- Mark unobserved task exceptions observed as part of runtime exception handling.
- Record recent suspend request history as JSON lines under the platform local application data directory, keeping the latest configured entry count when enabled.
- Record provider hook event `prompt` fields on start events: Codex and Claude `UserPromptSubmit`, and GitHub Copilot CLI `userPromptSubmitted`.
- Store default settings under the platform local application data directory.

## Session-End Webhooks

- If a post-session-end webhook URL is configured, POST JSON with a 5-second timeout after a provider-reported normal session end when that stop does not schedule suspend.
- Also send `PostSessionEnd` when a scheduled suspend is canceled before the pre-suspend webhook is attempted.
- Include `eventType = PostSessionEnd`, `reason = SessionEnded`, provider/session identity, UTC start/activity/end timestamps, end reason metadata, active session count, working directory, transcript path when available, one-line `inputPromptPreview` when available, and full `lastResponse` when available.
- Normalize prompt previews by converting `\r\n` and `\r` to `\n`, replacing line breaks with spaces, and trimming overlong text to 50 characters with `...` using a word boundary when possible.
- Derive notification event list and push text previews from `lastResponse`, capped at 50 characters, while exposing the full response in the event details UI.
- Do not send the post-session-end webhook for abort, interrupt, manual stop/remove, watched parent process exit, or orphan cleanup paths.
- Watched parent process exit and orphan cleanup must also suppress any new pre-suspend webhook they would schedule, without canceling a pre-existing pending suspend.
- Send post-session-end webhooks in the background, keep runtime cleanup pending until the send finishes or times out, and log webhook failures without failing the stop.
