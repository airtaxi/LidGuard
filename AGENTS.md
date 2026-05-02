# Codex Instructions - LidGuard

## Mandatory Rules

- You MUST NEVER run `git commit` or `git push` unless the user explicitly requests it.
- Commit messages must be written in English.
- On this Windows repository, normalize touched text files to consistent CRLF line endings before finishing. Do not leave mixed or LF-only working tree files that trigger recurring Git warnings such as `LF will be replaced by CRLF`.
- This repository is NativeAOT and trimming sensitive. Avoid APIs that trigger IL2026 / IL3050 warnings, and prefer AOT-safe overloads plus source-generated `System.Text.Json` serializers over reflection-driven or dynamic JSON helpers.
- Windows native interop must stay centralized through CsWin32-generated APIs. Do not add direct `[DllImport]` / `[LibraryImport]`, `NativeLibrary` / `GetProcAddress`, or manual COM vtable calls in project code unless CsWin32 or available metadata cannot express the API and the exception is documented in this file.
- `Microsoft.Windows.WDK.Win32Metadata` is intentionally referenced only to let CsWin32 generate WDK-backed APIs such as `NtQueryInformationProcess`; keep it `PrivateAssets="all"` and do not use it as permission to add hand-written native declarations.
- Persisted timestamps for sessions, runtime logs, hook logs, suspend history, backup state, notification data, and timestamped backup file names must be recorded from UTC sources such as `DateTimeOffset.UtcNow`; user-facing CLI and web output must convert stored timestamps to the current system local time immediately before display.
- You MUST NOT run builds unless the user explicitly asks for one, except when the changes are huge.
- If something is unclear or ambiguous, ask the user immediately and provide selectable choices where possible.

## Document Policy

- `AGENTS.md` is the single source of truth for LidGuard's product direction, technical design, current implementation state, and next work.
- `AGENTS.ko.md` is only the Korean user-readable mirror of this document. Do not read it during routine context gathering because it duplicates `AGENTS.md` and wastes context; read and update it only when `AGENTS.md` changes meaningfully or when the user explicitly asks.
- `Plan.md` was removed to avoid duplicated planning content.
- When changing core behavior, update this file instead of reintroducing duplicated design notes elsewhere.
- Any future repository-wide README that documents Provider MCP or model-managed MCP session flows must explicitly state that the behavior is not guaranteed, because correct operation depends entirely on the model choosing to call the LidGuard MCP tools at the right times.
- Any future repository-wide README that documents Codex hook/session lifecycle behavior must explicitly state that Codex App can still leave `process=none` sessions in the same working directory, so LidGuard only uses the Codex working-directory watchdog fallback for shell-hosted CLI sessions whose resolved process or direct parent is `cmd.exe`, `pwsh.exe`, or `powershell.exe`, and that cleanup path must never remove `process=none` Codex sessions.

## Product Goal

LidGuard is a Windows-first utility for long-running local AI coding agents such as Codex, Claude Code, and GitHub Copilot CLI.

The goal is to keep Windows awake while at least one tracked agent session still needs protection, then restore the user's original power policy after the session ends or becomes suspend-eligible.

