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

## Common Commands

```powershell
lidguard hook-install
lidguard hook-remove
lidguard mcp-status
lidguard mcp-install
lidguard mcp-remove
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-remove --config "<json-path>"
lidguard remove-pre-suspend-webhook
lidguard remove-session --all
lidguard remove-session --session "<session-id>"
lidguard remove-session --session "<session-id>" --provider codex
lidguard hook-install --provider all
lidguard hook-status --provider all
lidguard hook-remove --provider all
lidguard hook-events --provider all
lidguard mcp-status --provider all
lidguard mcp-install --provider all
lidguard mcp-remove --provider all
lidguard hook-install --provider claude
lidguard hook-status --provider claude
lidguard hook-remove --provider claude
lidguard mcp-status --provider claude
lidguard mcp-install --provider claude
lidguard mcp-remove --provider claude
lidguard hook-install --provider copilot
lidguard hook-status --provider copilot
lidguard hook-remove --provider copilot
lidguard mcp-status --provider copilot
lidguard mcp-install --provider copilot
lidguard mcp-remove --provider copilot
lidguard hook-install --provider codex
lidguard hook-status --provider codex
lidguard hook-remove --provider codex
lidguard mcp-status --provider codex
lidguard mcp-install --provider codex
lidguard mcp-remove --provider codex
lidguard provider-mcp-server --provider-name "<name>"
lidguard mcp-server
lidguard settings
lidguard settings --pre-suspend-webhook-url https://example.com/lidguard-webhook
lidguard status
```

With `--provider all`, LidGuard only processes providers whose default configuration roots already exist, and reports missing providers as skipped.

## Settings and Logs

LidGuard stores its default settings and runtime logs under:

```text
%LOCALAPPDATA%\LidGuard
```

The default settings file is `settings.json`. Runtime session execution events are written to `session-execution.log` as JSON lines, with only the latest 500 entries retained.

## Notes

This package targets `net10.0` and is packaged as RID-specific NativeAOT .NET tool packages for Windows, Linux, and macOS. Windows is the only implemented runtime platform in the current release.

Provider MCP integrations are best-effort only. They depend on the model actually calling the LidGuard MCP tools at the right times, so LidGuard cannot guarantee that a provider will start, soft-lock, clear, and stop sessions correctly.
