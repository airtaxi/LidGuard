---
name: lidguard-product-runtime
description: "LidGuard product and runtime behavior reference. Use when working on product goals, power control, lid and suspend policy, Emergency Hibernation, process watching, CLI commands, settings, MCP server behavior, active sessions, examples, or failure modes."
---

# LidGuard Product Runtime

## Product Goal

LidGuard is a Windows-first utility with systemd/logind Linux support and macOS support for long-running local AI coding agents such as Codex, Claude Code, and GitHub Copilot CLI.

The goal is to keep the supported local system awake while at least one tracked agent session still needs protection, then restore the user's original power policy after the session ends or becomes suspend-eligible.

- Agent sessions start through provider hooks.
- LidGuard detects and tracks active sessions.
- Claude Code and GitHub Copilot CLI sessions can enter a runtime-managed soft-lock state when provider notifications show the agent is waiting on user input.
- While at least one non-soft-locked session is active, Windows should not enter idle sleep through `PowerRequestSystemRequired` and `PowerRequestAwayModeRequired`, Linux should hold systemd/logind sleep and idle inhibitors, and macOS should hold `caffeinate -i` assertions that work on AC and DC power.
- If every remaining active session is soft-locked, LidGuard should release temporary keep-awake protection, restore any temporary lid policy change, and start the configured suspend flow only when the lid is closed and no suspend-blocking visible display monitors remain attached to the desktop.
- If a session has no activity after the configured session timeout, LidGuard should transition it to the soft-locked state and apply the same keep-awake release flow used for normal soft-lock operations.
- Optional settings temporarily change the active power plan's lid close action to `Do Nothing` on Windows, hold a `handle-lid-switch` inhibitor on Linux, or temporarily set `pmset -a disablesleep 1` on macOS.
- When sessions stop, all temporary power settings must be restored to the user's original values.
- After the last active session stops, LidGuard should always request suspend when the laptop lid is closed and no suspend-blocking visible display monitors remain attached to the desktop.
- Once the active session count reaches `0`, the server runtime should exit after the configured server runtime cleanup delay once any in-flight suspend or cleanup work finishes. The delay defaults to 10 minutes, `0` means exit immediately after in-flight work finishes, and `off` disables automatic runtime exit so the runtime stays alive.
- If active sessions remain but all of them are soft-locked, LidGuard should follow the same suspend path without waiting for stop hooks.
- The suspend mode remains user-selectable: Sleep by default, Hibernate optional.
- The post-stop suspend delay remains user-selectable: 10 seconds by default, `0` for immediate suspend.
- The post-stop suspend sound remains optional: off by default, with supported SystemSounds names or a playable `.wav` path.
- The post-stop suspend sound volume override remains optional: off by default, with an allowed master volume range of 1 through 100 percent.
- The inactive session timeout remains user-selectable: 12 minutes by default, `off` optional, and enabled values must be at least 1 minute.
- An optional post-session-end webhook URL remains off by default. When a provider reports a normal session end and that stop does not schedule the pre-suspend flow, LidGuard should POST a `PostSessionEnd` payload without blocking session cleanup. If the stop schedules the pre-suspend flow but that flow is canceled before the pre-suspend webhook is attempted, LidGuard should fall back to the same `PostSessionEnd` payload. Abort, interrupt, manual stop/remove, watchdog, and orphan cleanup paths must not send it.
- While keep-awake protection is applied and the laptop lid is closed with no suspend-blocking visible display monitors remaining on the desktop, an optional Emergency Hibernation thermal monitor should poll every 10 seconds and request immediate hibernation when the system temperature reaches the configured threshold.

The key design rule is to treat normal idle sleep and lid-close sleep as separate problems. Power requests, systemd inhibitors, or `caffeinate` handle idle sleep. Windows `LIDACTION` policy backup/change/restore, Linux `handle-lid-switch` inhibition, and macOS `pmset disablesleep` backup/change/restore handle lid-close behavior because standard sleep-prevention APIs cannot reliably block a user lid-close action.

## Technical Design

### Windows Power Control

- Use `PowerCreateRequest`, `PowerSetRequest`, and `PowerClearRequest` for normal idle sleep prevention.
- Use `PowerRequestSystemRequired` to prevent idle system sleep.
- Use `PowerRequestAwayModeRequired` to request away-mode behavior where supported.
- Keep `PowerRequestDisplayRequired` optional; it is only needed when display sleep should also be prevented.
- Always clear power requests and close handles when protection ends.
- Do not change sleep idle timeouts. That approach was rejected because runtime crashes could leave the user's system policy in a dangerous state.

