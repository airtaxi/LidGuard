# Keep the agent awake, even when the lid closes.

[![NuGet](https://img.shields.io/nuget/v/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![NuGet downloads](https://img.shields.io/nuget/dt/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![Pack and Publish](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml/badge.svg)](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 English | [한국어](README.ko.md)

LidGuard lets long-running local AI coding agents keep working after you close your laptop lid. It tracks Codex, Claude Code, and GitHub Copilot CLI sessions, temporarily prevents idle sleep and lid-close sleep while protected work is active, then restores the original operating system power policy when that work finishes or becomes suspend-eligible.

Most keep-awake tools protect a process, a timer, or the whole machine. LidGuard protects an AI coding agent session, so the workflow is simpler: start the agent, close the lid when the device is safe to leave running, and let LidGuard release protection or enter Sleep/Hibernate when the work is done.

## Highlights

- Agent-aware sleep prevention for Codex, Claude Code, and GitHub Copilot CLI.
- Optional lid-close protection so protected agent work can continue after the laptop closes, with automatic restoration of the original OS policy.
- Automatic Sleep or Hibernate after protected sessions finish, helping avoid unnecessary battery drain.
- Cross-platform power control for Windows, systemd/logind Linux, and macOS.
- Safety controls such as SoftLock, inactive timeout, pre-suspend hooks, diagnostics, and emergency hibernation.

## Why LidGuard?

Generic keep-awake tools are good at preventing idle sleep, but they are not built around the moment you want to close a laptop and leave an agent running. LidGuard is designed for that narrower workflow: start protection when a local AI coding agent begins work, keep it only while the agent still needs the machine awake, and release it when the agent finishes or becomes suspend-eligible.

If LidGuard temporarily changes lid-close behavior, it restores the user's original operating system policy afterward. If configured, it can request Sleep or Hibernate after completion. The goal is to preserve long-running local agent work without leaving the laptop awake longer than necessary. LidGuard targets Windows, systemd/logind Linux, and macOS rather than acting as a single-platform workaround.

## Install

### Let Your Agent Install It

LidGuard is built for local AI agents, so letting an agent install the agent-awake tool is a fairly reasonable division of labor. Hand it the prompt below, grant command execution when it asks, and let it check .NET, install the NuGet tool, wire provider hooks/MCP, and walk you through the safety settings.

For most agents:

```text
Read https://raw.githubusercontent.com/airtaxi/LidGuard/master/.github/agent-install.md and install LidGuard for this machine. You may run the commands needed for installation after explaining them, and you should ask me before any administrator, sudo, password, provider hook, MCP, or safety-related settings step.
```

For Codex, start with `/plan` so it can use its interactive question flow:

```text
/plan
Read https://raw.githubusercontent.com/airtaxi/LidGuard/master/.github/agent-install.md and install LidGuard for this machine. You may run the commands needed for installation after explaining them, and you should ask me before any administrator, sudo, password, provider hook, MCP, or safety-related settings step.
```

The agent-facing instructions live at [.github/agent-install.md](.github/agent-install.md).

After hooks or MCP servers are installed, the current conversation might not pick them up immediately. Start a new provider session or conversation when you want to verify the integration.

### Manual Install

```powershell
dotnet tool install --global lidguard
```

```powershell
lidguard help
lidguard hook-install --provider all
lidguard mcp-install all
```

### Provider MCP For Other Agents

If an AI tool does not have a native LidGuard hook integration but can register a custom stdio MCP server, use Provider MCP as a best-effort integration path:

```powershell
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
```

After registering the server, give the model [ProviderMcpModelPrompt.md](ProviderMcpModelPrompt.md) as its provider/session instruction so it knows when to call `provider_start_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`, and `provider_stop_session`.

Provider MCP behavior is not guaranteed. Correct operation depends entirely on the model choosing to call the LidGuard MCP tools at the right times, and it does not expand LidGuard's operating system support beyond Windows, systemd/logind Linux, and macOS.

## Current Support Status

LidGuard is currently officially tested with Codex on Windows. Linux, macOS, Claude Code, and GitHub Copilot CLI support are implemented, but broader real-world validation is still ongoing. Reports and pull requests are welcome.

## Full Feature Overview

- Session tracking and status: active session count, provider/session identity, watched process id, SoftLock state, working directory, local start and last-activity times, current lid state, visible display monitor count, and a live terminal status dashboard.
- Provider integration: Codex, Claude Code, and GitHub Copilot CLI hooks; hook install/status/remove/events commands; regular MCP install/status/remove commands; and Provider MCP for other tools that can call a custom stdio MCP server.
- Keep-awake controls: configurable system sleep prevention, display sleep prevention, Windows away-mode requests, and the Windows power request reason text shown by the operating system.
- Lid-close handling: temporary Windows lid-action override, Linux `handle-lid-switch` inhibitor, macOS `pmset disablesleep`, and restoration of the user's original policy after protection ends or recovery runs.
- Process and runtime cleanup: parent-process watching, orphan cleanup, inactive-session timeout SoftLocking, and automatic runtime exit after the configured cleanup delay once all sessions are gone.
- Completion suspend flow: configurable Sleep or Hibernate, post-stop suspend delay in seconds, cancellation when sessions resume or the environment is no longer suspend-eligible, and recent suspend history retention.
- Pre-suspend audio warning: optional off/system-sound/`.wav` warning sound before Sleep or Hibernate, plus an optional temporary master volume override from 1 through 100 percent that restores the previous volume and mute state afterward.
- SoftLock and closed-lid permissions: provider waiting/input events and inactive timeouts can release keep-awake protection without removing the session. Closed-lid permission automation is safety-sensitive: choosing `allow` can approve permission-required provider work while the user may not be watching, so `deny` is the safer default.
- Emergency Hibernation: optional closed-lid high-temperature monitor with low, average, or high sensor aggregation, a configurable Celsius threshold, immediate Hibernate, and Sleep fallback if Hibernate fails.
- Webhooks and notifications: `PreSuspend` webhook before Sleep/Hibernate, `PostSessionEnd` webhook after normal completion when suspend is not pending, and optional `LidGuard.Notifications` Web Push companion server.
- Diagnostics and logs: current lid state, visible monitor count, current temperature, live-status dashboard, suspend-history diagnostics, hook event logs, session execution logs, exception logs, and configurable suspend history count.
- Platform setup and localization: Linux polkit helper commands, macOS sudoers helper commands, localized CLI output with `auto`, `en`, `ko`, or another culture name, and default settings/log storage under the platform local application data directory.
- Packaging: .NET 10 NativeAOT global tool distribution for Windows, systemd/logind Linux, and macOS.

## Documentation

- [CLI details](LidGuard/README.md)
- [Webhook notification server](LidGuard.Notifications/README.md)

## Safety And Responsibility

LidGuard is a power-management helper, not a guarantee that a laptop is safe to leave unattended in every environment. Do not put a running laptop in a bag, sleeve, drawer, or other heat-trapping space unless you have personally confirmed that the device is cool, stable, and safe.

Closed-lid permission automation is also a supervision risk. If you configure LidGuard to allow provider permission requests while the lid is closed, permission-required agent actions may proceed while you are not watching the machine.

SoftLock detection, provider hooks, process watchers, operating system behavior, CLI behavior, temperature sensors, permissions, firmware, and power policies can all fail or change in ways that prevent safety features from running as expected. Emergency hibernation and suspend flows are best-effort safeguards, not a substitute for checking the machine yourself.

Codex hook cleanup has a specific limit: Codex App can still leave `process=none` sessions in the same working directory. LidGuard only uses the Codex working-directory watchdog fallback for resolved Codex CLI processes that are not running with the `app-server` argument, and that cleanup path never removes `process=none` Codex sessions.

You are responsible for monitoring device state and heat risk. Device damage, data loss, property damage, or other loss caused by ignoring those risks is your responsibility.

## Contributing

Pull requests are welcome. Please keep changes focused, test behavior that touches power control carefully, and avoid committing secrets or local machine configuration.

## License

LidGuard is licensed under the [MIT License](LICENSE.txt).

## Author

Created by [Howon Lee (airtaxi)](https://github.com/airtaxi).

Built with help from OpenAI Codex.