- Agent sessions start through provider hooks.
- LidGuard detects and tracks active sessions.
- Claude Code and GitHub Copilot CLI sessions can enter a runtime-managed soft-lock state when provider notifications show the agent is waiting on user input.
- While at least one non-soft-locked session is active, Windows should not enter idle sleep through `PowerRequestSystemRequired` and `PowerRequestAwayModeRequired`.
- If every remaining active session is soft-locked, LidGuard should release temporary keep-awake protection, restore any temporary lid policy change, and start the configured suspend flow only when the lid is closed and no suspend-blocking visible display monitors remain attached to the desktop.
- If a session has no activity after the configured session timeout, LidGuard should transition it to the soft-locked state and apply the same keep-awake release flow used for normal soft-lock operations.
- Optional settings temporarily change the active power plan's lid close action to `Do Nothing`.
- When sessions stop, all temporary power settings must be restored to the user's original values.
- After the last active session stops, LidGuard should always request suspend when the laptop lid is closed and no suspend-blocking visible display monitors remain attached to the desktop.
- Once the active session count reaches `0`, the server runtime should exit after the configured server runtime cleanup delay once any in-flight suspend or cleanup work finishes. The delay defaults to 10 minutes, and `off` means exit immediately after in-flight work finishes.
- If active sessions remain but all of them are soft-locked, LidGuard should follow the same suspend path without waiting for stop hooks.
- The suspend mode remains user-selectable: Sleep by default, Hibernate optional.
- The post-stop suspend delay remains user-selectable: 10 seconds by default, `0` for immediate suspend.
- The post-stop suspend sound remains optional: off by default, with supported SystemSounds names or a playable `.wav` path.
- The post-stop suspend sound volume override remains optional: off by default, with an allowed master volume range of 1 through 100 percent.
- The inactive session timeout remains user-selectable: 12 minutes by default, `off` optional, and enabled values must be at least 1 minute.
- While keep-awake protection is applied and the laptop lid is closed with no suspend-blocking visible display monitors remaining on the desktop, an optional Emergency Hibernation thermal monitor should poll every 10 seconds and request immediate hibernation when the system temperature reaches the configured threshold.

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
- `LidGuard.Notifications`
  - .NET 10 ASP.NET Core Razor Pages app targeting `net10.0`.
  - Receives LidGuard pre-suspend webhooks and sends browser Web Push notifications to subscribed clients.
  - Stores subscriptions, webhook events, and delivery attempts in SQLite.
  - Uses server-side VAPID settings; VAPID private keys and access tokens must never be committed.
- `LidGuard.slnx`
  - Root solution file including `LidGuardLib.Commons`, `LidGuardLib`, `LidGuard`, and `LidGuard.Notifications`.

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
- Closed-lid policy decisions start from `GetSystemMetrics(SM_CMONITORS)` and exclude inactive monitor connections reported by Windows WMI. The final suspend eligibility check also excludes internal laptop panel connections while `LidSwitchState` is `Closed`; LidGuard only treats the machine as suspend-eligible for lid-close policy when the resulting visible display monitor count is `0`.
- Immediate sleep/hibernate uses `SetSuspendState` after enabling `SeShutdownPrivilege`.
- On Modern Standby systems, `SetSuspendState(false, ...)` can fail with `ERROR_NOT_SUPPORTED`; a later fallback may use a display-off strategy.
- After the last active session stops, LidGuard should request suspend after the configured post-stop delay using the configured suspend mode only when the lid is closed and the suspend eligibility visible display monitor count is `0`. A delay of `0` means immediate suspend.
- If a post-stop suspend sound is configured, LidGuard should wait for the delay first, then play the configured sound to completion, then re-check the lid/session state before requesting suspend.
- If a post-stop suspend sound volume override is configured, LidGuard should capture the default output device master volume and mute state immediately before playback, temporarily unmute as needed, set the configured master volume percent for playback, then restore the previous volume and mute state in the sound playback cleanup path.
- If a pre-suspend webhook URL is configured, LidGuard should POST JSON before requesting suspend. The body must include `reason`, and soft-lock-triggered suspend must also include the soft-locked session count.

### Emergency Hibernation Thermal Monitor

- Emergency Hibernation uses `SystemThermalInformation.GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode)` to read the selected available system thermal-zone temperature in Celsius.
- Emergency Hibernation temperature mode is configurable as Low, Average, or High, and defaults to Average.
- The thermal monitor only runs while shared keep-awake protection is applied, the lid is closed, and the suspend eligibility visible display monitor count is `0`.
- The thermal poll interval is fixed at 10 seconds.
- The Emergency Hibernation threshold is configurable, defaults to 93 Celsius, and must always be clamped to 70 through 110 Celsius before runtime use.
- When the observed temperature reaches the clamped threshold, LidGuard should cancel any pending post-stop suspend, send the pre-suspend webhook with `reason = EmergencyHibernation` using a 5-second timeout, then immediately request hibernate.
- Emergency Hibernation ignores the regular suspend mode, post-stop suspend delay, post-stop suspend sound, and sound volume override settings.
- Emergency Hibernation webhook timeout or failure must not block the immediate hibernation request.

### Process Exit Watcher

Hook stop events may be missed, so LidGuard also watches the agent process.

