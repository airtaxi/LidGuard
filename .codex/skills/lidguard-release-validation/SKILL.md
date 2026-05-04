---
name: lidguard-release-validation
description: "LidGuard validation and release workflow reference. Use when working on build validation guidance, current validation scope, missing work, automated verification, .NET tool packaging, NuGet publishing, package commands, or local install smoke tests."
---

# LidGuard Release Validation

## Build Validation Note

- Local build, test, publish, pack, and reinstall validation commands can fail once because of transient Windows Defender file-lock interference.
- Retry the same validation command before taking any broader recovery action; when this specific issue is the cause, a retry is typically enough.
- Do not bring down any build server just because the first validation attempt failed with this known Defender issue.

## Current Validation Scope

- Current manual runtime validation has only covered Windows with Codex.
- Windows, Linux, and macOS RID compile validation can catch platform compilation regressions, but it is not a substitute for runtime behavior validation on each target OS.
- Linux systemd/logind runtime behavior, macOS runtime behavior, Claude Code hooks, GitHub Copilot CLI hooks, Provider MCP flows, and cross-provider concurrent-session behavior still need real environment validation before being treated as verified.

## Missing Work

The Windows, Linux, and macOS CLI hook receiving path is implemented for Codex, Claude Code, and GitHub Copilot CLI. Remaining work is now focused on lifecycle polish and automated regression coverage.

- Implement immediate runtime shutdown after the last session stops once the remaining post-stop cleanup work is complete.
- Validate Linux behavior on a real systemd/logind laptop: `systemd-inhibit` lifecycle, `handle-lid-switch` inhibition, `systemctl suspend` / `systemctl hibernate`, closed-lid plus monitor-count suspend eligibility, `/proc/acpi/button/lid` lid-state reads, `/sys/class/drm` monitor detection, `/sys/class/thermal` temperature aggregation, Emergency Hibernation, and suspend history logging.
- Validate `linux-permission status|check|install|remove` on non-root and root paths, including busctl `CanSuspend` / `CanHibernate`, managed marker refusal for unmanaged polkit files, sudo failure paths, and removal safety.
- Validate Linux post-stop sound behavior with no player installed, `pw-play`, `paplay`, `aplay`, missing desktop sound-theme assets, `.wav` paths, and `pactl` volume override capture/apply/restore failure paths.
- Validate macOS behavior on a real MacBook: `caffeinate` lifecycle, `pmset disablesleep` backup/restore, `pmset sleepnow`, temporary `hibernatemode 25` hibernate with deferred recovery restore, closed-lid plus monitor-count suspend eligibility, `ioreg` lid-state reads, `system_profiler` monitor detection, best-effort Apple Silicon `IOHIDEventSystemClient` and `powermetrics` temperature aggregation, Emergency Hibernation, and suspend history logging.
- Validate `macos-permission status|check|install|remove` on non-root and root paths, including managed marker refusal for unmanaged sudoers files, `visudo` validation, non-interactive sudo failure paths, and removal safety.
- Validate macOS post-stop sound behavior with missing `afplay`, SystemSounds mapping, `.wav` paths, and `osascript` volume override capture/apply/restore failure paths.
- Add automated regression tests or verification scripts for the already manually verified provider behavior, Windows behavior, Linux systemd/logind behavior, and macOS behavior: latest Codex hook behavior, Claude Code hook stdout behavior, GitHub Copilot CLI hook output behavior, GitHub Copilot CLI user-level `~/.copilot/hooks/` loading and inline `~/.copilot/settings.json` hook composition, GitHub Copilot CLI session id stability, `PowerReadACValueIndex`/`PowerReadDCValueIndex` read/write behavior under normal user permissions, Linux inhibitor lifecycle, Linux polkit rule management, macOS parser/permission management, and Group Policy or MDM blocked power setting fallback messages.
- Add direct Codex soft-lock support only if Codex later exposes a notification or machine-readable pending-state hook surface.

## .NET Tool Package Guidelines

- Do not run `dotnet pack` unless the user explicitly asks for package creation. `dotnet pack` performs a build.
- For NuGet upload, use the `$publish-nuget` skill at `C:\Users\kck41\.codex\skills\publish-nuget\SKILL.md`. Do not upload with raw `dotnet nuget push` commands.
- The `$publish-nuget` skill only publishes existing `.nupkg` files. If the user asks to publish but packages do not exist yet, ask whether packing should be done first.
- Do not open, inspect, quote, or rewrite `C:\Data\Scripts\publish_nuget-nopause.bat` unless the user explicitly asks to work on that file.
- Confirm the package version in `LidGuard\LidGuard.csproj` before packing.
- Confirm license metadata before public NuGet.org upload. Add either `PackageLicenseExpression` or `PackageLicenseFile` before publishing publicly.
- If `DOTNET_CLI_HOME` is set for packaging, delete that temporary directory immediately after packaging finishes.
- After local packaging, test installation from the local package source before upload.
- Manual local NuGet upload must use the `$publish-nuget` skill. The protected GitHub Actions `Pack and Publish NuGet` workflow is the release automation path and may use the `nuget-production` environment secret `NUGET_API_KEY` directly.
- Known issue: `linux-arm64` NativeAOT packages are intentionally excluded from the release workflow for now. Cross-packing Linux ARM64 from the Linux x64 runner fails with `gcc : error : unrecognized command-line option '--target=aarch64-linux-gnu'`, matching dotnet/runtime#78559, which is closed as not planned. Do not re-add `linux-arm64` unless a native Linux ARM64 runner is available or the required cross-linker/toolchain path is proven in CI.

Package commands:

```powershell
dotnet pack .\LidGuard\LidGuard.csproj -c Release
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x64
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-x86
dotnet pack .\LidGuard\LidGuard.csproj -c Release -r win-arm64
```

Expected package files:

```text
artifacts\packages\lidguard.0.1.0.nupkg
artifacts\packages\lidguard.win-x64.0.1.0.nupkg
artifacts\packages\lidguard.win-x86.0.1.0.nupkg
artifacts\packages\lidguard.win-arm64.0.1.0.nupkg
artifacts\packages\lidguard.linux-x64.0.1.0.nupkg
artifacts\packages\lidguard.osx-x64.0.1.0.nupkg
artifacts\packages\lidguard.osx-arm64.0.1.0.nupkg
```

Publish commands:

```powershell
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-x86.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.win-arm64.0.1.0.nupkg"
& "C:\Data\Scripts\publish_nuget-nopause.bat" ".\artifacts\packages\lidguard.0.1.0.nupkg"
```

Local install smoke test:

```powershell
dotnet tool install --global lidguard --add-source .\artifacts\packages --version 0.1.0
lidguard --help
lidguard status
```
