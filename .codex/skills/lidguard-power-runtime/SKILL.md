---
name: lidguard-power-runtime
description: "LidGuard power, lid, suspend, and thermal behavior reference. Use when working on Windows/Linux/macOS keep-awake control, lid close policy, lid state, monitor-based suspend eligibility, post-stop suspend flow, or Emergency Hibernation."
---

# LidGuard Power Runtime

## Platform Power Control

### Windows

- Use `PowerCreateRequest`, `PowerSetRequest`, and `PowerClearRequest` for normal idle sleep prevention.
- Use `PowerRequestSystemRequired` to prevent idle system sleep.
- Use `PowerRequestAwayModeRequired` to request away-mode behavior where supported.
- Keep `PowerRequestDisplayRequired` optional; use it only when display sleep should also be prevented.
- Always clear power requests and close handles when protection ends.
- Do not change sleep idle timeouts. Runtime crashes could leave the user's system policy in a dangerous state.

### Linux

- Target systemd/logind environments.
- Use `systemd-inhibit` block inhibitors for normal sleep and idle prevention.
- Map `PreventSystemSleep` to the `sleep` inhibitor.
- Map `PreventSystemSleep` and `PreventDisplaySleep` to the `idle` inhibitor.
- Keep `PreventAwayModeSleep` Windows-only; do not expose or apply it in Linux settings/help output.
- Map temporary lid-close protection to a separate `handle-lid-switch` inhibitor only when `ChangeLidAction` is enabled.
- Keep `SystemdInhibitor` releasing normally by closing stdin on the helper process; use process-tree kill only as fallback.
- Tie inhibitor helper processes to the LidGuard runtime lifetime so stale orphaned inhibitors do not survive the runtime.
- Request immediate sleep/hibernate with `systemctl suspend` or `systemctl hibernate`, returning exit code/stderr when the request fails.
- Keep Linux as a supported top-level platform when `OperatingSystem.IsLinux()` is true, even when systemd/logind prerequisites are incomplete; report missing prerequisites from the specific runtime or diagnostic operation.
- Do not depend on a long-lived `sudo -v` cache. Prepare persistent privileged logind actions through the Linux polkit rule command.

### macOS

- Target local macOS systems with `caffeinate`, `pmset`, `ioreg`, `system_profiler`, Apple Silicon `IOHIDEventSystemClient` temperature sensors, and best-effort `powermetrics`.
- Allow the Apple Silicon temperature fast path to use direct source-generated `LibraryImport` calls into CoreFoundation and IOKit, plus `NativeLibrary` export lookup for CoreFoundation callback tables, because CsWin32 does not cover macOS frameworks.
- Use `caffeinate` assertions for normal idle sleep prevention.
- Map `PreventSystemSleep` to `caffeinate -i`.
- Map `PreventDisplaySleep` to `caffeinate -d`.
- Do not use `caffeinate -s` for LidGuard's cross-platform `PreventAwayModeSleep` setting. Away-mode sleep prevention is Windows-only, and macOS sleep prevention must rely on `caffeinate -i` so it works on AC and DC power.
- Map temporary lid-close protection to `pmset -a disablesleep 1` only when `ChangeLidAction` is enabled.
- Before changing `SleepDisabled`, save the original state through the pending lid-action backup JSON path; restore it when protection ends or during the next CLI recovery path before normal command execution.
- Request immediate Sleep with `pmset sleepnow`.
- For Hibernate, temporarily back up the current supported `hibernatemode`, write `hibernatemode 25`, request `pmset sleepnow`, and leave the pending hibernatemode backup for the next CLI recovery path instead of restoring it immediately after `pmset sleepnow` returns. If `pmset sleepnow` fails, roll back the hibernatemode immediately when possible.
- Use non-interactive `sudo -n` only for the exact commands allowed by `macos-permission install`; fail with actionable setup guidance instead of prompting in background runtime paths.

## Lid Close Policy