- Prefer a provided parent process id when hooks can supply one.
- When parent process id is missing, use `ICommandLineProcessResolver` with the hook working directory only for providers where that fallback is reliable enough. Codex is the main exception: allow the implicit fallback only when the resolved Codex candidate process or its direct parent is `cmd.exe`, `pwsh.exe`, or `powershell.exe`, and treat `process=none` Codex sessions as out of scope for that working-directory cleanup path.
- On Windows, open the target process with synchronize/query rights and wait with `WaitForSingleObject`.
- Treat the first cleanup signal as authoritative; later stop/watchdog events for the same session should be harmless.
- If a provider launches a short-lived wrapper that exits before the real agent, provider-specific process selection may need follow-up work.

## Runtime Behavior

### Current Windows CLI Path

- `LidGuard` parses `help`, `start`, `stop`, `remove-pre-suspend-webhook`, `remove-session`, `status`, `settings`, `cleanup-orphans`, `current-lid-state`, `current-monitor-count`, `current-temperature`, `suspend-history`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `mcp-status`, `mcp-install`, `mcp-remove`, `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`, `preview-system-sound`, `preview-current-sound`, `mcp-server`, and `provider-mcp-server`.
- `help` prints a categorized command overview with short descriptions, and `help <command>` prints focused detailed help for one command or recognized command alias.
- `<command> --help` uses the same help metadata and returns before the target command validates options or performs command-specific work.
- `start`, the `UserPromptSubmit` path in `codex-hook` and `claude-hook`, and the `userPromptSubmitted` path in `copilot-hook` load persisted default settings and send them with the start IPC request.
- `remove-session --all` manually removes every active session currently tracked by the runtime.
- `remove-session` manually removes active sessions by session identifier; when `--provider` is omitted, it removes every active session whose session identifier matches. When `--provider mcp` is used, `--provider-name` can narrow the removal to one MCP-backed provider; omitting `--provider-name` removes every MCP-backed session that shares that session identifier.
- `remove-pre-suspend-webhook` clears the configured pre-suspend webhook URL and reports when no webhook is currently configured.
- `current-lid-state` prints the current lid switch state using the same `GUID_LIDSWITCH_STATE_CHANGE` source LidGuard uses for closed-lid policy decisions.
- `current-monitor-count` prints the current visible display monitor count using the same base Windows monitor visibility check LidGuard uses for closed-lid policy decisions, without the internal-display exclusion used by final suspend eligibility checks.
- `current-temperature` prints the currently recognized system thermal-zone temperature in Celsius using the selected aggregation mode, or reports when thermal-zone data is unavailable.
- `suspend-history` prints recent suspend request history from `%LOCALAPPDATA%\LidGuard\suspend-history.log`, including mode, reason, result, active session count, and related session or Emergency Hibernation temperature details when available.
- `status`, `suspend-history`, and `hook-events` display persisted timestamps in the current system local time while the underlying session, history, and hook log stores remain UTC.
- `settings` prints and updates default settings, and updates a running runtime when one is listening.
- `settings` also exposes `--emergency-hibernation-on-high-temperature`, `--emergency-hibernation-temperature-mode`, and `--emergency-hibernation-temperature-celsius`; the threshold option accepts 70 through 110 only.
- `settings` exposes `--post-stop-suspend-sound-volume-override-percent off|<1-100>` for temporary post-stop sound playback master volume override; `off` disables it and out-of-range values are rejected.
- `settings` exposes `--suspend-history-count off|<count>` for recent suspend history retention; `off` disables recording and enabled counts must be at least 1.
- `settings` exposes `--session-timeout-minutes off|<minutes>` for inactive session timeout soft-locking; `off` disables timeout soft-locking and enabled values must be at least 1.
- `settings` exposes `--server-runtime-cleanup-delay-minutes off|<minutes>` for server runtime cleanup after all active sessions are gone and pending cleanup is finished; `off` exits immediately and enabled values must be at least 1.
- `preview-system-sound` and `preview-current-sound` apply the saved post-stop suspend sound volume override setting and wait until playback finishes. `preview-current-sound` plays the saved post-stop suspend sound and prints setup guidance when no sound is configured.
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
- Session execution events are logged as JSON lines at `%LOCALAPPDATA%\LidGuard\session-execution.log`, keeping the latest 500 entries. Timeout-triggered soft-lock transitions are logged as `session-timeout-softlock-recorded`.
- Recent suspend request history is logged as JSON lines at `%LOCALAPPDATA%\LidGuard\suspend-history.log`, keeping the latest configured entry count when enabled.
- Provider hook event logs record the `prompt` field on received start events: Codex and Claude `UserPromptSubmit`, and GitHub Copilot CLI `userPromptSubmitted`.
- Default settings are stored at `%LOCALAPPDATA%\LidGuard\settings.json`.

