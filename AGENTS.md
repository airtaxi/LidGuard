# Codex Instructions - LidGuard

## Mandatory Rules

- You MUST NEVER run `git commit` or `git push` unless the user explicitly requests it.
- Commit messages must be written in English.
- On this Windows repository, normalize touched text files to consistent CRLF line endings before finishing. Do not leave mixed or LF-only working tree files that trigger recurring Git warnings such as `LF will be replaced by CRLF`.
- This repository is NativeAOT and trimming sensitive. Avoid APIs that trigger IL2026 / IL3050 warnings, and prefer AOT-safe overloads plus source-generated `System.Text.Json` serializers over reflection-driven or dynamic JSON helpers.
- You MUST NOT run builds unless the user explicitly asks for one, except when the changes are huge.
- If something is unclear or ambiguous, ask the user immediately and provide selectable choices where possible.

## Document Policy

- `AGENTS.md` is the single source of truth for LidGuard's product direction, technical design, current implementation state, and next work.
- `AGENTS.ko.md` is the Korean user-readable mirror of this document. Whenever this file changes in a meaningful way, update `AGENTS.ko.md` in the same turn.
- `Plan.md` was removed to avoid duplicated planning content.
- When changing core behavior, update this file instead of reintroducing duplicated design notes elsewhere.
- Any future repository-wide README that documents Provider MCP or model-managed MCP session flows must explicitly state that the behavior is not guaranteed, because correct operation depends entirely on the model choosing to call the LidGuard MCP tools at the right times.
- Any future repository-wide README that documents Codex hook/session lifecycle behavior must explicitly state that Codex App can spawn short-lived helper model sessions in the same working directory, so LidGuard intentionally does not use working-directory-only watchdog fallback for Codex unless a stable watched process id is supplied.

## Product Goal

LidGuard is a Windows-first utility for long-running local AI coding agents such as Codex, Claude Code, and GitHub Copilot CLI.

The goal is to keep Windows awake while at least one tracked agent session still needs protection, then restore the user's original power policy after the session ends or becomes suspend-eligible.

- Agent sessions start through provider hooks.
- LidGuard detects and tracks active sessions.
- Claude Code and GitHub Copilot CLI sessions can enter a runtime-managed soft-lock state when provider notifications show the agent is waiting on user input.
- While at least one non-soft-locked session is active, Windows should not enter idle sleep through `PowerRequestSystemRequired` and `PowerRequestAwayModeRequired`.
- If every remaining active session is soft-locked, LidGuard should release temporary keep-awake protection, restore any temporary lid policy change, and start the configured suspend flow when the lid is closed.
- Optional settings temporarily change the active power plan's lid close action to `Do Nothing`.
- When sessions stop, all temporary power settings must be restored to the user's original values.
- After the last active session stops, LidGuard should always request suspend when the laptop lid is closed.
- If active sessions remain but all of them are soft-locked, LidGuard should follow the same suspend path without waiting for stop hooks.
- The suspend mode remains user-selectable: Sleep by default, Hibernate optional.
- The post-stop suspend delay remains user-selectable: 10 seconds by default, `0` for immediate suspend.
- The post-stop suspend sound remains optional: off by default, with supported SystemSounds names or a playable `.wav` path.
- While keep-awake protection is applied and the laptop lid is closed, an optional Emergency Hibernation thermal monitor should poll every 10 seconds and request immediate hibernation when the system temperature reaches the configured threshold.

The key design rule is to treat normal idle sleep and lid-close sleep as separate problems. Power requests handle idle sleep. `LIDACTION` policy backup/change/restore handles lid-close behavior because standard sleep-prevention APIs cannot reliably block a user lid-close action.

## Repository Shape

- `LidGuardLib.Commons`
  - .NET 10 library.
  - Common, platform-neutral models and policies.
  - Nullable is intentionally not enabled in the csproj.
  - `ImplicitUsings` is enabled.
  - NativeAOT/trimming compatibility flags are enabled.
- `LidGuardLib`
  - .NET 10 library targeting `net10.0`.
  - Shared provider/hook utilities live in regular `*.cs` files.
  - Windows-specific runtime/process/power implementations live in `*.windows.cs`.
  - Linux/macOS placeholder files exist only for the minimal public surface currently needed to keep cross-platform builds compiling.
  - Uses CsWin32 with `CsWin32RunAsBuildTask=true` and `DisableRuntimeMarshalling=true` for AOT compatibility.
