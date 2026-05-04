---
name: lidguard-cli-runtime
description: "LidGuard CLI, settings, permission commands, examples, and failure modes reference. Use when working on command routing/help, settings defaults and validation, linux-permission or macos-permission commands, CLI examples, suspend history, or user-facing operational failures."
---

# LidGuard CLI Runtime

## Command Routing

- Parse `help`, `start`, `stop`, `remove-pre-suspend-webhook`, `remove-post-session-end-webhook`, `remove-session`, `status`, `live-status`, `settings`, `cleanup-orphans`, `current-lid-state`, `current-monitor-count`, `current-temperature`, `suspend-history`, `claude-hook`, `claude-hooks`, `copilot-hook`, `copilot-hooks`, `codex-hook`, `codex-hooks`, `hook-status`, `hook-install`, `hook-remove`, `hook-events`, `mcp-status`, `mcp-install`, `mcp-remove`, `provider-mcp-status`, `provider-mcp-install`, `provider-mcp-remove`, `preview-system-sound`, `preview-current-sound`, `mcp-server`, and `provider-mcp-server`.
- In Linux builds, additionally parse `linux-permission status`, `linux-permission check`, `linux-permission install`, and `linux-permission remove`.
- Do not include Linux permission commands in Windows routing or help output.
- In macOS builds, additionally parse `macos-permission status`, `macos-permission check`, `macos-permission install`, and `macos-permission remove`.
- Do not include macOS permission commands in Windows or Linux routing or help output.
- Make `help` print a categorized command overview with short descriptions.
- Make `help <command>` print focused detailed help for one command or recognized command alias.
- Make `<command> --help` use the same help metadata and return before target command validation or command-specific work.
- Load persisted default settings and send them with the start IPC request for `start`, the `UserPromptSubmit` path in `codex-hook` and `claude-hook`, and the `userPromptSubmitted` path in `copilot-hook`.
- Use the named-pipe runtime client for `start`, `stop`, `remove-session`, `status`, `settings`, and `cleanup-orphans` requests.

## Session Management Commands

- `remove-session --all` manually removes every active session currently tracked by the runtime.
- `remove-session --session <id>` removes a specific session identifier.
- `remove-session --provider <provider>` narrows manual removal to a provider.
- Provider MCP removal may also narrow by MCP provider name where supported.
- `status` should report active sessions, runtime lid state, visible display monitor count, settings summary, soft-lock state, and runtime cleanup state.
- `live-status` should keep refreshing status until interrupted.
- `cleanup-orphans` should remove sessions whose watched processes have exited or whose safe provider-specific cleanup criteria match.

## Linux Permission Command

- Compile `linux-permission` only into Linux builds.
- `linux-permission status` reports the current user, managed polkit rule state, `systemd-inhibit` availability, `systemctl` availability, and logind `CanSuspend` / `CanHibernate` capability values when queryable.
- `linux-permission check` non-destructively verifies inhibitor acquire/release, `systemctl --version`, and logind suspend/hibernate capability queries. It must not request an actual suspend or hibernate.
- `linux-permission install` writes `/etc/polkit-1/rules.d/49-lidguard.rules` using root privileges or one-time `sudo`, and grants only the current target user the logind actions LidGuard needs.
- The managed polkit rule allows only `org.freedesktop.login1.suspend`, `suspend-multiple-sessions`, `hibernate`, `hibernate-multiple-sessions`, `inhibit-block-sleep`, `inhibit-block-idle`, and `inhibit-handle-lid-switch`.
- `linux-permission remove` deletes the rule only when the file contains LidGuard's managed markers. It must refuse to delete unmanaged files.
- Do not add a `sudo -v` prepare command; sudo credential caches are tty/timeout scoped and are not a reliable basis for long-running agent protection.

## macOS Permission Command

- Compile `macos-permission` only into macOS builds.
- `macos-permission status` reports the current user, managed sudoers rule state when directly inspectable, privileged same-value `pmset` usability, `caffeinate`, `pmset`, `powermetrics`, `ioreg`, and `system_profiler` availability, plus current `SleepDisabled` and `hibernatemode` values when queryable.
- Do not use `sudo cat` or any command outside the managed sudoers allowlist to inspect sudoers content.
- `macos-permission check` non-destructively verifies `caffeinate` acquire/release, `pmset -g`, same-value privileged `pmset disablesleep`, same-value privileged `pmset hibernatemode`, and one privileged `powermetrics --samplers smc` sample. It must not request an actual sleep or hibernate.
- `macos-permission install` writes `/private/etc/sudoers.d/lidguard` using root privileges or one-time `sudo`, validates the generated rule with `visudo -cf`, and grants only the current target user the exact privileged commands LidGuard needs.
- The managed sudoers rule allows only `/usr/bin/pmset -a disablesleep 0`, `/usr/bin/pmset -a disablesleep 1`, `/usr/bin/pmset -a hibernatemode 0`, `/usr/bin/pmset -a hibernatemode 3`, `/usr/bin/pmset -a hibernatemode 25`, and `/usr/bin/powermetrics --samplers smc -n 1 -i 1000`.
- `macos-permission remove` deletes the sudoers file only when the file contains LidGuard's managed markers. It must refuse to delete unmanaged files.
- Do not add a `sudo -v` prepare command; sudo credential caches are tty/timeout scoped and are not a reliable basis for long-running agent protection.

## Settings Defaults

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
- Parent process watchdog: enabled.

## Provider Permission Hooks

- PermissionRequest hooks only emit a structured allow/deny decision when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so the provider's default permission flow continues.
- Claude and GitHub Copilot CLI closed-lid `PermissionRequest` outputs also set `interrupt: true`.
- Keep hook DTOs separate per provider even when another provider later uses a similar JSON shape.
- Claude `Elicitation` hooks emit a structured `cancel` only when the runtime reports `LidSwitchState = Closed` and `VisibleDisplayMonitorCount = 0`; otherwise they return empty stdout so Claude's default elicitation flow continues.

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
lidguard macos-permission status
lidguard macos-permission check
lidguard macos-permission install
lidguard macos-permission remove
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
lidguard live-status
lidguard cleanup-orphans
```

## Failure Modes

- Hook start succeeds but stop is missed: parent process watcher should clean up when safe.
- Runtime crashes after a temporary lid policy change: recovery paths should restore pending backup state before normal command execution when a backup exists.
- Power setting changes are denied by policy: keep normal power requests and surface the failure.
- Hibernate is unsupported or disabled: Emergency Hibernation falls back to Sleep after recording the hibernate failure; other Hibernate requests should fail clearly unless a safe fallback is intentionally added.
- Multiple providers run at once: ref-count active sessions and restore only after the last session stops.
- Active power scheme changes during a session: restore the originally backed-up scheme.