- Windows lid close behavior is power setting `LIDACTION`.
- Use subgroup GUID `4f971e89-eebd-4455-a8de-9e59040e7347`.
- Use setting GUID `5ca83367-6e45-459f-a27b-476b1d01c936`.
- Interpret values as `0 = Do Nothing`, `1 = Sleep`, `2 = Hibernate`, `3 = Shut Down`.
- Read AC/DC values from the active power scheme together before making changes.
- During active sessions, write AC/DC values to `0 = Do Nothing` when the setting is enabled.
- After the last active session stops, restore the backed-up AC/DC values.
- Restore the scheme that was active at backup time.
- Treat the pending lid-action backup JSON as the authoritative restore source. If it already exists, do not overwrite it with a new capture, and restore from that JSON instead of any in-memory value. If restore is expected but the JSON is missing, skip lid policy writes and append a failure entry to the runtime session log.
- On Linux, `ChangeLidAction=true` means LidGuard holds a systemd/logind `handle-lid-switch` inhibitor while protection is applied. Do not edit distribution power configuration files.
- On macOS, `ChangeLidAction=true` means LidGuard temporarily applies `pmset -a disablesleep 1`, records the original `SleepDisabled` value in pending backup state, and restores it after protection ends or during crash recovery.

## Lid State And Suspend

- Windows lid open/close notification uses `GUID_LIDSWITCH_STATE_CHANGE`.
- Interpret broadcast values as `0x0 = lid closed` and `0x1 = lid opened`.
- Convert lid broadcast values through `LidSwitchNotificationRegistration` to `LidSwitchState`.
- Start Windows closed-lid decisions from `GetSystemMetrics(SM_CMONITORS)` and exclude inactive monitor connections reported by WMI.
- For final Windows suspend eligibility while `LidSwitchState` is `Closed`, also exclude internal laptop panel connections and treat the machine as suspend-eligible only when visible display monitor count is `0`.
- Request immediate Windows sleep/hibernate with `SetSuspendState` after enabling `SeShutdownPrivilege`.
- Expect `SetSuspendState(false, ...)` to fail with `ERROR_NOT_SUPPORTED` on some Modern Standby systems.
- Read Linux lid state from `/proc/acpi/button/lid/*/state`; report `Unknown` when no readable state exists.
- Count Linux visible display monitors from `/sys/class/drm/*/status`; for final suspend eligibility while the lid is closed, exclude internal connector families such as `eDP`, `LVDS`, and `DSI`.
- Request Linux immediate sleep/hibernate with `systemctl suspend` or `systemctl hibernate`.
- Read macOS lid state from `ioreg` clamshell state; report `Unknown` when no readable state exists.
- Count macOS visible display monitors from `system_profiler SPDisplaysDataType -json`; for final suspend eligibility while the lid is closed, exclude built-in/internal display entries.
- Request macOS immediate sleep with `pmset sleepnow`; request hibernate with the temporary `hibernatemode 25` flow.
- After the last active session stops, request suspend after the configured post-stop delay using the configured suspend mode only when the lid is closed and suspend eligibility visible display monitor count is `0`.
- Treat a delay of `0` as immediate suspend.
- When `closedLidStopFollowUpWebhookUrl` is valid, `closedLidStopFollowUpDelaySeconds >= 20`, and `postStopSuspendDelaySeconds >= 10`, supported Stop hooks keep the Stop hook open through the post-stop safety delay first, then start the ask-before-sleep reply webhook. `postStopSuspendDelaySeconds` protects immediately-following prompts from being skipped too early; `closedLidStopFollowUpDelaySeconds` is the reply window after the notification is sent. A reply cancels the pending suspend and resumes the session, while no reply falls through to the existing suspend flow. If the user cancels the reply wait from the notification UI/API, reschedule the same pending suspend with a `0` second delay so `PreSuspend`, post-stop sound, final re-check, and the real suspend request start immediately.
- When a continued Stop arrives with `stop_hook_active = true` and `RepeatClosedLidStopFollowUp = false`, skip both the repeated ask-before-sleep reply wait and the normal post-stop suspend delay; proceed as an immediate suspend attempt after the usual closed-lid/display checks.
- Keep normal LidGuard protection active during the pending suspend delay, the ask-before-sleep reply wait, the `PreSuspend` webhook, and post-stop sound playback. Release protection only immediately before the actual suspend request. If pending suspend is canceled, release protection only when no active or resumed session still needs it.
- If an ask-before-sleep reply wait sound is configured, play it once after the StopFollowUp webhook start succeeds and the returned poll URL is validated, before the polling loop begins. Sound or volume override failures must be logged and must not interrupt reply waiting or the suspend flow.
- If an ask-before-sleep reply wait sound volume override is configured, use the same default output device master volume capture, temporary unmute/volume set, and restore cleanup rules used for post-stop suspend sounds.
- If a post-stop suspend sound is configured, wait for the delay first, send the pre-suspend webhook when configured, play the configured sound to completion, then re-check lid/session state before requesting suspend.
- If a post-stop suspend sound volume override is configured, capture the default output device master volume and mute state immediately before playback, temporarily unmute as needed, set the configured master volume percent for playback, then restore the previous volume and mute state in the sound playback cleanup path.
- If a pre-suspend webhook URL is configured, POST JSON with a 5-second timeout after the post-stop suspend delay and before post-stop suspend sound playback.
- Require pre-suspend webhook bodies to include `eventType = PreSuspend` and `reason`; include soft-locked session count for soft-lock-triggered suspend.
- When provider-reported normal session end schedules suspend, include the same provider/session identity, UTC timestamps, end reason metadata, active session count, working directory, transcript path, `inputPromptPreview`, and `lastAssistantMessage` fields in the `PreSuspend` body when available.
- If pending suspend is canceled before the pre-suspend webhook is attempted, allow the suppressed normal session-end notification to fall back to `PostSessionEnd` when configured.
- Require notification receivers to reject webhook payloads that omit `eventType`.