- `LidGuard`
  - .NET 10 console app targeting `net10.0`.
  - Standalone hook-facing CLI plus in-process headless runtime and stdio MCP server hosting.
  - Uses root namespace `LidGuard` and assembly/apphost name `lidguard`.
  - Prepared for .NET 10 RID-specific NativeAOT .NET tool distribution as NuGet package `lidguard` with tool command `lidguard`.
  - Supported package RIDs are `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.
  - Windows behavior is implemented; macOS/Linux currently print a support-planned message and return exit code `0`.
  - Uses a named pipe to send `start`, `stop`, `remove-session`, `status`, `settings`, and `cleanup-orphans` requests to the runtime.
  - Hosts the stdio MCP server through the `mcp-server` subcommand.
  - Stores default settings JSON at `%LOCALAPPDATA%\LidGuard\settings.json`.
- `LidGuard.slnx`
  - Root solution file including `LidGuardLib.Commons`, `LidGuardLib`, and `LidGuard`.

## Technical Design

### Windows Power Control

- Use `PowerCreateRequest`, `PowerSetRequest`, and `PowerClearRequest` for normal idle sleep prevention.
- Use `PowerRequestSystemRequired` to prevent idle system sleep.
- Use `PowerRequestAwayModeRequired` to request away-mode behavior where supported.
- Keep `PowerRequestDisplayRequired` optional; it is only needed when display sleep should also be prevented.
- Always clear power requests and close handles when protection ends.
- Do not change sleep idle timeouts. That approach was rejected because runtime crashes could leave the user's system policy in a dangerous state.

### Lid Close Policy

- The lid close setting is Windows power setting `LIDACTION`.
- Subgroup GUID: `4f971e89-eebd-4455-a8de-9e59040e7347`.
- Setting GUID: `5ca83367-6e45-459f-a27b-476b1d01c936`.
- Values are `0 = Do Nothing`, `1 = Sleep`, `2 = Hibernate`, `3 = Shut Down`.
- Read AC/DC values from the active power scheme together before making changes.
- During active sessions, write AC/DC values to `0 = Do Nothing` when the setting is enabled.
- After the last active session stops, restore the backed-up AC/DC values.
- v1 restores the scheme that was active at backup time. Future work may add policy for active scheme changes while LidGuard is running.

### Lid State And Suspend

- Lid open/close notification uses `GUID_LIDSWITCH_STATE_CHANGE`.
- Broadcast values are `0x0 = lid closed` and `0x1 = lid opened`.
- `LidSwitchNotificationRegistration` converts these values to `LidSwitchState`.
- Immediate sleep/hibernate uses `SetSuspendState` after enabling `SeShutdownPrivilege`.
- On Modern Standby systems, `SetSuspendState(false, ...)` can fail with `ERROR_NOT_SUPPORTED`; a later fallback may use a display-off strategy.
- After the last active session stops, a closed lid should always trigger suspend after the configured post-stop delay using the configured suspend mode. A delay of `0` means immediate suspend.
- If a post-stop suspend sound is configured, LidGuard should wait for the delay first, then play the configured sound to completion, then re-check the lid/session state before requesting suspend.
- If a pre-suspend webhook URL is configured, LidGuard should POST JSON before requesting suspend. The body must include `reason`, and soft-lock-triggered suspend must also include the soft-locked session count.

### Emergency Hibernation Thermal Monitor

- Emergency Hibernation uses `SystemThermalInformation.GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode)` to read the selected available system thermal-zone temperature in Celsius.
- Emergency Hibernation temperature mode is configurable as Low, Average, or High, and defaults to Average.
- The thermal monitor only runs while shared keep-awake protection is applied and the lid is closed.
- The thermal poll interval is fixed at 10 seconds.
- The Emergency Hibernation threshold is configurable, defaults to 93 Celsius, and must always be clamped to 70 through 110 Celsius before runtime use.
- When the observed temperature reaches the clamped threshold, LidGuard should cancel any pending post-stop suspend, send the pre-suspend webhook with `reason = EmergencyHibernation` using a 5-second timeout, then immediately request hibernate.
- Emergency Hibernation ignores the regular suspend mode, post-stop suspend delay, and post-stop suspend sound settings.
- Emergency Hibernation webhook timeout or failure must not block the immediate hibernation request.

### Process Exit Watcher

Hook stop events may be missed, so LidGuard also watches the agent process.

- Prefer a provided parent process id when hooks can supply one.
- When parent process id is missing, use `ICommandLineProcessResolver` with the hook working directory only for providers where that fallback is reliable enough; Codex currently skips the implicit fallback because Codex App helper sessions can cause false cleanup.
- On Windows, open the target process with synchronize/query rights and wait with `WaitForSingleObject`.
- Treat the first cleanup signal as authoritative; later stop/watchdog events for the same session should be harmless.
- If a provider launches a short-lived wrapper that exits before the real agent, provider-specific process selection may need follow-up work.

## Runtime Behavior

### Current Windows CLI Path

- `LidGuard` parses `start`, `stop`, `remove-pre-suspend-webhook`, `remove-session`, `status`, `settings`, `cleanup-orphans`, `current-temperature`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `mcp-status`, `mcp-install`, `mcp-remove`, `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`, `preview-system-sound`, `mcp-server`, and `provider-mcp-server`.
- `start`, the `UserPromptSubmit` path in `codex-hook` and `claude-hook`, and the `userPromptSubmitted` path in `copilot-hook` load persisted default settings and send them with the start IPC request.
- `remove-session --all` manually removes every active session currently tracked by the runtime.
- `remove-session` manually removes active sessions by session identifier; when `--provider` is omitted, it removes every active session whose session identifier matches. When `--provider mcp` is used, `--provider-name` can narrow the removal to one MCP-backed provider; omitting `--provider-name` removes every MCP-backed session that shares that session identifier.
- `remove-pre-suspend-webhook` clears the configured pre-suspend webhook URL and reports when no webhook is currently configured.
- `current-temperature` prints the currently recognized system thermal-zone temperature in Celsius using the selected aggregation mode, or reports when thermal-zone data is unavailable.
- `settings` prints and updates default settings, and updates a running runtime when one is listening.
- `settings` also exposes `--emergency-hibernation-on-high-temperature`, `--emergency-hibernation-temperature-mode`, and `--emergency-hibernation-temperature-celsius`; the threshold option accepts 70 through 110 only.
- `hook-install`, `hook-status`, `hook-remove`, and `hook-events` prompt for `codex`, `claude`, `copilot`, or `all` when `--provider` is omitted.
- `mcp-status`, `mcp-install`, and `mcp-remove` prompt for `codex`, `claude`, `copilot`, or `all` when `--provider` is omitted.
- `provider-mcp-status`, `provider-mcp-install`, and `provider-mcp-remove` work on a caller-supplied JSON config file path instead of using Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration commands.
- `--provider all` installs, removes, checks, or prints hook events only for providers whose default configuration roots already exist, and reports missing providers as skipped.
- `mcp-status --provider all`, `mcp-install --provider all`, and `mcp-remove --provider all` only process providers whose default configuration roots already exist, and report missing providers as skipped.
- When adding a new CLI command that takes a provider parameter, make omitted provider values prompt the user instead of silently defaulting.
- When no runtime is listening, `start` launches detached `run-server`.
- `run-server` acquires the named mutex `Local\LidGuard.Runtime.v1`.
- `run-server` is detached from inherited stdout/stderr so hook callers do not hang while reading child process output.
- Runtime communication uses a local named pipe.
- Session execution events are logged as JSON lines at `%LOCALAPPDATA%\LidGuard\session-execution.log`, keeping the latest 500 entries.
- Provider hook event logs record the `prompt` field on received start events: Codex and Claude `UserPromptSubmit`, and GitHub Copilot CLI `userPromptSubmitted`.
- Default settings are stored at `%LOCALAPPDATA%\LidGuard\settings.json`.

### MCP Server

- `LidGuard` hosts a stdio MCP server for local automation clients through `lidguard mcp-server`.
- `mcp-status` inspects the provider's global/user MCP configuration and reports whether the `lidguard` server entry is present and still points at `mcp-server`.
- `mcp-install` and `mcp-remove` register or remove the user/global LidGuard stdio MCP server named `lidguard` for Codex, Claude Code, and GitHub Copilot CLI.
- `mcp-install` prefers the current `lidguard.exe` path over the Windows `.cmd` shim when registering stdio MCP servers, because shim wrapper processes can remain visible under MCP clients and should not be mistaken for agent work.
- The regular MCP server exposes `get_settings_status`, `list_sessions`, `update_settings`, `remove_session`, `set_session_soft_lock`, and `clear_session_soft_lock`.
- `list_sessions` returns the active session list plus runtime lid/session state without the full settings payload.
- `update_settings` accepts multiple setting fields in a single request and persists them together.
- `remove_session` manually removes active sessions by session identifier and optionally narrows the removal to one provider and one MCP provider name.
- `set_session_soft_lock` and `clear_session_soft_lock` are general-purpose tools that accept provider and session identifier inputs, so non-MCP providers can also use MCP-driven soft-lock control when they can supply those values.
- `LidGuard` also hosts a separate stdio Provider MCP server through `lidguard provider-mcp-server --provider-name <name>`.
- `provider-mcp-install` and `provider-mcp-remove` directly edit a caller-supplied JSON config file and register or remove a managed stdio server entry for `provider-mcp-server`; this path intentionally does not use Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration commands.
- `provider-mcp-install` uses the same MCP executable selection policy as `mcp-install`: prefer the current `lidguard.exe` path over the Windows `.cmd` shim.
- The Provider MCP server exposes `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, and `provider_clear_soft_lock`.
- `provider_start_session` is intended to be called before a provider begins processing a user prompt, while `provider_stop_session` is intended to be called before a turn ends only when the work is truly complete.
- `provider_set_soft_lock` is intended to be called before a turn ends because the model needs user input and wants LidGuard to release keep-awake protection. The tool itself cannot end the turn; the model still has to stop or hand back the conversation after calling it.
- Provider MCP behavior is inherently model-dependent. LidGuard cannot guarantee that a model will call these tools at the right times, so this integration should always be documented as best-effort rather than guaranteed.
- MCP settings updates use the same named-pipe client and settings store used by the CLI, but they do not launch `run-server` if no runtime is listening.
- MCP server logging must stay on stderr so stdio tool traffic remains clean.