### MCP Server

- `LidGuard` hosts a stdio MCP server for local automation clients through `lidguard mcp-server`.
- `mcp-status` inspects the provider's global/user MCP configuration and reports whether the `lidguard` server entry is present and still points at `mcp-server`.
- `mcp-install` and `mcp-remove` register or remove the user/global LidGuard stdio MCP server named `lidguard` for Codex, Claude Code, and GitHub Copilot CLI.
- `mcp-install` refreshes an already installed managed LidGuard MCP registration by removing the existing provider entry first, then reinstalling it with the current command and arguments.
- `mcp-install` prefers the current `lidguard.exe` path over the Windows `.cmd` shim when registering stdio MCP servers, because shim wrapper processes can remain visible under MCP clients and should not be mistaken for agent work.
- The regular MCP server exposes `get_settings_status`, `list_sessions`, `update_settings`, `remove_session`, `set_session_soft_lock`, and `clear_session_soft_lock`.
- `list_sessions` returns the active session list plus runtime lid/session state without the full settings payload.
- `update_settings` accepts multiple setting fields in a single request and persists them together.
- `update_settings` exposes inactive session timeout through `sessionTimeoutMinutes`, accepting `off` or an enabled minute count of at least 1.
- `update_settings` exposes server runtime cleanup delay through `serverRuntimeCleanupDelayMinutes`, accepting `off` for immediate exit or an enabled minute count of at least 1.
- `remove_session` manually removes active sessions by session identifier and optionally narrows the removal to one provider and one MCP provider name.
- `set_session_soft_lock` and `clear_session_soft_lock` are general-purpose tools that accept provider and session identifier inputs, so non-MCP providers can also use MCP-driven soft-lock control when they can supply those values.
- `LidGuard` also hosts a separate stdio Provider MCP server through `lidguard provider-mcp-server --provider-name <name>`.
- `provider-mcp-install` and `provider-mcp-remove` directly edit a caller-supplied JSON config file and register or remove a managed stdio server entry for `provider-mcp-server`; this path intentionally does not use Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration commands.
- `provider-mcp-install` uses the same MCP executable selection policy as `mcp-install`: prefer the current `lidguard.exe` path over the Windows `.cmd` shim.
- The Provider MCP server exposes `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, and `provider_clear_soft_lock`.
- `provider_start_session` is intended to be called once before a brand-new provider session begins autonomous work. It generates an 8-character lowercase hexadecimal `sessionIdentifier` from the first block of a new GUID and returns that value for reuse.
- The model must reuse the exact `sessionIdentifier` returned by `provider_start_session` in `provider_set_soft_lock`, `provider_clear_soft_lock`, and `provider_stop_session` until the work is truly complete.
- `provider_set_soft_lock` is intended to be called before a turn ends because the model needs user input and wants LidGuard to release keep-awake protection. The tool itself cannot end the turn; the model still has to stop or hand back the conversation after calling it.
- When resuming a previously soft-locked Provider MCP session after a user reply, the model should call `provider_clear_soft_lock` with the earlier returned `sessionIdentifier` instead of starting a brand-new session.
- Provider MCP behavior is inherently model-dependent. LidGuard cannot guarantee that a model will call these tools at the right times, so this integration should always be documented as best-effort rather than guaranteed.
- MCP settings updates use the same named-pipe client and settings store used by the CLI, but they do not launch `run-server` if no runtime is listening.
- MCP server logging must stay on stderr so stdio tool traffic remains clean.

### Active Session Policy

- Session state is ref-counted by active session.
- `AgentProvider.Mcp` sessions also carry a provider name so multiple MCP-backed providers can reuse the same session identifier without colliding.
- Each session also carries a last activity timestamp plus soft-lock state, reason, and timestamp.
- One or more active sessions keep shared `SystemRequired` and `AwayModeRequired` power requests alive only while at least one active session is not soft-locked.
- When all remaining active sessions are soft-locked, LidGuard treats the runtime as suspend-eligible even before those sessions emit stop events.
- Start/update and provider activity such as new tool execution refresh that session's last activity timestamp. Provider activity also clears that session's current soft-lock state.
- Setting a soft-lock does not refresh last activity, because it represents waiting rather than autonomous work.
- When a session reaches the configured inactive session timeout, LidGuard transitions it to soft-locked with reason metadata and applies the same suspend-eligibility handling as other soft-locked sessions.
- Codex, Claude, and GitHub Copilot CLI sessions use the shared `AgentTranscriptMonitor` implementation for transcript JSONL monitoring. Transcript length growth or `LastWriteTimeUtc` advancement refreshes the session's last activity timestamp and clears the current soft-lock state through the same activity path used by tool events.
- The Codex transcript profile prefers hook-provided `transcript_path` and otherwise falls back to a unique `~/.codex/sessions` match by session id. If the latest transcript record is an `event_msg` whose payload type is `turn_aborted`, LidGuard treats it as an interrupted Codex turn and routes the session through the normal stop path instead of recording activity.
- The Claude transcript profile prefers hook-provided `transcript_path` and otherwise falls back to a unique `~/.claude/projects` match by session id. If the latest transcript record is a `user` record whose text content is `[Request interrupted by user]` or `[Request interrupted by user for tool use]`, LidGuard treats it as an interrupted Claude turn and routes the session through the normal stop path instead of recording activity.
- The GitHub Copilot CLI transcript profile prefers hook-provided `transcriptPath` / `transcript_path` and otherwise falls back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`. If the latest JSONL record has top-level `type` of `abort`, LidGuard treats it as a Copilot abort signal and routes the session through the normal stop path instead of recording activity.
- `AgentProvider.Mcp` sessions do not auto-resolve a watched process from the working directory, because model-managed Provider MCP sessions do not reliably identify one owning CLI process.
- `AgentProvider.Codex` sessions only auto-resolve a watched process from the working directory when no explicit watched process id is supplied and the resolved candidate process is shell-hosted through `cmd.exe`, `pwsh.exe`, or `powershell.exe` as the process itself or its direct parent.
- When a shell-hosted Codex watchdog or `cleanup-orphans` removes sessions by working directory, it removes only watched Codex sessions in that directory and intentionally leaves `process=none` Codex sessions untouched.
- Optional lid action changes are backed up once and restored after the last active session stops.
- While shared protection remains applied and the lid is closed, the Emergency Hibernation thermal monitor polls every 10 seconds and stops automatically once protection is restored or disabled.
- Multiple stop signals for the same session should not cause repeated cleanup side effects.
- When the active session count reaches `0`, the runtime should shut down after the configured server runtime cleanup delay once no post-stop suspend request, lid-action restore, pre-suspend webhook, post-stop sound, or equivalent cleanup work remains pending.
- Persistent pending backup state is still missing and is the next resilience priority.

