# Keep the agent awake, even when the lid closes.

[![NuGet](https://img.shields.io/nuget/v/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![NuGet downloads](https://img.shields.io/nuget/dt/lidguard.svg)](https://www.nuget.org/packages/lidguard)
[![Pack and Publish](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml/badge.svg)](https://github.com/airtaxi/LidGuard/actions/workflows/pack-and-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

🌐 English | [한국어](README.ko.md)

LidGuard keeps a local AI coding agent session protected while it is still working, then gives power policy back to the operating system when the session ends or becomes suspend-eligible. It handles the practical laptop workflow: start an agent, close the lid, and let LidGuard return the machine to Sleep or Hibernate when the work is done.

LidGuard is currently officially tested with Codex on Windows. Linux, macOS, Claude Code, and GitHub Copilot CLI support are implemented, but the official test scope is still limited.

## Features

- Covers laptop lid-close policy as well as ordinary idle sleep prevention.
- Tracks Codex, Claude Code, and GitHub Copilot CLI sessions through provider hooks.
- Supports SoftLock, inactive session timeout, and automatic Sleep or Hibernate after session end.
- Sends `PreSuspend` and `PostSessionEnd` webhooks, with an optional `LidGuard.Notifications` Web Push companion server.
- Implements Windows, systemd/logind Linux, and macOS power control, packaged as a NativeAOT .NET tool.
- Supports human CLI UI culture selection with `auto`, `en`, `ko`, or another culture name.

## Install

```powershell
dotnet tool install --global lidguard
```

```powershell
lidguard help
lidguard hook-install --provider codex
lidguard settings --ui-culture ko
```

## Documentation

- [CLI details](LidGuard/README.md)
- [Webhook notification server](LidGuard.Notifications/README.md)

## Safety And Responsibility

LidGuard is a power-management helper, not a guarantee that a laptop is safe to leave unattended in every environment. Do not put a running laptop in a bag, sleeve, drawer, or other heat-trapping space unless you have personally confirmed that the device is cool, stable, and safe.

SoftLock detection, provider hooks, process watchers, operating system behavior, CLI behavior, temperature sensors, permissions, firmware, and power policies can all fail or change in ways that prevent safety features from running as expected. Emergency hibernation and suspend flows are best-effort safeguards, not a substitute for checking the machine yourself.

Codex hook cleanup has a specific limit: Codex App can still leave `process=none` sessions in the same working directory. LidGuard only uses the Codex working-directory watchdog fallback for shell-hosted CLI sessions whose resolved process or direct parent is a platform-approved shell, and that cleanup path never removes `process=none` Codex sessions.

You are responsible for monitoring device state and heat risk. Device damage, data loss, property damage, or other loss caused by ignoring those risks is your responsibility.

## Contributing

Pull requests are welcome. Please keep changes focused, test behavior that touches power control carefully, and avoid committing secrets or local machine configuration.

## License

LidGuard is licensed under the [MIT License](LICENSE.txt).

## Author

Created by [Howon Lee (airtaxi)](https://github.com/airtaxi).

Built with help from OpenAI Codex.