### Active Session Policy

- Session state is ref-counted by active session.
- `AgentProvider.Mcp` sessions also carry a provider name so multiple MCP-backed providers can reuse the same session identifier without colliding.
- Each session also carries a soft-lock state, reason, and timestamp.
- One or more active sessions keep shared `SystemRequired` and `AwayModeRequired` power requests alive only while at least one active session is not soft-locked.
- When all remaining active sessions are soft-locked, LidGuard treats the runtime as suspend-eligible even before those sessions emit stop events.
- Provider activity such as new tool execution clears that session's current soft-lock state.
- Codex sessions are a provider-specific exception: when a Codex session is already soft-locked, LidGuard can clear that soft-lock after five actual growth events are observed on the session transcript JSONL file. It prefers hook-provided `transcript_path` and otherwise falls back to a unique `~/.codex/sessions` match by session id.
- `AgentProvider.Mcp` sessions do not auto-resolve a watched process from the working directory, because model-managed Provider MCP sessions do not reliably identify one owning CLI process.
- `AgentProvider.Codex` sessions also do not auto-resolve a watched process from the working directory when no explicit watched process id is supplied, because Codex App can spawn short-lived helper model sessions in the same working directory and the fallback can bind the wrong process.
- Optional lid action changes are backed up once and restored after the last active session stops.
- While shared protection remains applied and the lid is closed, the Emergency Hibernation thermal monitor polls every 10 seconds and stops automatically once protection is restored or disabled.
- Multiple stop signals for the same session should not cause repeated cleanup side effects.
- Persistent pending backup state is still missing and is the next resilience priority.

### Settings Defaults

- Normal idle sleep prevention: enabled.
- Away-mode sleep prevention: enabled.
- Display sleep prevention: disabled.
- Temporary lid close action change: enabled for the headless CLI runtime and applied to AC/DC together.
- Post-stop suspend delay: 10 seconds by default, `0` for immediate suspend.
- Post-stop suspend mode: Sleep by default, Hibernate optional.
- Post-stop suspend sound: off by default.
- Pre-suspend webhook URL: off by default.
- Emergency Hibernation on high temperature: enabled by default.
- Emergency Hibernation temperature mode: Average by default, with Low and High optional.
- Emergency Hibernation temperature threshold: 93 Celsius by default, clamped to 70 through 110.
- Closed-lid PermissionRequest decision: Deny by default, Allow optional.
- PermissionRequest hooks only emit a structured allow/deny decision when the runtime reports the lid is closed; otherwise they return empty stdout so the provider's default permission flow continues.
- Claude and GitHub Copilot CLI closed-lid `PermissionRequest` outputs also set `interrupt: true`. Even if another provider later uses a similar JSON shape, keep hook DTOs separate per provider instead of sharing one output type across providers.
- Claude `Elicitation` hooks emit a structured `cancel` only when the runtime reports the lid is closed; otherwise they return empty stdout so Claude's default elicitation flow continues.
- Parent process watchdog: enabled.

