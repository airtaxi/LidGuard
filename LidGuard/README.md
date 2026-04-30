# LidGuard CLI

LidGuard is a command-line tool for long-running local AI coding agent sessions. Windows protection is currently implemented; macOS and Linux support is planned and currently exits successfully with a support message.

## Install

```powershell
dotnet tool install --global lidguard
```

The tool package ID and command are both `lidguard`.

After installation, run:

```powershell
lidguard --help
```

## Usage

```powershell
lidguard <command> [options]
```

Use `--name value` or `--name=value` for options. Boolean options accept `true/false`, `yes/no`, `on/off`, and `1/0`.

For the full command and parameter reference, run:

```powershell
lidguard --help
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
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard remove-pre-suspend-webhook
lidguard preview-system-sound --name Asterisk
```

Running `settings` with no options starts interactive editing. Use `remove-pre-suspend-webhook` to clear a configured webhook URL.

## Diagnostics

```powershell
lidguard current-temperature
```

`current-temperature` prints the highest system thermal-zone temperature currently recognized by Windows in Celsius, or reports when the value is unavailable.

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

If `--provider` is omitted on `mcp-status`, `mcp-install`, or `mcp-remove`, LidGuard prompts for a provider. With `--provider all`, LidGuard only processes providers whose default configuration roots already exist and reports missing providers as skipped.

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

The default settings file is `settings.json`. Runtime session execution events are written to `session-execution.log` as JSON lines, with only the latest 500 entries retained.

## Notes

This package targets `net10.0` and is packaged as RID-specific NativeAOT .NET tool packages for Windows, Linux, and macOS. Windows is the only implemented runtime platform in the current release.

Provider MCP integrations are best-effort only. They depend on the model actually calling the LidGuard MCP tools at the right times, so LidGuard cannot guarantee that a provider will start, soft-lock, clear, and stop sessions correctly.
