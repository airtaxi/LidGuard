---
name: lidguard-implementation-map
description: "LidGuard repository structure and design constraints. Use when working on repository shape, subsystem ownership boundaries, Commons/App/Notifications layout, platform-specific files, NativeAOT constraints, or architectural constraints."
---

# LidGuard Implementation Map

## Scope

Use this skill for repository shape, subsystem ownership, and architectural constraints. Keep release-phase tracking, completed-component inventories, and missing-work lists out of this file.

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
  - Package RID support is prepared for `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, but the release workflow excludes `linux-arm64` because of the NativeAOT cross-linking known issue documented in the .NET Tool Package Guidelines.
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

## Subsystem Ownership

- `Sessions` owns provider identity, session keys, session registry state, session snapshots, and soft-lock model types.
- `Settings` owns runtime settings, defaults, normalization, and validation helpers.
- `Results` owns operation result envelopes shared across runtime and CLI paths.
- `Platform` owns the platform service-set abstraction used by runtime wiring.
- `Services` owns platform-neutral service interfaces for power requests, lid actions, suspend requests, process watching, lid state, visible display monitor counts, sound playback, and audio volume control.
- `Power` owns platform-neutral power request options, lid action values, lid action backup values, lid switch state, and suspend mode types.
- `Hooks` owns provider-specific hook models, hook installers, hook inspection logic, managed hook config generation, and hook diagnostics.
- `Ipc` owns pipe command names, request/response contracts, runtime client behavior, and status payloads.
- `Control` owns runtime-facing control operations, settings patching, session removal outcomes, and control snapshots.
- `Runtime` owns live session orchestration, transcript monitoring, Emergency Hibernation polling, post-stop suspend sound coordination, and suspend history storage.
- `LidGuard.Notifications` owns webhook API endpoints, token login, subscription dashboard, webhook event history, SQLite persistence, Web Push sending, and background notification dispatch.

## Design Constraints

- Keep cross-platform-capable logic in regular `LidGuard` feature folders and namespaces.
- Keep Windows API calls and Windows-only assumptions in `LidGuard` `*.windows.cs` files.
- Keep Linux systemd/logind, `/proc`, and `/sys` assumptions in `*.linux.cs` files.
- Keep macOS command/framework assumptions in `*.macOS.cs` files.
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
- Keep the notification server optional and external to the core LidGuard runtime.
- Keep VAPID private keys on the notification server only.
- The notification server's human-facing web UI, browser subscription status text, and Web Push notification title/body text must support the same localization language set as the core LidGuard UI.
- `UserInterfaceCulture` uses `auto` by default, accepts `en`, `ko`, or any `CultureInfo`-resolvable culture name, and `LIDGUARD_UI_CULTURE` overrides the configured value for testing and support.
- If the CLI webhook option names `--pre-suspend-webhook-url` or `--post-session-end-webhook-url` change, update the LidGuard Notifications dashboard command examples in the same change.
- Localize final human-facing CLI presentation, including runtime/session status messages, session list summaries, management output, enum display text, and placeholders.
- Do not leak raw protocol `Message` text directly into user-facing terminal output when a localized rendering can be produced.
- Before version 1.0.0, do not add migration-only legacy code for behavior or configuration that has not been publicly released.