### Settings Defaults

- Normal idle sleep prevention: enabled.
- Away-mode sleep prevention: enabled.
- Display sleep prevention: disabled.
- Temporary lid close action change: enabled for the headless CLI runtime and applied to AC/DC together.
- Post-stop suspend delay: 10 seconds by default, `0` for immediate suspend.
- Post-stop suspend mode: Sleep by default, Hibernate optional.
- Post-stop suspend sound: off by default.
- Post-stop suspend sound volume override: off by default, accepts 1 through 100 percent, and is rejected rather than clamped when out of range.
- Suspend history recording: on by default, keeps the latest 10 entries, and accepts `off` or an enabled count of at least 1.
- Inactive session timeout: 12 minutes by default, accepts `off` or an enabled minute count of at least 1, and has no product-level maximum.
- Server runtime cleanup delay after all sessions are gone: 10 minutes by default, accepts `off` for immediate exit or an enabled minute count of at least 1, and has no product-level maximum.
- Pre-suspend webhook URL: off by default.
- Emergency Hibernation on high temperature: enabled by default.
- Emergency Hibernation temperature mode: Average by default, with Low and High optional.
- Emergency Hibernation temperature threshold: 93 Celsius by default, clamped to 70 through 110.
- Closed-lid PermissionRequest decision: Deny by default, Allow optional.
- PermissionRequest hooks only emit a structured allow/deny decision when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so the provider's default permission flow continues.
- Claude and GitHub Copilot CLI closed-lid `PermissionRequest` outputs also set `interrupt: true`. Even if another provider later uses a similar JSON shape, keep hook DTOs separate per provider instead of sharing one output type across providers.
- Claude `Elicitation` hooks emit a structured `cancel` only when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so Claude's default elicitation flow continues.
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
  - `LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent`
  - `LidGuardSettings.IsValidSuspendHistoryEntryCount`
  - `LidGuardSettings.IsValidSessionTimeoutMinutes`
  - `LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes`
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
  - `IVisibleDisplayMonitorCountProvider`
  - `IPostStopSuspendSoundPlayer`
  - `ISystemAudioVolumeController`
  - `SystemAudioVolumeState`
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
  - `ServerRuntimeCleanupConfiguration`