## Implemented Components

### Commons

- `Sessions`
  - `AgentProvider`
  - `LidGuardSessionKey`
  - `LidGuardSessionSoftLockState`
  - `LidGuardSessionStartRequest`
  - `LidGuardSessionStopRequest`
  - `LidGuardSessionSnapshot`
  - `LidGuardSessionRegistry`
- `Settings`
  - `ClosedLidPermissionRequestDecision`
  - `EmergencyHibernationTemperatureMode`
  - `LidGuardSettings`
  - `LidGuardSettings.Default`
  - `LidGuardSettings.HeadlessRuntimeDefault`
  - `LidGuardSettings.ClampEmergencyHibernationTemperatureCelsius`
  - `LidGuardSettings.Normalize`
- `Results`
  - `LidGuardOperationResult`
  - `LidGuardOperationResult<TValue>`
- `Platform`
  - `ILidGuardRuntimePlatform`
  - `LidGuardRuntimeServiceSet`
- `Services`
  - `IPowerRequestService`
  - `ILidGuardPowerRequest`
  - `ILidActionService`
  - `ISystemSuspendService`
  - `IProcessExitWatcher`
  - `ICommandLineProcessResolver`
  - `ILidStateSource`
- `Power`
  - `PowerRequestOptions`
  - `PowerLine`
  - `LidAction`
  - `LidActionBackup`
  - `LidActionPolicyController`
  - `LidSwitchState`
  - `SystemSuspendMode`
- `Hooks`
  - Codex hook input models.
  - Claude hook input models.
  - GitHub Copilot CLI hook input models.
  - Codex installation request/result/inspection models.
  - Claude installation request/result/inspection models.
  - GitHub Copilot CLI installation request/result/inspection models.
  - Codex `config.toml` managed block generation and inspection.
  - Claude `settings.json` managed hook generation and inspection.
  - GitHub Copilot CLI managed hook JSON generation and inspection.

`LidActionPolicyController` backs up AC/DC lid close actions together, writes `DoNothing`, and restores backup values.

### LidGuard App

- `Ipc`
  - `LidGuardPipeCommands`
  - `LidGuardPipeNames`
  - `LidGuardPipeRequest`
  - `LidGuardPipeResponse`
  - `LidGuardRuntimeClient`
  - `LidGuardSessionStatus`
- `Settings`
  - `LidGuardSettingsStore`
  - `LidGuardSettingsFileJsonSerializerContext`
- `Control`
  - `LidGuardControlService`
  - `LidGuardControlSnapshot`
  - `LidGuardSessionRemovalOutcome`
  - `LidGuardSettingsPatch`
  - `LidGuardSettingsUpdateOutcome`
- `Runtime`
  - `CodexSoftLockTranscriptMonitor`
  - `EmergencyHibernationThermalMonitor`

`LidGuardControlService` loads/saves stored settings and can push updated settings into a running runtime without requiring the CLI entrypoint.

### Windows

- `PowerRequestService`
  - Uses `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest`.
  - Supports system-required, away-mode-required, and display-required requests.
- `LidActionService`
  - Reads/writes active power plan `LIDACTION`.
- `ProcessExitWatcher`
  - Opens a process with synchronize/query rights.
  - Waits with `WaitForSingleObject`.
- `CommandLineProcessResolver`
  - Used when a hook does not provide a parent process id.
  - Finds CLI-like processes whose current working directory matches the hook working directory.
  - Excludes transient LidGuard utility processes whose command line is running `codex-hook`, `claude-hook`, `copilot-hook`, `mcp-server`, or `provider-mcp-server`, so MCP launcher wrappers are never treated as watched agent processes.
  - Reads the remote process current directory from the process PEB instead of using WMI, to stay AOT-friendly.
  - Candidate process names include `codex`, `claude`, `copilot`, `cmd`, `pwsh`, `powershell`, `node`, `dotnet`, and `gh`.
- `LidSwitchNotificationRegistration`
  - Registers `GUID_LIDSWITCH_STATE_CHANGE`.
  - Converts broadcast values to `LidSwitchState`.
- `SystemSuspendService`
  - Enables `SeShutdownPrivilege`.
  - Calls `SetSuspendState` for sleep/hibernate.
- `LidGuardRuntimePlatform`
  - Adapts Windows power/process services into the Commons runtime platform abstraction.
  - Reports unsupported platforms before Windows-only services are constructed.
- `CodexHookInstaller`
  - Resolves `%USERPROFILE%\.codex\config.toml` or `CODEX_HOME\config.toml`.
  - Installs, removes, and inspects the LidGuard-managed Codex hook block.
  - When no managed block marker exists, status falls back to detecting valid `lidguard ... codex-hook` command entries in the required hook events, while removal also cleans an optional `SessionEnd` hook when present.
  - Backs up existing config files before writing when configured.
- `CodexHookEventLog`
  - Records Codex hook diagnostics.
- `ClaudeHookInstaller`
  - Resolves `CLAUDE_CONFIG_DIR\settings.json` or `%USERPROFILE%\.claude\settings.json`.
  - Installs, removes, and inspects the LidGuard-managed Claude hook entries in `settings.json`.
  - Backs up existing config files before writing when configured.
- `ClaudeHookEventLog`
  - Records Claude hook diagnostics.
- `GitHubCopilotHookInstaller`
  - Resolves `COPILOT_HOME\hooks\lidguard-copilot-cli.json` or `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`.
  - Installs, removes, and inspects the LidGuard-managed global GitHub Copilot CLI hook file by default.
  - Scans user-level hooks, user settings, repository hooks, and repository Copilot settings for non-LidGuard `agentStop` hooks and warns about continuation risk.
  - Backs up existing hook files before writing when configured.
