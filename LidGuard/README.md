# LidGuard CLI

🌐 [한국어](README.ko.md)

LidGuard is a command-line tool for long-running local AI coding agent sessions. Windows protection, systemd/logind Linux protection, and macOS protection are implemented.

## Install

```powershell
dotnet tool install --global lidguard
```

The tool package ID and command are both `lidguard`.

After installation, run:

```powershell
lidguard help
```

## Usage

```powershell
lidguard <command> [options]
```

Use `--name value` or `--name=value` for options. Boolean options accept `true/false`, `yes/no`, `on/off`, and `1/0`.

For the categorized command overview, run:

```powershell
lidguard help
lidguard --help
```

For full options and notes for one command, run:

```powershell
lidguard help status
lidguard status --help
```

## Session Control

```powershell
lidguard start --provider codex --session "<session-id>"
lidguard stop --provider codex --session "<session-id>"
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard status
lidguard live-status
lidguard cleanup-orphans
```

`start` and `stop` require `--provider`. `--session` is optional; when omitted, LidGuard derives a fallback session identifier from the provider display name and normalized working directory.

`live-status` opens a runtime subscription and redraws the fixed terminal dashboard every second, with runtime events allowed to refresh it sooner. It shows the same runtime snapshot values as `status`, including current lid state, recent hook processing lines, runtime flow events, and suspend history results without starting the runtime when it is not already running. If the runtime is unavailable or disconnects, the interactive dashboard stays open and periodically reconnects. Press `q`, `Escape`, or `Ctrl+C` to exit.

## Settings & Suspend

```powershell
lidguard settings
lidguard settings --change-lid-action true --suspend-mode hibernate
lidguard settings --emergency-hibernation-temperature-mode average
lidguard settings --post-stop-suspend-sound Asterisk
lidguard settings --post-stop-suspend-sound-volume-override-percent 75
lidguard settings --post-stop-suspend-sound-volume-override-percent off
lidguard settings --session-timeout-minutes 12
lidguard settings --session-timeout-minutes off
lidguard settings --server-runtime-cleanup-delay-minutes 10
lidguard settings --server-runtime-cleanup-delay-minutes 0
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --post-session-end-webhook-url https://example.com/lidguard-session-ended
lidguard settings --closed-lid-stop-follow-up-webhook-url https://example.com/lidguard-follow-up
lidguard settings --closed-lid-permission-request-decision ask
lidguard remove-pre-suspend-webhook
lidguard remove-post-session-end-webhook
lidguard remove-closed-lid-stop-follow-up-webhook
lidguard preview-system-sound Asterisk
lidguard preview-current-sound
```

Running `settings` with no options starts interactive editing. Session timeout defaults to 12 minutes; pass `--session-timeout-minutes off` to disable inactive session timeout soft-locking or pass a value of at least 1 to transition sessions to soft-locked after that many minutes since last activity. Server runtime cleanup delay defaults to 10 minutes after all sessions are gone and pending cleanup is finished; pass `--server-runtime-cleanup-delay-minutes 0` to exit immediately or `--server-runtime-cleanup-delay-minutes off` to keep the runtime alive. Closed-lid PermissionRequest decision accepts `deny`, `allow`, or `ask`; `ask` soft-locks the session and returns empty hook stdout so the provider's normal permission prompt continues. Closed-lid Stop follow-up replies reuse `post-stop-suspend-delay-seconds` as the reply wait window; the feature stays off unless `--closed-lid-stop-follow-up-webhook-url` is a valid HTTP/HTTPS URL and the delay is greater than 0. Changing that URL or the delay automatically refreshes installed managed hook timeouts for the current OS providers and best-effort WSL managed hook configs on Windows. Emergency Hibernation temperature mode defaults to `Average`, and you can change it to `Low`, `Average`, or `High`. The optional post-stop suspend sound volume override accepts `off` or 1 through 100 percent; when enabled, it temporarily sets the default output device master volume while the sound plays, then restores the previous volume and mute state. `preview-system-sound` and `preview-current-sound` use the saved override setting and wait until playback finishes. `preview-current-sound` prints setup guidance when no post-stop suspend sound is configured. Use `remove-pre-suspend-webhook`, `remove-post-session-end-webhook`, or `remove-closed-lid-stop-follow-up-webhook` to clear configured webhook URLs.

## Diagnostics

```powershell
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature high
lidguard suspend-history
lidguard linux-permission status
lidguard linux-permission check
lidguard macos-permission status
lidguard macos-permission check
```

`current-lid-state` prints the current lid switch state as `Open`, `Closed`, or `Unknown` using the same platform lid-state source LidGuard uses for closed-lid policy decisions.

`current-monitor-count` prints the current visible display monitor count using the same base platform monitor visibility check LidGuard uses for closed-lid suspend policy decisions. Internal laptop panel connections are only excluded by the final suspend eligibility check.

`current-temperature` prints the current recognized system thermal-zone temperature in Celsius using the selected aggregation mode. Pass `default`, `low`, `average`, or `high` as the optional positional value to reuse the saved setting or override it for one command. When the settings file does not exist yet, `default` falls back to LidGuard's `Average` headless runtime default.

On Linux, `linux-permission status` and `linux-permission check` inspect the systemd/logind permission environment without suspending the system. Use `linux-permission install` to install a LidGuard-managed polkit rule for the current user, and `linux-permission remove` to remove only that managed rule file.

