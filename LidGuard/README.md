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
lidguard cleanup-orphans
```

`start` and `stop` require `--provider`. `--session` is optional; when omitted, LidGuard derives a fallback session identifier from the provider display name and normalized working directory.

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
lidguard settings --server-runtime-cleanup-delay-minutes off
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard settings --post-session-end-webhook-url https://example.com/lidguard-session-ended
lidguard remove-pre-suspend-webhook
lidguard remove-post-session-end-webhook
lidguard preview-system-sound --name Asterisk
lidguard preview-current-sound
```

Running `settings` with no options starts interactive editing. Session timeout defaults to 12 minutes; pass `--session-timeout-minutes off` to disable inactive session timeout soft-locking or pass a value of at least 1 to transition sessions to soft-locked after that many minutes since last activity. Server runtime cleanup delay defaults to 10 minutes after all sessions are gone and pending cleanup is finished; pass `--server-runtime-cleanup-delay-minutes off` to exit immediately. Emergency Hibernation temperature mode defaults to `Average`, and you can change it to `Low`, `Average`, or `High`. The optional post-stop suspend sound volume override accepts `off` or 1 through 100 percent; when enabled, it temporarily sets the default output device master volume while the sound plays, then restores the previous volume and mute state. `preview-system-sound` and `preview-current-sound` use the saved override setting and wait until playback finishes. `preview-current-sound` prints setup guidance when no post-stop suspend sound is configured. Use `remove-pre-suspend-webhook` or `remove-post-session-end-webhook` to clear configured webhook URLs.

## Diagnostics

```powershell
lidguard current-lid-state
lidguard current-monitor-count
lidguard current-temperature
lidguard current-temperature --temperature-mode high
lidguard linux-permission status
lidguard linux-permission check
lidguard macos-permission status
lidguard macos-permission check
```

`current-lid-state` prints the current lid switch state as `Open`, `Closed`, or `Unknown` using the same platform lid-state source LidGuard uses for closed-lid policy decisions.

`current-monitor-count` prints the current visible display monitor count using the same base platform monitor visibility check LidGuard uses for closed-lid suspend policy decisions. Internal laptop panel connections are only excluded by the final suspend eligibility check.

`current-temperature` prints the current recognized system thermal-zone temperature in Celsius using the selected aggregation mode. Use `--temperature-mode default|low|average|high` to reuse the saved setting or override it for one command. When the settings file does not exist yet, `default` falls back to LidGuard's `Average` headless runtime default.

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
```

If `--provider` is omitted on `hook-status`, `hook-install`, `hook-remove`, or `hook-events`, LidGuard prompts for a provider. With `--provider all`, LidGuard only processes providers whose default configuration roots already exist and reports missing providers as skipped.

## MCP Integration

```powershell
lidguard mcp-status --provider codex
lidguard mcp-install --provider codex
lidguard mcp-remove --provider codex
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-remove --config "<json-path>"
```

If `--provider` is omitted on `mcp-status`, `mcp-install`, or `mcp-remove`, LidGuard prompts for a provider. Re-running `mcp-install` refreshes an existing managed LidGuard MCP server by removing it first and then installing the current command. With `--provider all`, LidGuard only processes providers whose default configuration roots already exist and reports missing providers as skipped.

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

On macOS, idle sleep protection uses `caffeinate`. Lid-close protection with `--change-lid-action true` temporarily sets `pmset -a disablesleep 1`, stores the original `SleepDisabled` state as a pending backup, and restores it when protection ends or during the next CLI recovery path. Hibernate temporarily sets supported `hibernatemode` values to `25` before `pmset sleepnow`, then restores the original mode. Temperature readings are best-effort `powermetrics --samplers smc` samples; unavailable sensors or permissions simply make Emergency Hibernation skip that poll.

Provider MCP integrations are best-effort only. They depend on the model actually calling the LidGuard MCP tools at the right times, so LidGuard cannot guarantee that a provider will start, soft-lock, clear, and stop sessions correctly.