### Linux Power Control

- Linux support targets systemd/logind environments.
- Use `systemd-inhibit` block inhibitors for normal sleep and idle prevention.
- `PreventSystemSleep` maps to the `sleep` inhibitor.
- `PreventSystemSleep` and `PreventDisplaySleep` map to the `idle` inhibitor.
- `PreventAwayModeSleep` is Windows-only and must not affect Linux inhibitor selection or Linux settings/help output.
- Temporary lid-close protection maps to a separate `handle-lid-switch` inhibitor only when `ChangeLidAction` is enabled.
- `SystemdInhibitor` keeps the helper process alive by running a shell that waits on stdin, then normally releases the inhibitor by closing stdin; process-tree kill remains the fallback if the helper does not exit promptly. Do not switch this to `sleep infinity` unless the normal release path is intentionally changed to rely on process termination.
- Inhibitor helper processes must be tied to the LidGuard runtime lifetime so they do not survive as stale orphaned inhibitors.
- Immediate sleep/hibernate uses `systemctl suspend` or `systemctl hibernate` and returns the command exit code/stderr when the request fails.
- Linux remains a supported top-level platform when `OperatingSystem.IsLinux()` is true, and missing or incomplete systemd/logind prerequisites are reported by the specific runtime or diagnostic operation. This keeps `linux-permission status|check` and `/proc`/`/sys` diagnostics usable in partial environments such as WSL.
- Linux runtime operation should not depend on a long-lived `sudo -v` cache. Persistent privileged logind actions should be prepared through the Linux polkit rule command.

### macOS Power Control

- macOS support targets local macOS systems with `caffeinate`, `pmset`, `ioreg`, `system_profiler`, Apple Silicon `IOHIDEventSystemClient` temperature sensors, and best-effort `powermetrics`.
- The macOS Apple Silicon temperature fast path is allowed to use direct source-generated `LibraryImport` calls into CoreFoundation and IOKit, plus `NativeLibrary` export lookup for CoreFoundation callback tables, because CsWin32 does not cover macOS frameworks and no stable managed metadata surface exists for `IOHIDEventSystemClient`.
- Use `caffeinate` assertions for normal idle sleep prevention.
- `PreventSystemSleep` maps to `caffeinate -i`.
- `PreventDisplaySleep` maps to `caffeinate -d`.
- Do not use `caffeinate -s` for LidGuard's cross-platform `PreventAwayModeSleep` setting. Away-mode sleep prevention is Windows-only, and macOS sleep prevention must rely on `caffeinate -i` so it works on both AC and DC power.
- Temporary lid-close protection maps to `pmset -a disablesleep 1` only when `ChangeLidAction` is enabled.
- Before changing `SleepDisabled`, save the original state through the pending lid-action backup JSON path; restore it when protection ends or during the next CLI recovery path before normal command execution.
- Immediate Sleep uses `pmset sleepnow`.
- Hibernate temporarily backs up the current supported `hibernatemode`, writes `hibernatemode 25`, requests `pmset sleepnow`, and leaves the pending hibernatemode backup for the next CLI recovery path instead of restoring it immediately after `pmset sleepnow` returns. If `pmset sleepnow` fails, LidGuard must roll back the hibernatemode immediately when possible.
- macOS privileged runtime operations should use non-interactive `sudo -n` only for the exact commands allowed by `macos-permission install`; they must fail with actionable setup guidance instead of prompting in background runtime paths.

### Lid Close Policy

- The lid close setting is Windows power setting `LIDACTION`.
- Subgroup GUID: `4f971e89-eebd-4455-a8de-9e59040e7347`.
- Setting GUID: `5ca83367-6e45-459f-a27b-476b1d01c936`.
- Values are `0 = Do Nothing`, `1 = Sleep`, `2 = Hibernate`, `3 = Shut Down`.
- Read AC/DC values from the active power scheme together before making changes.
- During active sessions, write AC/DC values to `0 = Do Nothing` when the setting is enabled.
- After the last active session stops, restore the backed-up AC/DC values.
- v1 restores the scheme that was active at backup time. Future work may add policy for active scheme changes while LidGuard is running.
- On Linux, `ChangeLidAction=true` means LidGuard holds a systemd/logind `handle-lid-switch` inhibitor while protection is applied. It does not edit distribution power configuration files.
- On macOS, `ChangeLidAction=true` means LidGuard temporarily applies `pmset -a disablesleep 1` while protection is applied, records the original `SleepDisabled` value in pending backup state, and restores it after protection ends or during crash recovery.

### Lid State And Suspend