On macOS, `macos-permission status` and `macos-permission check` inspect the `caffeinate`, `pmset`, and `powermetrics` environment without requesting sleep. Use `macos-permission install` to install a LidGuard-managed sudoers rule for the current user, and `macos-permission remove` to remove only that managed rule file.

## Hook Integration

```powershell
lidguard hook-status --provider codex
lidguard hook-install --provider codex
lidguard hook-remove --provider codex
lidguard hook-events --provider codex --count 20
lidguard codex-hooks
lidguard claude-hooks
lidguard copilot-hooks
lidguard wsl-hook-status --provider codex
lidguard wsl-hook-install --provider all --distro Ubuntu
lidguard wsl-hook-remove --provider claude
lidguard wsl-codex-hooks config-toml --distro Ubuntu
lidguard wsl-claude-hooks settings-json
lidguard wsl-copilot-hooks config-json
```

If `--provider` is omitted on `hook-status`, `hook-install`, `hook-remove`, or `hook-events`, LidGuard prompts for a provider. With `--provider all`, LidGuard only processes providers whose default configuration roots already exist and reports missing providers as skipped.

The `wsl-*` hook commands are available only in Windows builds. They inspect or edit provider configuration inside WSL and write hook commands that call the current Windows `lidguard.exe` through its WSL path. Pass `--distro <name>` to select a distro; when omitted, `wsl.exe` uses the default distro. WSL hook status treats older managed `lidguard.exe` paths as needing update so reinstalling refreshes versioned tool paths.

## MCP Integration

```powershell
lidguard mcp-status codex
lidguard mcp-install codex
lidguard mcp-remove codex
lidguard wsl-mcp-status codex
lidguard wsl-mcp-install all --distro Ubuntu
lidguard wsl-mcp-remove claude
lidguard wsl-codex-mcp-install
lidguard wsl-claude-mcp-status --distro Ubuntu
lidguard wsl-copilot-mcp-remove
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-remove --config "<json-path>"
lidguard wsl-provider-mcp-status --config "~/.example/mcp.json"
lidguard wsl-provider-mcp-install --config "~/.example/mcp.json" --provider-name "<name>" --distro Ubuntu
lidguard wsl-provider-mcp-remove --config "~/.example/mcp.json"
```

If the provider positional value is omitted on `mcp-status`, `mcp-install`, or `mcp-remove`, LidGuard prompts for a provider. Re-running `mcp-install` refreshes an existing managed LidGuard MCP server by removing it first and then installing the current command. With `all`, LidGuard only processes providers whose default configuration roots already exist and reports missing providers as skipped.

The WSL MCP commands are Windows-only and run the provider CLI inside the selected/default WSL distro while registering the Windows `lidguard.exe` WSL path as the stdio server command. `wsl-codex-mcp-*`, `wsl-claude-mcp-*`, and `wsl-copilot-mcp-*` are provider-specific aliases for `wsl-mcp-*`. `wsl-provider-mcp-*` directly edits a WSL-side JSON config path.

## Managed / Internal Commands

```powershell
lidguard mcp-server
lidguard provider-mcp-server --provider-name "<name>"
lidguard codex-hook
lidguard claude-hook
lidguard copilot-hook --event notification
```

These commands are primarily intended for managed integrations and stdio hosts rather than direct everyday CLI use.

## Settings and Logs

LidGuard stores its default settings and runtime logs under:

```text
%LOCALAPPDATA%\LidGuard
```

On typical Linux desktops, this resolves under `~/.local/share/LidGuard`. On macOS, it resolves through .NET's local application data path for the current user.

The default settings file is `settings.json`. Runtime session execution events are written to `session-execution.log` as JSON lines, with only the latest 500 entries retained. Inactive-session timeout expiry is logged as `session-timeout-softlock-recorded`.

## Notes

This package targets `net10.0` and is packaged as RID-specific NativeAOT .NET tool packages for Windows, Linux, and macOS. Windows, systemd/logind Linux, and macOS are implemented runtime platforms in the current release.

On Linux, idle sleep protection uses systemd/logind `sleep` and `idle` inhibitors. Lid-close handling is separate: `--change-lid-action true` holds a `handle-lid-switch` inhibitor, while `false` leaves distribution lid-close handling unchanged. Partial systemd/logind environments report missing prerequisites per operation so diagnostics can still explain what is unavailable.

On macOS, idle sleep protection uses `caffeinate`. Lid-close protection with `--change-lid-action true` temporarily sets `pmset -a disablesleep 1`, stores the original `SleepDisabled` state as a pending backup, and restores it when protection ends or during the next CLI recovery path. Hibernate temporarily sets supported `hibernatemode` values to `25` before `pmset sleepnow`, then leaves the original mode in the pending backup for recovery on a later CLI run. Temperature readings first use best-effort Apple Silicon `IOHIDEventSystemClient` processor sensors, then fall back to `powermetrics --samplers smc` samples; unavailable sensors or permissions simply make Emergency Hibernation skip that poll. If an Emergency Hibernation request cannot hibernate, LidGuard immediately requests Sleep as a best-effort fallback and records both outcomes.

On Windows, WSL integration is only configuration management. WSL providers call the Windows LidGuard executable; LidGuard still protects the Windows host runtime and does not add independent Linux power management inside the distro. Before WSL commands run provider work, LidGuard checks that `wsl.exe` is usable and that the selected or default distro can execute a trivial command.

Provider MCP integrations are best-effort only. They depend on the model actually calling the LidGuard MCP tools at the right times, so LidGuard cannot guarantee that a provider will start, soft-lock, clear, and stop sessions correctly.