- `GitHubCopilotHookEventLog`
  - Records GitHub Copilot CLI hook diagnostics.

### MCP

- `LidGuardMcpServerCommand`
  - Hosts the stdio MCP server from the main `lidguard` executable.
- `ProviderMcpServerCommand`
  - Hosts the dedicated stdio Provider MCP server from the main `lidguard` executable.
- `LidGuardSettingsMcpTools`
  - Exposes `get_settings_status`.
  - Exposes `list_sessions` for active-session listing without the full settings payload.
  - Exposes `update_settings` for multi-field settings updates in one call, including Emergency Hibernation temperature settings.
  - Exposes `remove_session` for manual active-session deletion by session identifier, with optional provider and MCP provider-name filters.
  - Exposes `set_session_soft_lock` and `clear_session_soft_lock` for provider/session-targeted soft-lock control.
- `LidGuardProviderMcpTools`
  - Exposes `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, and `provider_clear_soft_lock` for model-managed Provider MCP integrations.
- `LidGuard` MCP hosting
  - Uses `WithStdioServerTransport()` and `WithTools<LidGuardSettingsMcpTools>()` from the official C# SDK.
  - Keeps host logging on stderr so MCP stdio responses stay valid.

## Provider MCP Mapping

### Generic Provider MCP

- Provider enum: `AgentProvider.Mcp`.
- Provider sessions are distinguished by both `sessionId` and `providerName`.
- The external provider must supply a stable session identifier to the model when possible. If it cannot, the model should generate a stable identifier and keep reusing it until the session is truly complete.
- Provider MCP install/remove/status commands are `lidguard provider-mcp-status --config <json-path>`, `lidguard provider-mcp-install --config <json-path> --provider-name <name>`, and `lidguard provider-mcp-remove --config <json-path>`.
- Provider MCP config is edited directly as JSON and does not reuse the Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration flows.
- Provider MCP server command: `lidguard provider-mcp-server --provider-name <name>`.
- Provider MCP start tool: `provider_start_session`.
- Provider MCP stop tool: `provider_stop_session`.
- Provider MCP soft-lock tools: `provider_set_soft_lock` and `provider_clear_soft_lock`.
- `provider_start_session` should be described to the model as a pre-user-prompt call.
- `provider_stop_session` should be described to the model as a pre-turn-end call only when the work is truly complete.
- `provider_set_soft_lock` should explain the soft-lock concept and instruct the model to call it before ending a turn that is about to wait for user input. The description must also explain that the tool cannot end the turn on the model's behalf.
- Because all Provider MCP behavior depends on model compliance, do not promise or document it as guaranteed behavior.

## Provider Hook Mapping

### Codex CLI

- Start event: `UserPromptSubmit`.
- Permission decision event: `PermissionRequest`.
- Required stop event: `Stop`.
- Optional compatibility stop event: `SessionEnd` when a Codex build emits it.
- Command path: `lidguard codex-hook` when the global tool is available on PATH, otherwise the current executable path plus `codex-hook`.
- Snippet command: `lidguard codex-hooks --format config-toml`.
- Install/status/remove commands: `lidguard hook-install --provider codex`, `lidguard hook-status --provider codex`, and `lidguard hook-remove --provider codex`.
- MCP status/install/remove commands: `lidguard mcp-status --provider codex`, `lidguard mcp-install --provider codex`, and `lidguard mcp-remove --provider codex`.
- Codex may require `features.codex_hooks = true`.
- Codex MCP registration delegates to `codex mcp add/remove` and writes a global stdio server entry named `lidguard`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PermissionRequest`, and `Stop`; `SessionEnd` is optional and shown separately when present.
- `codex-hook` reads Codex hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider codex`.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and returns a structured allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed.
- For `Stop`, and for `SessionEnd` when a Codex build emits it, it sends internal `stop --provider codex`.
- Notification-driven soft-lock detection is currently unsupported for Codex because the current public hook surface does not expose a comparable `Notification` event. Future support can be added if Codex exposes notification or machine-readable pending-state hooks later.
- Because Codex lacks a notification-style soft-lock clear signal, LidGuard records `transcript_path` from `UserPromptSubmit` and uses transcript JSONL monitoring as a Codex-only exception path. Once a Codex session is already soft-locked, five actual transcript growth events clear that soft lock. If `transcript_path` is missing, LidGuard falls back to a unique `~/.codex/sessions` transcript match by session id.
- Codex hook input does not provide a stable parent process id. LidGuard therefore only uses an explicit watched process id for Codex and intentionally skips working-directory-only watchdog fallback, because Codex App can spawn short-lived helper model sessions such as title generation in the same working directory.
- Codex `PermissionRequest` exits successfully with structured JSON stdout only for closed-lid decisions; when the lid is open, unknown, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Codex task when a runtime request fails.
- This behavior is based on analyzing the `openai/codex` `codex-rs` hook source: `exit 0` with empty stdout is treated as a no-op success, while non-empty stdout may be parsed as hook JSON or interpreted as plain-text context depending on the event.

Reference:

- https://developers.openai.com/codex/hooks
- https://github.com/openai/codex

### Claude Code

- Start event: `UserPromptSubmit`.
- Activity telemetry events: `PreToolUse`, `PostToolUse`, `PostToolUseFailure`.
- Permission decision event: `PermissionRequest`.
- MCP elicitation event: `Elicitation`.
- Soft-lock notification event: `Notification`.
- Stop events: `Stop`, `StopFailure`, `SessionEnd`.
- Command path: `lidguard claude-hook` when the global tool is available on PATH, otherwise the current executable path plus `claude-hook`.
- Snippet command: `lidguard claude-hooks --format settings-json`.
- Install/status/remove commands: `lidguard hook-install --provider claude`, `lidguard hook-status --provider claude`, and `lidguard hook-remove --provider claude`.
- MCP status/install/remove commands: `lidguard mcp-status --provider claude`, `lidguard mcp-install --provider claude`, and `lidguard mcp-remove --provider claude`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `Stop`, `StopFailure`, `Elicitation`, `PermissionRequest`, `Notification`, and `SessionEnd`.
- Default config path: `CLAUDE_CONFIG_DIR\settings.json` when `CLAUDE_CONFIG_DIR` is set, otherwise `%USERPROFILE%\.claude\settings.json`.
- Claude MCP registration uses the user-scope global config at `%USERPROFILE%\.claude.json` and delegates to `claude mcp add/remove --scope user`.
- Windows hook config uses `shell = "powershell"` in Claude `settings.json` command hooks.
- Based on analysis of a locally retained Claude Code source snapshot, command hooks treat `exit code 0` with empty stdout as a successful no-op, while non-empty stdout may be interpreted as hook JSON or plain-text output depending on the execution path.
- Based on the same local source snapshot analysis, `PermissionRequest` only becomes a programmatic allow/deny when the hook returns structured JSON with `hookSpecificOutput.decision`; LidGuard also sets `interrupt: true` on those closed-lid decisions so Claude stops the interactive permission path immediately. Empty stdout keeps the normal permission flow.
- `claude-hook` reads Claude hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider claude`.
- For `PreToolUse`, `PostToolUse`, and `PostToolUseFailure`, it records provider activity and clears the current session soft-lock state for non-`AskUserQuestion` tools.
- For `Elicitation`, it does not stop the runtime; it queries the runtime lid state and returns a structured `cancel` only when the lid is closed.
- For `Notification`, `permission_prompt` and `elicitation_dialog` mark the session soft-locked, while `elicitation_complete` and `elicitation_response` clear the current soft-lock state.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and returns a Claude-specific structured allow/deny decision with `interrupt: true` from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed.
- When working on Claude Code-related setup, support, or documentation, explicitly and strongly warn the user not to use third-party prompt-style hooks alongside LidGuard. Explain that LidGuard must only answer its own closed-lid `PermissionRequest` and `Elicitation` paths and must not be presented as able to answer or proxy third-party hook prompts.
- For `Stop`, `StopFailure`, and `SessionEnd`, it sends internal `stop --provider claude`.
- The analyzed Claude hook input provides `session_id` and `cwd`, but not a stable parent process id, so the current implementation resolves a process by working directory.
- Claude `Elicitation` exits successfully with structured JSON stdout only for closed-lid `cancel`; when the lid is open, unknown, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.
- Claude `PermissionRequest` exits successfully with structured JSON stdout only for closed-lid decisions; when the lid is open, unknown, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.