- Lid open/close notification uses `GUID_LIDSWITCH_STATE_CHANGE`.
- Broadcast values are `0x0 = lid closed` and `0x1 = lid opened`.
- `LidSwitchNotificationRegistration` converts these values to `LidSwitchState`.
- Closed-lid policy decisions start from `GetSystemMetrics(SM_CMONITORS)` and exclude inactive monitor connections reported by Windows WMI. The final suspend eligibility check also excludes internal laptop panel connections while `LidSwitchState` is `Closed`; LidGuard only treats the machine as suspend-eligible for lid-close policy when the resulting visible display monitor count is `0`.
- Immediate sleep/hibernate uses `SetSuspendState` after enabling `SeShutdownPrivilege`.
- On Modern Standby systems, `SetSuspendState(false, ...)` can fail with `ERROR_NOT_SUPPORTED`; a later fallback may use a display-off strategy.
- Linux lid state reads `/proc/acpi/button/lid/*/state` and reports `Unknown` when no readable lid state exists.
- Linux visible display monitor count reads `/sys/class/drm/*/status` and counts `connected` connectors. The final suspend eligibility check excludes internal connector families such as `eDP`, `LVDS`, and `DSI` while `LidSwitchState` is `Closed`.
- Linux immediate sleep/hibernate uses `systemctl suspend` or `systemctl hibernate`.
- macOS lid state reads `ioreg` clamshell state and reports `Unknown` when no readable state exists.
- macOS visible display monitor count reads `system_profiler SPDisplaysDataType -json`. The final suspend eligibility check excludes built-in/internal display entries while `LidSwitchState` is `Closed`.
- macOS immediate sleep uses `pmset sleepnow`; macOS hibernate uses the temporary `hibernatemode 25` flow described above.
- After the last active session stops, LidGuard should request suspend after the configured post-stop delay using the configured suspend mode only when the lid is closed and the suspend eligibility visible display monitor count is `0`. A delay of `0` means immediate suspend.
- If a post-stop suspend sound is configured, LidGuard should wait for the delay first, send the pre-suspend webhook when configured, then play the configured sound to completion, then re-check the lid/session state before requesting suspend.
- If a post-stop suspend sound volume override is configured, LidGuard should capture the default output device master volume and mute state immediately before playback, temporarily unmute as needed, set the configured master volume percent for playback, then restore the previous volume and mute state in the sound playback cleanup path.
- If a pre-suspend webhook URL is configured, LidGuard should POST JSON with a 5-second timeout after the post-stop suspend delay and before post-stop suspend sound playback. The body must include `eventType = PreSuspend` and `reason`, and soft-lock-triggered suspend must also include the soft-locked session count. When a provider-reported normal session end schedules suspend and therefore suppresses the separate `PostSessionEnd` webhook, the `PreSuspend` body must also include the same provider/session identity, UTC start/activity/end timestamps, end reason metadata, active session count, working directory, transcript path, `inputPromptPreview`, and `lastResponse` fields that the `PostSessionEnd` webhook would have carried when available. If the pending suspend is canceled before the pre-suspend webhook is attempted, the suppressed normal session-end notification should fall back to `PostSessionEnd` when that webhook URL is configured. Notification receivers should reject webhook payloads that omit `eventType`.
- If a post-session-end webhook URL is configured, LidGuard should POST JSON with a 5-second timeout after a provider-reported normal session end when that stop does not schedule suspend, or when the scheduled suspend is canceled before the pre-suspend webhook is attempted. The body must include `eventType = PostSessionEnd`, `reason = SessionEnded`, provider/session identity, UTC start/activity/end timestamps, end reason metadata, active session count, working directory, transcript path when available, one-line `inputPromptPreview` when available, and full `lastResponse` when available. Prompt preview values must normalize `\r\n` and `\r` to `\n`, replace line breaks with spaces, and trim overlong text to 50 characters with `...` using a word boundary when possible. Notification event lists and push text should derive a 50-character preview from `lastResponse`, while the event details UI should expose the full response.

### Emergency Hibernation Thermal Monitor

