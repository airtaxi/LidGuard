---
name: lidguard-implementation-map
description: "LidGuard implementation map and design constraints. Use when working on repository structure, implemented components, subsystem ownership, Commons, the LidGuard app, notifications, Windows implementations, MCP components, or architectural constraints."
---

# LidGuard Implementation Map

## Repository Shape

- `LidGuard`
  - .NET 10 console app targeting `net10.0`.
  - Standalone hook-facing CLI plus in-process headless runtime and stdio MCP server hosting.
  - Common, platform-neutral models and policies live in feature folders such as `Sessions`, `Settings`, `Power`, `Services`, `Results`, and `Processes`.
  - Shared provider/hook utilities live under `Hooks` in regular `*.cs` files.
  - Windows-specific runtime/process/power implementations live in `*.windows.cs`.
  - Linux-specific systemd/logind, `/proc`, and `/sys` implementations live in `*.linux.cs`.
  - macOS-specific runtime/process/power implementations live in `*.macOS.cs`.
  - Nullable is intentionally not enabled in the csproj.
  - `ImplicitUsings` is enabled.
  - NativeAOT/trimming compatibility flags are enabled.
  - Uses CsWin32 with `CsWin32RunAsBuildTask=true` and `DisableRuntimeMarshalling=true` for AOT compatibility.
  - Uses root namespace `LidGuard` and assembly/apphost name `lidguard`.
  - Prepared for .NET 10 RID-specific NativeAOT .NET tool distribution as NuGet package `lidguard` with tool command `lidguard`.
  - Package RID support is prepared for `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, but the release workflow currently excludes `linux-arm64` because of the NativeAOT cross-linking known issue documented in the .NET Tool Package Guidelines.
  - Windows behavior is implemented.
  - Linux behavior is implemented for systemd/logind systems.
  - macOS behavior is implemented with `caffeinate`, `pmset`, `ioreg`, `system_profiler`, Apple Silicon `IOHIDEventSystemClient` temperature sensors, `powermetrics`, and `osascript`/`afplay` where needed.
  - Uses a named pipe to send `start`, `stop`, `remove-session`, `status`, `settings`, and `cleanup-orphans` requests to the runtime.
  - Hosts the stdio MCP server through the `mcp-server` subcommand.
  - Stores default settings JSON under the platform local application data directory, such as `%LOCALAPPDATA%\LidGuard\settings.json` on Windows, `~/.local/share/LidGuard/settings.json` on typical Linux desktops, or the .NET local application data path on macOS.
- `LidGuard.Notifications`
  - .NET 10 ASP.NET Core Razor Pages app targeting `net10.0`.
  - Receives LidGuard pre-suspend and post-session-end webhooks and sends browser Web Push notifications to subscribed clients.
  - Stores subscriptions, webhook events, and delivery attempts in SQLite.
  - Uses server-side VAPID settings; VAPID private keys and access tokens must never be committed.
- `LidGuard.slnx`
  - Root solution file including `LidGuard` and `LidGuard.Notifications`.

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

The notification server is optional and external to the core LidGuard runtime. It receives pre-suspend and post-session-end webhook payloads that include `eventType`, and must keep VAPID private keys on the server only.
The notification server's human-facing web UI, browser subscription status text, and Web Push notification title/body text must support the same localization language set as the core LidGuard UI. `UserInterfaceCulture` uses `auto` by default, accepts `en`, `ko`, or any `CultureInfo`-resolvable culture name, and `LIDGUARD_UI_CULTURE` overrides the configured value for testing and support.
If the CLI webhook option names `--pre-suspend-webhook-url` or `--post-session-end-webhook-url` change, update the LidGuard Notifications dashboard command examples in the same change.

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
  - Reports unsupported platforms before platform services are constructed.
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
  - Exposes `update_settings` for server runtime cleanup delay through `serverRuntimeCleanupDelayMinutes`, accepting `off` to keep the runtime alive, `0` for immediate exit, or a positive minute count to wait.
  - Exposes `update_settings` for post-session-end webhook URL through `postSessionEndWebhookUrl`.
  - Exposes `remove_session` for manual active-session deletion by session identifier, with optional provider and MCP provider-name filters.
  - Exposes `set_session_soft_lock` and `clear_session_soft_lock` for provider/session-targeted soft-lock control.
- `LidGuardProviderMcpTools`
  - Exposes `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, and `provider_clear_soft_lock` for model-managed Provider MCP integrations.
- `LidGuard` MCP hosting
  - Uses `WithStdioServerTransport()` and `WithTools<LidGuardSettingsMcpTools>()` from the official C# SDK.
  - Keeps host logging on stderr so MCP stdio responses stay valid.

## Design Constraints

- Keep cross-platform-capable logic in regular `LidGuard` feature folders and namespaces.
- Keep Windows API calls and Windows-only assumptions in `LidGuard` `*.windows.cs` files.
- Do not enable Nullable in `LidGuard.csproj` unless the user explicitly asks.
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