Reference:

- https://code.claude.com/docs/en/hooks

### GitHub Copilot CLI

- Start event: `userPromptSubmitted`.
- Stop event: `agentStop`.
- Closed-lid permission decision event: `permissionRequest`.
- Closed-lid ask-user guard event: `preToolUse` when `toolName` is `ask_user`.
- Activity event: `postToolUse`.
- Soft-lock notification event: `notification` with `notification_type` / `notificationType` of `permission_prompt` or `elicitation_dialog`.
- Telemetry-only events: `sessionStart`, `sessionEnd`, and `errorOccurred`.
- Command path: `lidguard copilot-hook --event <event-name>` when the global tool is available on PATH, otherwise the current executable path plus `copilot-hook --event <event-name>`.
- Snippet command: `lidguard copilot-hooks --format config-json`.
- Install/status/remove commands: `lidguard hook-install --provider copilot`, `lidguard hook-status --provider copilot`, and `lidguard hook-remove --provider copilot`.
- MCP status/install/remove commands: `lidguard mcp-status --provider copilot`, `lidguard mcp-install --provider copilot`, and `lidguard mcp-remove --provider copilot`.
- Default global config path: `COPILOT_HOME\hooks\lidguard-copilot-cli.json` when `COPILOT_HOME` is set, otherwise `%USERPROFILE%\.copilot\hooks\lidguard-copilot-cli.json`.
- GitHub Copilot CLI MCP registration delegates to `copilot mcp add/remove` and uses the user config file `%USERPROFILE%\.copilot\mcp-config.json`.
- GitHub Copilot CLI also supports inline user hooks in `~/.copilot/settings.json`; repository hooks in `.github/hooks/` and repository Copilot settings are loaded alongside user hooks, so `hook-install` and `hook-status` inspect those sources for conflicts.
- `hook-install` and `hook-status` require `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, `permissionRequest`, `agentStop`, `errorOccurred`, and a filtered `notification` hook.
- Because official Copilot CLI docs allow `agentStop` hooks to return `decision: "block"` with a `reason` continuation prompt, `hook-install` and `hook-status` should warn when non-LidGuard `agentStop` hooks are present.
- Based on the official Copilot CLI hooks documentation, passive hooks such as `sessionStart` may be implemented as logging-only shell commands with no JSON output, so `exit code 0` with empty stdout is a valid no-op pattern for non-decision hooks.
- Based on the official hooks configuration reference, `preToolUse` output JSON is optional and omitting output allows the tool by default, so structured JSON should only be returned when LidGuard intentionally wants to influence a hook decision.
- Even if a future GitHub Copilot CLI hook output ends up looking similar to another provider's current hook JSON, keep a dedicated GitHub Copilot CLI hook output type. Hook contracts are provider-specific and are not standardized across CLIs.
- `copilot-hook` takes the configured event name from the command line because camelCase GitHub Copilot CLI hook payloads do not consistently include the event name in stdin JSON.
- For `userPromptSubmitted`, it sends internal `start --provider copilot`.
- For `permissionRequest`, it does not stop the runtime; it queries the runtime lid state and returns a GitHub Copilot CLI allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed, and it includes `interrupt: true`.
- For `preToolUse`, it does not stop the runtime; it denies `ask_user` only when the lid is closed, so the agent cannot soft-lock waiting for user input that cannot be answered, and it clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it records tool completion activity and clears the current session soft-lock state for non-`ask_user` tools.
- For `notification`, it marks the session soft-locked when GitHub Copilot CLI reports `permission_prompt` or `elicitation_dialog`.
- For `agentStop`, it sends internal `stop --provider copilot`.
- For `sessionStart`, `sessionEnd`, and `errorOccurred`, it records telemetry only.
- GitHub Copilot CLI hook input currently does not provide a stable parent process id in the documented payloads, so the current implementation resolves a process by working directory.
- GitHub Copilot CLI `permissionRequest` exits successfully with structured JSON stdout only for closed-lid decisions; when the lid is open, unknown, or runtime status is unavailable, it exits successfully with empty stdout so the normal permission flow continues.
- GitHub Copilot CLI `preToolUse` exits successfully with structured JSON stdout only for closed-lid `ask_user` denial; otherwise it exits successfully with empty stdout so normal tool handling continues.

Reference:

- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference
- https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference

## CLI Examples

```powershell
lidguard start --provider codex --session "<session-id>" --parent-pid 1234
lidguard stop --provider codex --session "<session-id>"
lidguard remove-pre-suspend-webhook
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard start --provider claude --session "<session-id>"
lidguard stop --provider claude --session "<session-id>"
lidguard claude-hook
lidguard claude-hooks --format settings-json
lidguard start --provider copilot --session "<session-id>"
lidguard stop --provider copilot --session "<session-id>"
lidguard copilot-hook --event userPromptSubmitted
lidguard copilot-hooks --format config-json
lidguard codex-hook
lidguard codex-hooks --format config-toml
lidguard hook-status --provider copilot
lidguard hook-install --provider copilot
lidguard hook-remove --provider copilot
lidguard hook-events --provider copilot --count 50
lidguard mcp-status --provider copilot
lidguard mcp-install --provider copilot
lidguard mcp-remove --provider copilot
lidguard hook-status --provider claude
lidguard hook-install --provider claude
lidguard hook-remove --provider claude
lidguard hook-events --provider claude --count 50
lidguard mcp-status --provider claude
lidguard mcp-install --provider claude
lidguard mcp-remove --provider claude
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 50
lidguard mcp-status --provider codex
lidguard mcp-install --provider codex
lidguard mcp-remove --provider codex
lidguard mcp-status --provider all
lidguard mcp-install --provider all
lidguard mcp-remove --provider all
lidguard provider-mcp-status --config "C:\path\to\mcp.json"
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
lidguard provider-mcp-remove --config "C:\path\to\mcp.json"
lidguard provider-mcp-server --provider-name "ExampleProvider"
lidguard current-temperature
lidguard current-temperature --temperature-mode high
lidguard preview-system-sound --name Asterisk
lidguard settings
lidguard settings --emergency-hibernation-temperature-mode average
lidguard settings --change-lid-action true
lidguard settings --post-stop-suspend-delay-seconds 0
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --closed-lid-permission-request-decision allow
lidguard settings --prevent-away-mode-sleep true --prevent-display-sleep true --power-request-reason "LidGuard keeps agent sessions awake"
lidguard status
lidguard cleanup-orphans
```

## MCP Server Example

```powershell
lidguard mcp-server
```

## Local Packaging Note

- `pack-local-reinstall.bat` can fail once because of transient Windows Defender file-lock interference.
- Retry the script before taking any broader recovery action; when this specific issue is the cause, a retry is typically enough.
- Do not bring down any build server just because the first `pack-local-reinstall.bat` attempt failed with this known Defender issue.

## Missing Work

The Windows CLI hook receiving path is implemented for Codex, Claude Code, and GitHub Copilot CLI. Remaining work is now focused on resilience and verification.

- Add persistent pending backup state for crash recovery. This is the recommended immediate next task because a forced runtime crash must not leave the active power plan stuck at `DoNothing`.
- Add runtime lifecycle policy for idle shutdown.
- Verify Codex hook behavior on the latest Codex CLI and Codex Desktop/App.
- Verify the analyzed Claude Code hook stdout behavior against the latest released Claude Code build before finalizing provider integration.
- Verify the documented GitHub Copilot CLI hook output behavior against the latest CLI build before finalizing provider integration.
- Verify user-level `~/.copilot/hooks/` loading and inline `~/.copilot/settings.json` hook composition against the latest GitHub Copilot CLI build.
- Verify parent process id availability for GitHub Copilot CLI hooks.
- Verify parent process id availability for Claude Code Windows hooks.
- Verify GitHub Copilot CLI session id stability.
- Verify `PowerReadACValueIndex`/`PowerReadDCValueIndex` read/write behavior under normal user permissions.
- Verify Group Policy or MDM blocked power settings and fallback messages.
- Add direct Codex soft-lock support only if Codex later exposes a notification or machine-readable pending-state hook surface.

## Completed Work

1. ~~Add a Windows hook-facing CLI project.~~
2. ~~Keep Windows-only process and power behavior in `LidGuardLib`.~~
3. ~~Normalize CLI `start` requests into `LidGuardSessionStartRequest`.~~
4. ~~Normalize CLI `stop` requests into `LidGuardSessionStopRequest`.~~
5. ~~When `--parent-pid` is missing, use `ICommandLineProcessResolver` with the hook working directory.~~
6. ~~Start with a local/headless orchestration path.~~
7. ~~Add settings loading for the headless runtime.~~
8. ~~Add a solution file including `LidGuardLib.Commons`, `LidGuardLib`, and `LidGuard`.~~
9. ~~Add Codex hook parsing, snippet output, and managed config install/remove/status helpers.~~
10. ~~Map Codex `Stop` to stop handling, keep `SessionEnd` as an optional compatibility stop trigger, and handle `PermissionRequest` as a closed-lid-only settings-driven allow/deny decision.~~
11. ~~Add Claude hook parsing, snippet output, and managed `settings.json` install/remove/status helpers.~~
12. ~~Map Claude `Stop`, `StopFailure`, and `SessionEnd` to stop handling, while handling `PermissionRequest` as a closed-lid-only settings-driven allow/deny decision.~~
13. ~~Add a stdio MCP server that can read LidGuard settings and update multiple settings in one request.~~
14. ~~Add a Claude `Elicitation` hook guard that cancels closed-lid MCP elicitation requests.~~
15. ~~Always request suspend after the last session stops while the lid is closed, while keeping Sleep/Hibernate mode selectable.~~
16. ~~Add a configurable post-stop suspend delay with a default of 10 seconds and `0` for immediate suspend.~~
17. ~~Add an optional post-stop suspend completion sound with SystemSounds or `.wav` support, and wait for it before suspend.~~
18. ~~Add a `preview-system-sound` CLI command for auditioning supported SystemSounds names.~~
19. ~~Add GitHub Copilot CLI hook parsing, snippet output, and managed global hook install/remove/status helpers.~~
20. ~~Map GitHub Copilot CLI `userPromptSubmitted` and `agentStop` to start/stop handling, handle `permissionRequest` as a closed-lid-only settings-driven allow/deny decision with `interrupt: true`, and deny closed-lid `preToolUse` `ask_user`.~~
21. ~~Add runtime-led Claude/GitHub Copilot soft-lock orchestration driven by provider notifications, with per-session soft-lock state and activity-based clearing.~~
22. ~~Add a `current-temperature` CLI command for reporting the current Windows-recognized system thermal-zone temperature.~~
23. ~~Add Emergency Hibernation temperature mode selection with Low/Average/High settings and a `current-temperature` mode override.~~

## Design Constraints

- Keep cross-platform-capable logic in `LidGuardLib.Commons`.
- Keep Windows API calls and Windows-only assumptions in `LidGuardLib` `*.windows.cs` files.
- Do not enable Nullable in the current library csproj files unless the user explicitly asks.
- Keep `ImplicitUsings` enabled.
- Keep NativeAOT/trimming compatibility in mind.
- When adding an enum that may be serialized to JSON, attach `JsonStringEnumConverter<TEnum>` to the enum type so values are stored as strings, not numbers.
- Prefer libraries over manual interop where reasonable.
- For Windows native APIs, prefer CsWin32. Keep `NativeMethods.txt` minimal and sorted enough to maintain.
- Do not introduce reflection-heavy, dynamic-loading, or runtime-marshalling-dependent patterns unless there is a clear AOT-safe reason.
- Do not share hook DTOs across providers only because their current JSON shapes look similar. Hook contracts are provider-specific and should keep separate types.
- Do not reintroduce sleep idle timeout modification.
- Use power plan writes only for behavior that power requests cannot cover, currently `LIDACTION`.
- Before version 1.0.0, do not add migration-only legacy code for behavior or configuration that has not been publicly released.

## Failure Modes

- Hook start succeeds but stop is missed: parent process watcher should cleanup.
- Runtime crashes after changing lid action: future pending backup state should restore on the next CLI run.
- Power setting changes are denied by policy: keep normal power requests and surface the failure.
- Hibernate is unsupported or disabled: fail clearly or use a future safe fallback.
- Multiple providers run at once: ref-count active sessions and restore only after the last session stops.
- Active power scheme changes during a session: v1 restores the originally backed-up scheme.

## .NET Tool Package Guidelines

- Do not run `dotnet pack` unless the user explicitly asks for package creation. `dotnet pack` performs a build.
- For NuGet upload, use the `$publish-nuget` skill at `C:\Users\kck41\.codex\skills\publish-nuget\SKILL.md`. Do not upload with raw `dotnet nuget push` commands.
- The `$publish-nuget` skill only publishes existing `.nupkg` files. If the user asks to publish but packages do not exist yet, ask whether packing should be done first.
- Do not open, inspect, quote, or rewrite `C:\Data\Scripts\publish_nuget-nopause.bat` unless the user explicitly asks to work on that file.
- Confirm the package version in `LidGuard\LidGuard.csproj` before packing.
- Confirm license metadata before public NuGet.org upload. Add either `PackageLicenseExpression` or `PackageLicenseFile` before publishing publicly.
- If `DOTNET_CLI_HOME` is set for packaging, delete that temporary directory immediately after packaging finishes.
- After local packaging, test installation from the local package source before upload.

Package commands:

```powershell
dotnet pack .\LidGuard\LidGuard.csproj -c Release
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x64
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x86
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-arm64
```

Expected package files:

```text
artifacts\packages\lidguard.0.1.0.nupkg
artifacts\packages\lidguard.win-x64.0.1.0.nupkg
artifacts\packages\lidguard.win-x86.0.1.0.nupkg
artifacts\packages\lidguard.win-arm64.0.1.0.nupkg
```

Publish commands:

```powershell
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x86.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-arm64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.0.1.0.nupkg"
```

Local install smoke test:

```powershell
dotnet tool install --global lidguard --add-source .\artifacts\packages --version 0.1.0
lidguard --help
lidguard status
```

## Operational Notes

- Existing Codex and Claude config should point directly to the intended `lidguard.exe` path after `hook-install`.
- When helping a user with Claude deployment or configuration, explicitly and strongly warn them not to rely on third-party prompt hooks with LidGuard. State that LidGuard can only make its own closed-lid permission or elicitation decisions and cannot safely respond on behalf of unrelated Claude hook prompts.
- If tests are added, prefer focused unit tests around Commons policy controllers and small integration-style tests around Windows service wrappers where safe.