- Emergency Hibernation uses `SystemThermalInformation.GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode)` to read the selected available system thermal-zone temperature in Celsius.
- On Linux, Emergency Hibernation temperature reads millidegree Celsius values from `/sys/class/thermal/thermal_zone*/temp` and applies the configured Low, Average, or High aggregation.
- On macOS, Emergency Hibernation temperature first uses best-effort Apple Silicon `IOHIDEventSystemClient` processor temperature sensors, then falls back to `powermetrics --samplers smc` Celsius sensor output, and applies the configured Low, Average, or High aggregation. Unsupported sensors, permission failures, unsupported samplers, missing numeric Celsius values, and timeouts must report unavailable and must not trigger Emergency Hibernation.
- Emergency Hibernation temperature mode is configurable as Low, Average, or High, and defaults to Average.
- The thermal monitor only runs while shared keep-awake protection is applied, the lid is closed, and the suspend eligibility visible display monitor count is `0`.
- The thermal poll interval is fixed at 10 seconds.
- The Emergency Hibernation threshold is configurable, defaults to 93 Celsius, and must always be clamped to 70 through 110 Celsius before runtime use.
- When the observed temperature reaches the clamped threshold, LidGuard should cancel any pending post-stop suspend, send the pre-suspend webhook with `reason = EmergencyHibernation` using a 5-second timeout, then immediately request hibernate. If the hibernate request fails, LidGuard should immediately request Sleep as a best-effort fallback and record both suspend results.
- Emergency Hibernation ignores the regular suspend mode, post-stop suspend delay, post-stop suspend sound, and sound volume override settings.
- Emergency Hibernation webhook timeout or failure must not block the immediate hibernation request.

### Process Exit Watcher

Hook stop events may be missed, so LidGuard also watches the agent process.

- Prefer a provided parent process id when hooks can supply one.
- When parent process id is missing, use `ICommandLineProcessResolver` with the hook working directory only for providers where that fallback is reliable enough. Codex is the main exception: allow the implicit fallback only when the resolved Codex candidate process or its direct parent is a platform-approved shell, and treat `process=none` Codex sessions as out of scope for that working-directory cleanup path.
- On Windows, open the target process with synchronize/query rights and wait with `WaitForSingleObject`.
- On Linux, use `/proc/<pid>/cwd`, `/proc/<pid>/comm`, `/proc/<pid>/cmdline`, and `/proc/<pid>/stat` for working-directory process resolution, and use `Process.GetProcessById().WaitForExitAsync()` for process exit watching.
- On Linux, Codex shell-host fallback is allowed only when the resolved candidate process or its direct parent is `bash`, `zsh`, `fish`, `sh`, `dash`, or `pwsh`.
- On macOS, use `ps` plus `lsof` current-working-directory inspection for working-directory process resolution, and use `Process.GetProcessById().WaitForExitAsync()` for process exit watching.
- On macOS, Codex shell-host fallback is allowed only when the resolved candidate process or its direct parent is `zsh`, `bash`, `fish`, `sh`, or `pwsh`.
- Treat the first cleanup signal as authoritative; later stop/watchdog events for the same session should be harmless.
- If a provider launches a short-lived wrapper that exits before the real agent, provider-specific process selection may need follow-up work.

## Runtime Behavior

### Current CLI Path