- `Control`
  - `LidGuardControlService`
  - `LidGuardControlSnapshot`
  - `LidGuardSessionRemovalOutcome`
  - `LidGuardSettingsPatch`
  - `LidGuardSettingsUpdateOutcome`
- `Runtime`
  - `AgentTranscriptMonitor`
  - `EmergencyHibernationThermalMonitor`
  - `PostStopSuspendSoundPlaybackCoordinator`
  - `SuspendHistoryLogStore`

`LidGuardControlService` loads/saves stored settings and can push updated settings into a running runtime without requiring the CLI entrypoint.

### LidGuard Notifications App

- `Configuration`
  - `LidGuardNotificationsOptions`
- `Data`
  - SQLite connection, schema initialization, subscription storage, webhook event storage, and delivery logging.
- `Services`
  - Web Push sending, webhook API endpoints, and background notification dispatch.
- `Pages`
  - Token login, browser subscription dashboard, and webhook event history.

The notification server is optional and external to the core LidGuard runtime. It receives the existing pre-suspend webhook payload and must keep VAPID private keys on the server only.

### Windows

- `PowerRequestService`
  - Uses `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest`.
  - Supports system-required, away-mode-required, and display-required requests.
- `VisibleDisplayMonitorCountProvider`
  - Starts from `GetSystemMetrics(SM_CMONITORS)`, then uses `WmiMonitorConnectionParams` to exclude inactive monitor connections.
  - Accepts an internal-display exclusion flag used by final suspend eligibility checks, so status and diagnostic monitor counts can still report the active internal laptop panel.
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
- `SystemAudioVolumeController`
  - Uses Windows Core Audio endpoint volume APIs to capture, temporarily apply, and restore the default render output master volume and mute state for post-stop suspend sound playback.
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
  - Exposes `update_settings` for multi-field settings updates in one call, including Emergency Hibernation temperature settings and post-stop suspend sound volume override percent.
  - Exposes `update_settings` for suspend history retention through `suspendHistoryEntryCount`, accepting `off` or an enabled count of at least 1.
  - Exposes `update_settings` for inactive session timeout through `sessionTimeoutMinutes`, accepting `off` or an enabled minute count of at least 1.
  - Exposes `update_settings` for server runtime cleanup delay through `serverRuntimeCleanupDelayMinutes`, accepting `off` for immediate exit or an enabled minute count of at least 1.
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
- Snippet command: `lidguard codex-hooks --format config-toml`.
- Install/status/remove commands: `lidguard hook-install --provider codex`, `lidguard hook-status --provider codex`, and `lidguard hook-remove --provider codex`.
- MCP status/install/remove commands: `lidguard mcp-status --provider codex`, `lidguard mcp-install --provider codex`, and `lidguard mcp-remove --provider codex`.
- Codex may require `features.codex_hooks = true`.
- Codex MCP registration delegates to `codex mcp add/remove` and writes a global stdio server entry named `lidguard`.
- `hook-install` and `hook-status` require `UserPromptSubmit`, `PermissionRequest`, and `Stop`; `SessionEnd` is optional and shown separately when present.
- `codex-hook` reads Codex hook JSON from stdin and maps `hook_event_name` to runtime IPC.
- For `UserPromptSubmit`, it sends internal `start --provider codex`.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a structured allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`.
- For `Stop`, and for `SessionEnd` when a Codex build emits it, it sends internal `stop --provider codex`.
- Notification-driven soft-lock detection is currently unsupported for Codex because the current public hook surface does not expose a comparable `Notification` event. Future support can be added if Codex exposes notification or machine-readable pending-state hooks later.
- Because Codex lacks a notification-style soft-lock clear signal and comparable tool activity hooks, LidGuard records `transcript_path` from `UserPromptSubmit` and treats transcript JSONL length growth or `LastWriteTimeUtc` advancement as Codex provider activity through the shared transcript monitor. That activity refreshes `LastActivityAt` and clears the current soft-lock state through the standard activity path. If `transcript_path` is missing, LidGuard falls back to a unique `~/.codex/sessions` transcript match by session id. The transcript monitor combines file-system change notifications with a short metadata polling fallback. A latest-record `turn_aborted` event is handled as an interrupted turn and stops the tracked Codex session rather than refreshing activity.
- Codex hook input does not provide a stable parent process id. LidGuard therefore prefers an explicit watched process id for Codex, but when none is supplied it can still use a working-directory fallback if the resolved Codex candidate process is shell-hosted through `cmd.exe`, `pwsh.exe`, or `powershell.exe` as the process itself or its direct parent. That cleanup path never removes `process=none` Codex sessions.
- Codex `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Codex task when a runtime request fails.
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
- For `UserPromptSubmit`, it sends internal `start --provider claude` with `transcript_path` when Claude provides one.
- For `PreToolUse`, `PostToolUse`, and non-interrupt `PostToolUseFailure`, it records provider activity and clears the current session soft-lock state for non-`AskUserQuestion` tools.
- For `PostToolUseFailure` with `is_interrupt: true`, it sends internal `stop --provider claude` immediately.
- For `Elicitation`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a structured `cancel` only when the lid is closed and the visible display monitor count is `0`.
- For `Notification`, `permission_prompt` and `elicitation_dialog` mark the session soft-locked, while `elicitation_complete` and `elicitation_response` clear the current soft-lock state.
- Claude transcript JSONL changes are monitored through the same shared transcript monitor used by Codex. If `transcript_path` is missing, LidGuard falls back to a unique `~/.claude/projects` transcript match by session id; a latest user text marker of `[Request interrupted by user]` or `[Request interrupted by user for tool use]` stops the tracked Claude session instead of refreshing activity.
- For `PermissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a Claude-specific structured allow/deny decision with `interrupt: true` from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`.
- When working on Claude Code-related setup, support, or documentation, explicitly and strongly warn the user not to use third-party prompt-style hooks alongside LidGuard. Explain that LidGuard must only answer its own closed-lid `PermissionRequest` and `Elicitation` paths and must not be presented as able to answer or proxy third-party hook prompts.
- For `Stop`, `StopFailure`, and `SessionEnd`, it sends internal `stop --provider claude`.
- The analyzed Claude hook input provides `session_id` and `cwd`, but not a stable parent process id, so the current implementation resolves a process by working directory.
- Claude `Elicitation` exits successfully with structured JSON stdout only for effective closed-lid `cancel`; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.
- Claude `PermissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout. LidGuard records diagnostics locally and should not block the Claude task when a runtime request fails.

Reference:

- https://code.claude.com/docs/en/hooks

### GitHub Copilot CLI

- Start event: `userPromptSubmitted`.
- Stop events: `agentStop`, `sessionEnd`, and session-state JSONL `abort`.
- Closed-lid permission decision event: `permissionRequest`.
- Closed-lid ask-user guard event: `preToolUse` when `toolName` is `ask_user`.
- Activity event: `postToolUse`.
- Soft-lock notification event: `notification` with `notification_type` / `notificationType` of `permission_prompt` or `elicitation_dialog`.
- Telemetry-only events: `sessionStart` and `errorOccurred`.
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
- For `userPromptSubmitted`, it sends internal `start --provider copilot` with `transcriptPath` / `transcript_path` when Copilot provides one.
- For `permissionRequest`, it does not stop the runtime; it queries the runtime lid state and visible display monitor count and returns a GitHub Copilot CLI allow/deny decision from `LidGuardSettings.ClosedLidPermissionRequestDecision` only when the lid is closed and the visible display monitor count is `0`, and it includes `interrupt: true`.
- For `preToolUse`, it does not stop the runtime; it denies `ask_user` only when the lid is closed and the visible display monitor count is `0`, so the agent cannot soft-lock waiting for user input that cannot be answered, and it clears the current session soft-lock state for non-`ask_user` tools.
- For `postToolUse`, it records tool completion activity and clears the current session soft-lock state for non-`ask_user` tools.
- For `notification`, it marks the session soft-locked when GitHub Copilot CLI reports `permission_prompt` or `elicitation_dialog`.
- For `agentStop` and `sessionEnd`, it sends internal `stop --provider copilot`.
- GitHub Copilot CLI session-state JSONL changes are monitored through the shared transcript monitor. If `transcriptPath` / `transcript_path` is missing, LidGuard falls back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`; a latest top-level `type` of `abort` stops the tracked Copilot session instead of refreshing activity. Other JSONL appends or `LastWriteTimeUtc` advancements refresh `LastActivityAt` with reason `github_copilot_session_event_activity_detected` and clear the current soft-lock state.
- For `sessionStart` and `errorOccurred`, it records telemetry only.
- GitHub Copilot CLI hook input currently does not provide a stable parent process id in the documented payloads, so the current implementation resolves a process by working directory.
- GitHub Copilot CLI `permissionRequest` exits successfully with structured JSON stdout only for effective closed-lid decisions; when the lid is open, unknown, any visible display monitor remains active, or runtime status is unavailable, it exits successfully with empty stdout so the normal permission flow continues.
- GitHub Copilot CLI `preToolUse` exits successfully with structured JSON stdout only for effective closed-lid `ask_user` denial; otherwise it exits successfully with empty stdout so normal tool handling continues.

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
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature --temperature-mode high
lidguard suspend-history
lidguard preview-system-sound --name Asterisk
lidguard preview-current-sound
lidguard settings
lidguard settings --emergency-hibernation-temperature-mode average
lidguard settings --change-lid-action true
lidguard settings --post-stop-suspend-delay-seconds 0
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --post-stop-suspend-sound-volume-override-percent 75
lidguard settings --post-stop-suspend-sound-volume-override-percent off
lidguard settings --suspend-history-count 10
lidguard settings --suspend-history-count off
lidguard settings --session-timeout-minutes 12
lidguard settings --session-timeout-minutes off
lidguard settings --server-runtime-cleanup-delay-minutes 10
lidguard settings --server-runtime-cleanup-delay-minutes off
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

