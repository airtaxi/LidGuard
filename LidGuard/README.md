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
lidguard hook-install --provider all
lidguard hook-status --provider all
lidguard hook-remove --provider all
lidguard hook-events --provider all
lidguard hook-install --provider claude
lidguard hook-status --provider claude
lidguard hook-remove --provider claude
lidguard hook-install --provider copilot
lidguard hook-status --provider copilot
lidguard hook-remove --provider copilot
lidguard hook-install --provider codex
lidguard hook-status --provider codex
lidguard hook-remove --provider codex
lidguard mcp-server
lidguard settings
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