- `LidGuard` parses `help`, `start`, `stop`, `remove-pre-suspend-webhook`, `remove-post-session-end-webhook`, `remove-session`, `status`, `live-status`, `settings`, `cleanup-orphans`, `current-lid-state`, `current-monitor-count`, `current-temperature`, `suspend-history`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `mcp-status`, `mcp-install`, `mcp-remove`, `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`, `preview-system-sound`, `preview-current-sound`, `mcp-server`, and `provider-mcp-server`.
- Linux builds additionally parse `linux-permission status`, `linux-permission check`, `linux-permission install`, and `linux-permission remove`. Windows builds must not include this command in routing or help output.
- macOS builds additionally parse `macos-permission status`, `macos-permission check`, `macos-permission install`, and `macos-permission remove`. Windows and Linux builds must not include this command in routing or help output.
- `help` prints a categorized command overview with short descriptions, and `help <command>` prints focused detailed help for one command or recognized command alias.
- `<command> --help` uses the same help metadata and returns before the target command validates options or performs command-specific work.
- `start`, the `UserPromptSubmit` path in `codex-hook` and `claude-hook`, and the `userPromptSubmitted` path in `copilot-hook` load persisted default settings and send them with the start IPC request.
- `remove-session --all` manually removes every active session currently tracked by the runtime.
- `remove-session` manually removes active sessions by session identifier; when `--provider` is omitted, it removes every active session whose session identifier matches. When `--provider mcp` is used, `--provider-name` can narrow the removal to one MCP-backed provider; omitting `--provider-name` removes every MCP-backed session that shares that session identifier.
- `remove-pre-suspend-webhook` clears the configured pre-suspend webhook URL and reports when no webhook is currently configured.
- `remove-post-session-end-webhook` clears the configured post-session-end webhook URL and reports when no webhook is currently configured.
- `current-lid-state` prints the current lid switch state using the same platform source LidGuard uses for closed-lid policy decisions.
- `current-monitor-count` prints the current visible display monitor count using the same base platform monitor visibility check LidGuard uses for closed-lid policy decisions, without the internal-display exclusion used by final suspend eligibility checks.
- `current-temperature` prints the currently recognized system thermal-zone temperature in Celsius using the selected aggregation mode, or reports when thermal-zone data is unavailable.
- `suspend-history` prints recent suspend request history from the platform local application data directory, including mode, reason, result, active session count, and related session or Emergency Hibernation temperature details when available.
- `live-status` opens an event-driven runtime IPC subscription without starting the runtime when unavailable. The runtime immediately pushes an initial snapshot, then pushes updated snapshots when runtime state, runtime logs, hook logs, or suspend history change. The fixed terminal dashboard shows active sessions, lid state, the same visible display monitor count used by `status`, pending suspend details, recent provider hook `received` / `runtime-result` lines, runtime flow events, and recent suspend history results. When the runtime is unavailable or the stream disconnects, interactive `live-status` keeps the dashboard open and periodically reconnects until the user exits.
- `status`, `live-status`, `suspend-history`, and `hook-events` display persisted timestamps in the current system local time while the underlying session, history, and hook log stores remain UTC.
- `settings` prints and updates default settings, and updates a running runtime when one is listening.
- `settings` also exposes `--emergency-hibernation-on-high-temperature`, `--emergency-hibernation-temperature-mode`, and `--emergency-hibernation-temperature-celsius`; the threshold option accepts 70 through 110 only.
- `settings` exposes `--post-stop-suspend-sound-volume-override-percent off|<1-100>` for temporary post-stop sound playback master volume override; `off` disables it and out-of-range values are rejected.
- `settings` exposes `--suspend-history-count off|<count>` for recent suspend history retention; `off` disables recording and enabled counts must be at least 1.
- `settings` exposes `--session-timeout-minutes off|<minutes>` for inactive session timeout soft-locking; `off` disables timeout soft-locking and enabled values must be at least 1.
- `settings` exposes `--server-runtime-cleanup-delay-minutes off|0|<minutes>` for server runtime cleanup after all active sessions are gone and pending cleanup is finished; `off` disables automatic runtime exit, `0` exits immediately, and positive values wait that many minutes.
- `settings` exposes `--post-session-end-webhook-url <http-or-https-url>` for normal provider session-end notifications that do not schedule suspend or whose scheduled suspend is canceled before the pre-suspend webhook is attempted.
- `settings` exposes `--ui-culture auto|en|ko|<culture-name>` for human CLI UI localization. `auto` uses the process or operating system UI culture, and the `LIDGUARD_UI_CULTURE` environment variable overrides the stored setting for testing, support, and scripted use.
- UI culture selection affects human CLI presentation and generated managed hook `statusMessage` text. Command names, option names, IPC payloads, hook stdout JSON, MCP tool names, MCP JSON response property names, settings JSON property names, generated provider configuration keys, command paths, protocol values, and persisted JSONL log structure remain stable and non-localized.
- When `settings` changes UI culture, LidGuard refreshes `statusMessage` text only in already installed default managed provider hooks. This refresh must detect LidGuard hook command entries rather than replacing a whole marked config region, and it must not install missing hooks.
- On Windows, when the effective UI culture is not English, LidGuard sets console input and output encoding to UTF-8 before writing human CLI text so localized non-ASCII output such as Korean does not degrade to question marks. This must not change hook or MCP JSON protocol text.
- `preview-system-sound` and `preview-current-sound` apply the saved post-stop suspend sound volume override setting and wait until playback finishes. `preview-current-sound` plays the saved post-stop suspend sound and prints setup guidance when no sound is configured.
- `hook-install`, `hook-status`, `hook-remove`, and `hook-events` prompt for `codex`, `claude`, `copilot`, or `all` when `--provider` is omitted.
- `mcp-status`, `mcp-install`, and `mcp-remove` prompt for `codex`, `claude`, `copilot`, or `all` when the provider positional value is omitted.
- `provider-mcp-status`, `provider-mcp-install`, and `provider-mcp-remove` work on a caller-supplied JSON config file path instead of using Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration commands.
- `--provider all` installs, removes, checks, or prints hook events only for providers whose default configuration roots already exist, and reports missing providers as skipped.
- `mcp-status all`, `mcp-install all`, and `mcp-remove all` only process providers whose default configuration roots already exist, and report missing providers as skipped.
- When adding a new CLI command that takes a provider parameter, make omitted provider values prompt the user instead of silently defaulting.
- When no runtime is listening, `start` launches detached `run-server`.
- `run-server` acquires the named mutex `Local\LidGuard.Runtime.v1` on Windows and `LidGuard.Runtime.v1` on Linux.
- macOS uses the same non-Windows named mutex name as Linux: `LidGuard.Runtime.v1`.
- `run-server` is detached from inherited stdout/stderr so hook callers do not hang while reading child process output.
- When the Linux CLI is run through the `dotnet` host during framework-dependent validation, automatic runtime launch must pass the built `lidguard.dll` path before `run-server`. Linux detached runtime launch prefers `setsid`, falls back to `nohup`, and redirects stdin/stdout/stderr to `/dev/null`.
- Runtime communication uses a local named pipe.
- Session execution events are logged as JSON lines under the platform local application data directory, keeping the latest 500 entries. Timeout-triggered soft-lock transitions are logged as `session-timeout-softlock-recorded`.
- First-chance, unhandled, and unobserved task exceptions are appended under the platform local application data directory at `log/exceptions.log`, including inner exception details. Unobserved task exceptions must be marked observed as part of that handling.
- Recent suspend request history is logged as JSON lines under the platform local application data directory, keeping the latest configured entry count when enabled.
- Provider hook event logs record the `prompt` field on received start events: Codex and Claude `UserPromptSubmit`, and GitHub Copilot CLI `userPromptSubmitted`.
- Default settings are stored under the platform local application data directory.