## Emergency Hibernation

- Use `SystemThermalInformation.GetSystemTemperatureCelsius(EmergencyHibernationTemperatureMode)` to read the selected available system thermal-zone temperature in Celsius.
- On Linux, read millidegree Celsius values from `/sys/class/thermal/thermal_zone*/temp` and apply the configured Low, Average, or High aggregation.
- On macOS, try Apple Silicon `IOHIDEventSystemClient` processor temperature sensors first, then fall back to `powermetrics --samplers smc` Celsius sensor output, applying Low, Average, or High aggregation.
- Treat unsupported sensors, permission failures, unsupported samplers, missing numeric Celsius values, and timeouts as unavailable; do not trigger Emergency Hibernation from unavailable data.
- Keep Emergency Hibernation temperature mode configurable as Low, Average, or High, defaulting to Average.
- Run the thermal monitor only while shared keep-awake protection is applied, the lid is closed, and suspend eligibility visible display monitor count is `0`.
- Poll every 10 seconds.
- Keep the Emergency Hibernation threshold configurable, defaulting to 93 Celsius, and always clamp it to 70 through 110 Celsius before runtime use.
- When observed temperature reaches the clamped threshold, cancel any pending post-stop suspend, send the pre-suspend webhook with `reason = EmergencyHibernation` using a 5-second timeout, then immediately request hibernate.
- If the hibernate request fails, immediately request Sleep as a best-effort fallback and record both suspend results.
- Ignore the regular suspend mode, post-stop suspend delay, post-stop suspend sound, and sound volume override settings.
- Do not let Emergency Hibernation webhook timeout or failure block the immediate hibernation request.
- Emergency Hibernation takes priority over any pending ask-before-sleep reply wait and must cancel that wait before hibernate/Sleep fallback handling continues.