## Build Validation Note

- Local build, test, publish, pack, and reinstall validation commands can fail once because of transient Windows Defender file-lock interference.
- Retry the same validation command before taking any broader recovery action; when this specific issue is the cause, a retry is typically enough.
- Do not bring down any build server just because the first validation attempt failed with this known Defender issue.

## Missing Work

The Windows CLI hook receiving path is implemented for Codex, Claude Code, and GitHub Copilot CLI. Remaining work is now focused on lifecycle polish and automated regression coverage.

- Implement immediate runtime shutdown after the last session stops once the remaining post-stop cleanup work is complete.
- Add automated regression tests or verification scripts for the already manually verified provider/Windows behavior: latest Codex hook behavior, Claude Code hook stdout behavior, GitHub Copilot CLI hook output behavior, GitHub Copilot CLI user-level `~/.copilot/hooks/` loading and inline `~/.copilot/settings.json` hook composition, GitHub Copilot CLI session id stability, `PowerReadACValueIndex`/`PowerReadDCValueIndex` read/write behavior under normal user permissions, and Group Policy or MDM blocked power setting fallback messages.
- Add direct Codex soft-lock support only if Codex later exposes a notification or machine-readable pending-state hook surface.

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
- Any future localization work must localize the final human-facing CLI presentation, including runtime/session status messages, session list summaries, management output, enum display text, and placeholders, even when the underlying IPC/log/settings values remain stable English; do not leak raw protocol `Message` text directly into user-facing terminal output when a localized rendering can be produced.
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