### Linux Permission Command

- `linux-permission` is compiled only into Linux builds and must not appear in Windows routing or help.
- `linux-permission status` reports the current user, managed polkit rule state, `systemd-inhibit` availability, `systemctl` availability, and logind `CanSuspend` / `CanHibernate` capability values when queryable.
- `linux-permission check` non-destructively verifies inhibitor acquire/release, `systemctl --version`, and logind suspend/hibernate capability queries. It must not request an actual suspend or hibernate.
- `linux-permission install` writes `/etc/polkit-1/rules.d/49-lidguard.rules` using root privileges or one-time `sudo`, and grants only the current target user the logind actions LidGuard needs.
- The managed polkit rule allows only `org.freedesktop.login1.suspend`, `suspend-multiple-sessions`, `hibernate`, `hibernate-multiple-sessions`, `inhibit-block-sleep`, `inhibit-block-idle`, and `inhibit-handle-lid-switch`.
- `linux-permission remove` deletes the rule only when the file contains LidGuard's managed markers. It must refuse to delete unmanaged files.
- Do not add a `sudo -v` prepare command; sudo credential caches are tty/timeout scoped and are not a reliable basis for long-running agent protection.

### macOS Permission Command

- `macos-permission` is compiled only into macOS builds and must not appear in Windows or Linux routing or help.
- `macos-permission status` reports the current user, managed sudoers rule state when directly inspectable, privileged same-value `pmset` usability, `caffeinate`, `pmset`, `powermetrics`, `ioreg`, and `system_profiler` availability, plus current `SleepDisabled` and `hibernatemode` values when queryable. It must not use `sudo cat` or any command outside the managed sudoers allowlist to inspect sudoers content.
- `macos-permission check` non-destructively verifies `caffeinate` acquire/release, `pmset -g`, same-value privileged `pmset disablesleep`, same-value privileged `pmset hibernatemode`, and one privileged `powermetrics --samplers smc` sample. It must not request an actual sleep or hibernate.
- `macos-permission install` writes `/private/etc/sudoers.d/lidguard` using root privileges or one-time `sudo`, validates the generated rule with `visudo -cf`, and grants only the current target user the exact privileged commands LidGuard needs.
- The managed sudoers rule allows only `/usr/bin/pmset -a disablesleep 0`, `/usr/bin/pmset -a disablesleep 1`, `/usr/bin/pmset -a hibernatemode 0`, `/usr/bin/pmset -a hibernatemode 3`, `/usr/bin/pmset -a hibernatemode 25`, and `/usr/bin/powermetrics --samplers smc -n 1 -i 1000`.
- `macos-permission remove` deletes the sudoers file only when the file contains LidGuard's managed markers. It must refuse to delete unmanaged files.
- Do not add a `sudo -v` prepare command; sudo credential caches are tty/timeout scoped and are not a reliable basis for long-running agent protection.

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
- `update_settings` exposes server runtime cleanup delay through `serverRuntimeCleanupDelayMinutes`, accepting `off` to keep the runtime alive, `0` for immediate exit, or a positive minute count to wait.
- `update_settings` exposes post-session-end webhook URL through `postSessionEndWebhookUrl`, accepting an empty string to clear it.
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
- One or more active sessions keep shared platform keep-awake protection alive only while at least one active session is not soft-locked.
- When all remaining active sessions are soft-locked, LidGuard treats the runtime as suspend-eligible even before those sessions emit stop events.
- Start/update and provider activity such as new tool execution refresh that session's last activity timestamp. Provider activity also clears that session's current soft-lock state.
- Setting a soft-lock does not refresh last activity, because it represents waiting rather than autonomous work.
- When a session reaches the configured inactive session timeout, LidGuard transitions it to soft-locked with reason metadata and applies the same suspend-eligibility handling as other soft-locked sessions.
- Codex, Claude, and GitHub Copilot CLI sessions use the shared `AgentTranscriptMonitor` implementation for transcript JSONL monitoring. Transcript length growth or `LastWriteTimeUtc` advancement normally refreshes the session's last activity timestamp and clears the current soft-lock state through the same activity path used by tool events, unless a provider-specific transcript detector reports a stop or soft-lock signal first.
- The Codex transcript profile prefers hook-provided `transcript_path` and otherwise falls back to a unique `~/.codex/sessions` match by session id. If the latest transcript record is an `event_msg` whose payload type is `turn_aborted`, LidGuard treats it as an interrupted Codex turn and routes the session through the normal stop path instead of recording activity. Otherwise, if recent transcript records contain a pending `response_item` `function_call` named `request_user_input` without a matching `function_call_output` for the same `call_id`, LidGuard marks the Codex session soft-locked with reason `codex_transcript_request_user_input_pending`.
- The Claude transcript profile prefers hook-provided `transcript_path` and otherwise falls back to a unique `~/.claude/projects` match by session id. If the latest transcript record is a `user` record whose text content is `[Request interrupted by user]` or `[Request interrupted by user for tool use]`, LidGuard treats it as an interrupted Claude turn and routes the session through the normal stop path instead of recording activity.
- The GitHub Copilot CLI transcript profile prefers hook-provided `transcriptPath` / `transcript_path` and otherwise falls back to `COPILOT_HOME\session-state\<sessionId>\events.jsonl` or `%USERPROFILE%\.copilot\session-state\<sessionId>\events.jsonl`. If the latest JSONL record has top-level `type` of `abort`, LidGuard treats it as a Copilot abort signal and routes the session through the normal stop path instead of recording activity.
- `AgentProvider.Mcp` sessions do not auto-resolve a watched process from the working directory, because model-managed Provider MCP sessions do not reliably identify one owning CLI process.
- `AgentProvider.Codex` sessions only auto-resolve a watched process from the working directory when no explicit watched process id is supplied and the resolved candidate process is shell-hosted through a platform-approved shell as the process itself or its direct parent. Approved shells are `cmd.exe`, `pwsh.exe`, and `powershell.exe` on Windows, and `bash`, `zsh`, `fish`, `sh`, `dash`, and `pwsh` on Linux.
- If an active Codex session has ever had a watched process and a later Codex start for the same session can no longer resolve one, LidGuard treats that as a canceled/lost watched process session and removes it without sending the normal provider session-end webhook.
- When a shell-hosted Codex watchdog or `cleanup-orphans` removes sessions by working directory, it removes only watched Codex sessions in that directory and intentionally leaves `process=none` Codex sessions untouched.
- Optional lid action changes are backed up once and restored after the last active session stops.
- While shared protection remains applied and the lid is closed, the Emergency Hibernation thermal monitor polls every 10 seconds and stops automatically once protection is restored or disabled.
- Multiple stop signals for the same session should not cause repeated cleanup side effects.
- When the active session count reaches `0`, the runtime should shut down after the configured server runtime cleanup delay once no post-stop suspend request, lid-action restore, pre-suspend webhook, post-session-end webhook, post-stop sound, or equivalent cleanup work remains pending, unless automatic runtime exit is disabled.
- When a provider-reported normal stop removes an active session and no suspend is scheduled from that stop, or when the scheduled suspend is canceled before the pre-suspend webhook is attempted, the runtime sends the post-session-end webhook in the background, keeps runtime cleanup pending until that send finishes or times out, and logs webhook failures without failing the stop.
- Persistent pending backup state is still missing and is the next resilience priority.

### Settings Defaults

- Normal idle sleep prevention: enabled.
- Away-mode sleep prevention: enabled on Windows only; Linux and macOS builds do not expose this setting and normalize it to disabled.
- Display sleep prevention: disabled.
- Temporary lid close action change: enabled for the headless CLI runtime and applied to AC/DC together.
- Post-stop suspend delay: 10 seconds by default, `0` for immediate suspend.
- Post-stop suspend mode: Sleep by default, Hibernate optional.
- Post-stop suspend sound: off by default.
- Post-stop suspend sound volume override: off by default, accepts 1 through 100 percent, and is rejected rather than clamped when out of range.
- Suspend history recording: on by default, keeps the latest 10 entries, and accepts `off` or an enabled count of at least 1.
- Inactive session timeout: 12 minutes by default, accepts `off` or an enabled minute count of at least 1, and has no product-level maximum.
- Server runtime cleanup delay after all sessions are gone: 10 minutes by default, accepts `off` to disable automatic runtime exit, `0` for immediate exit, or a positive minute count to wait, and has no product-level maximum.
- Pre-suspend webhook URL: off by default.
- Post-session-end webhook URL: off by default.
- Emergency Hibernation on high temperature: enabled by default.
- Emergency Hibernation temperature mode: Average by default, with Low and High optional.
- Emergency Hibernation temperature threshold: 93 Celsius by default, clamped to 70 through 110.
- Closed-lid PermissionRequest decision: Deny by default, Allow optional.
- User interface culture: `auto` by default, with `en`, `ko`, or any `CultureInfo`-resolvable BCP 47 culture name optional.
- PermissionRequest hooks only emit a structured allow/deny decision when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so the provider's default permission flow continues.
- Claude and GitHub Copilot CLI closed-lid `PermissionRequest` outputs also set `interrupt: true`. Even if another provider later uses a similar JSON shape, keep hook DTOs separate per provider instead of sharing one output type across providers.
- Claude `Elicitation` hooks emit a structured `cancel` only when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so Claude's default elicitation flow continues.
- Parent process watchdog: enabled.

## CLI Examples

```powershell
lidguard start --provider codex --session "<session-id>" --parent-pid 1234
lidguard stop --provider codex --session "<session-id>"
lidguard remove-pre-suspend-webhook
lidguard remove-post-session-end-webhook
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard start --provider claude --session "<session-id>"
lidguard stop --provider claude --session "<session-id>"
lidguard claude-hook
lidguard claude-hooks settings-json
lidguard start --provider copilot --session "<session-id>"
lidguard stop --provider copilot --session "<session-id>"
lidguard copilot-hook --event userPromptSubmitted
lidguard copilot-hooks config-json
lidguard codex-hook
lidguard codex-hooks config-toml
lidguard hook-status --provider copilot
lidguard hook-install --provider copilot
lidguard hook-remove --provider copilot
lidguard hook-events --provider copilot --count 50
lidguard mcp-status copilot
lidguard mcp-install copilot
lidguard mcp-remove copilot
lidguard hook-status --provider claude
lidguard hook-install --provider claude
lidguard hook-remove --provider claude
lidguard hook-events --provider claude --count 50
lidguard mcp-status claude
lidguard mcp-install claude
lidguard mcp-remove claude
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 50
lidguard mcp-status codex
lidguard mcp-install codex
lidguard mcp-remove codex
lidguard mcp-status all
lidguard mcp-install all
lidguard mcp-remove all
lidguard provider-mcp-status --config "C:\path\to\mcp.json"
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
lidguard provider-mcp-remove --config "C:\path\to\mcp.json"
lidguard provider-mcp-server --provider-name "ExampleProvider"
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature high
lidguard linux-permission status
lidguard linux-permission check
lidguard linux-permission install
lidguard linux-permission remove
lidguard suspend-history
lidguard suspend-history 20
lidguard preview-system-sound Asterisk
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
lidguard settings --server-runtime-cleanup-delay-minutes 0
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --post-session-end-webhook-url https://example.com/lidguard-session-ended
lidguard settings --closed-lid-permission-request-decision allow
lidguard settings --prevent-system-sleep true --prevent-display-sleep true --power-request-reason "LidGuard keeps agent sessions awake"
lidguard status
lidguard cleanup-orphans
```

## MCP Server Example

```powershell
lidguard mcp-server
```

## Failure Modes

- Hook start succeeds but stop is missed: parent process watcher should cleanup.
- Runtime crashes after changing lid action: future pending backup state should restore on the next CLI run.
- Power setting changes are denied by policy: keep normal power requests and surface the failure.
- Hibernate is unsupported or disabled: Emergency Hibernation falls back to Sleep after recording the hibernate failure; other Hibernate requests should fail clearly unless a future safe fallback is intentionally added.
- Multiple providers run at once: ref-count active sessions and restore only after the last session stops.
- Active power scheme changes during a session: v1 restores the originally backed-up scheme.
