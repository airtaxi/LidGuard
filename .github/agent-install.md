# LidGuard Agent Installation Instructions

You are an AI coding agent helping the user install LidGuard on this machine.

LidGuard is distributed from NuGet.org as the global .NET tool package `lidguard`. Do not install from local `.nupkg` files, local build artifacts, or a repository clone unless the user explicitly asks for a local development install.

You need permission to run shell commands, install software, modify shell profile files, install provider hooks/MCP entries, and possibly run privileged Linux/macOS permission setup. Explain elevated commands before running them, and ask the user before any step that requires administrator, sudo, or password entry.

## 0. Ask The User For Their Language

Before starting installation or running setup commands, ask the user which language they want you to use for the installation conversation and final report.

Use the selected language for all user-facing prompts, questions, progress updates, and summaries. If the user does not answer clearly, ask again before continuing.

## 1. Detect The Platform

Detect the operating system and shell:

- Windows
- Linux with systemd/logind support where available
- macOS

On Windows, also detect whether WSL is installed and whether any distro is available. Use non-destructive checks such as:

```powershell
wsl.exe --status
wsl.exe --list --quiet
```

If WSL is unavailable, continue with normal Windows setup. If WSL is available, remember the available distro names and whether the user has a default distro. Do not install WSL hook or MCP configuration yet; ask the user later before changing WSL-side provider configuration.

Also detect whether the current shell can run commands interactively. If this agent cannot run commands, tell the user to use the manual installation instructions instead of pretending the install succeeded.

## 2. Ensure .NET 10 SDK Is Available

Run:

```bash
dotnet --list-sdks
```

If `dotnet` is missing or no SDK line starts with `10.`, install the .NET 10 SDK for the current platform using the official Microsoft .NET installation instructions for that OS or Linux distribution:

- Windows: install the .NET 10 SDK with a supported Microsoft installer or package manager path.
- Linux: install the .NET 10 SDK using the Microsoft-supported package feed or installer path for the detected distribution.
- macOS: install the .NET 10 SDK using the Microsoft-supported macOS installer or package manager path.

After installation, verify again with:

```bash
dotnet --list-sdks
dotnet --info
```

Continue only after .NET 10 is visible.

## 3. Reinstall LidGuard From NuGet.org

If LidGuard is already installed, reinstall it anyway so the user gets the current NuGet.org package and not a stale local tool.

Check:

```bash
dotnet tool list --global
```

If `lidguard` is listed, uninstall it:

```bash
dotnet tool uninstall --global lidguard
```

Install from NuGet.org:

```bash
dotnet tool install --global lidguard --add-source https://api.nuget.org/v3/index.json
```

On macOS, the .NET tool installer may print instructions for adding the global tool directory to `PATH`. Follow that output exactly. If `lidguard` is still not found after installation, add the .NET global tools directory for the current user to the active shell and the user's normal shell profile, then restart or refresh the shell:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

Verify:

```bash
lidguard --help
lidguard status
```

## 4. Prepare Platform Permissions

On Windows, continue to provider setup.

On Linux, inspect and install the managed permission setup:

```bash
lidguard linux-permission status
lidguard linux-permission check
lidguard linux-permission install
lidguard linux-permission check
```

On macOS, inspect and install the managed permission setup:

```bash
lidguard macos-permission status
lidguard macos-permission check
lidguard macos-permission install
lidguard macos-permission check
```

If `sudo` is required and the current agent session cannot collect the user's password, open a new terminal when your environment supports that, or ask the user to run the exact command in a terminal and enter their password there. Do not report the permission setup as complete until the follow-up `check` command succeeds or clearly reports what remains unavailable.

## 5. Install Provider Hooks And MCP Servers

Install hooks for all supported providers whose configuration roots exist:

```bash
lidguard hook-install --provider all
lidguard hook-status --provider all
```

Install MCP servers for all supported providers whose configuration roots exist:

```bash
lidguard mcp-install all
lidguard mcp-status all
```

Missing provider configuration roots can be reported as skipped. That is expected when the user has not installed or initialized a provider CLI.

On Windows, if WSL was detected in step 1, ask the user whether they also want LidGuard integration installed inside WSL. Explain that this edits provider hook/MCP configuration inside the WSL distro, but the configured commands call the Windows `lidguard.exe`; LidGuard does not need to be installed inside WSL.

Ask which distro to target:

- Use the default WSL distro.
- Use a specific detected distro name.
- Skip WSL integration.

If the user chooses WSL integration, run the matching commands. Omit `--distro` for the default distro, or include `--distro "<name>"` for a named distro:

```bash
lidguard wsl-hook-install --provider all
lidguard wsl-hook-status --provider all
lidguard wsl-mcp-install all
lidguard wsl-mcp-status all
```

If the user chose a named distro:

```bash
lidguard wsl-hook-install --provider all --distro "<distro-name>"
lidguard wsl-hook-status --provider all --distro "<distro-name>"
lidguard wsl-mcp-install all --distro "<distro-name>"
lidguard wsl-mcp-status all --distro "<distro-name>"
```

As with native provider setup, missing WSL-side provider configuration roots can be reported as skipped. That is expected when the provider has not been installed or initialized inside that distro.

If the current AI provider is not one of LidGuard's native hook providers, or if the user wants to use another provider that supports custom stdio MCP servers, offer Provider MCP as a best-effort fallback. Ask the user for:

- the provider display name to pass as `--provider-name`
- the provider's MCP JSON configuration file path

Then inspect and install the managed Provider MCP server entry:

```bash
lidguard provider-mcp-status --config "<json-path>"
lidguard provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard provider-mcp-status --config "<json-path>"
```

If the user wants Provider MCP for a provider running inside WSL, and Windows WSL integration was selected, use the WSL-specific commands with a WSL-side JSON path:

```bash
lidguard wsl-provider-mcp-status --config "<json-path>"
lidguard wsl-provider-mcp-install --config "<json-path>" --provider-name "<name>"
lidguard wsl-provider-mcp-status --config "<json-path>"
```

For a named distro, add `--distro "<distro-name>"` to each command.

After installing Provider MCP, tell the user to give [ProviderMcpModelPrompt.md](../ProviderMcpModelPrompt.md) to that provider's model as a provider/session instruction. If the agent needs to fetch it directly, use `https://raw.githubusercontent.com/airtaxi/LidGuard/master/ProviderMcpModelPrompt.md`. The model must follow that prompt so it knows when to call `provider_start_session`, `provider_set_soft_lock`, `provider_clear_soft_lock`, and `provider_stop_session`.

Be explicit that Provider MCP is not guaranteed. It depends entirely on the model calling LidGuard's MCP tools at the right times, and it does not add operating system support beyond Windows, systemd/logind Linux, and macOS.

## 6. Ask The User For Settings

Ask these questions in the user's selected language. Use an interactive user-input tool when your environment provides one. Prefer concise multiple-choice questions with a short explanation of the tradeoff. If no such tool exists, ask the questions directly and wait for the user's answers before changing settings.

If you are Codex and the user started this from a prompt that begins with `/plan`, use Plan-mode interactive questions before running the final settings commands.

Ask:

1. Whether LidGuard should prevent display sleep while sessions are active.
   - Parameter: `--prevent-display-sleep true|false`
   - Explain that this keeps the screen/display awake too; many users only need system sleep prevention.

2. Whether LidGuard should request Sleep or Hibernate after work completes.
   - Parameter: `--suspend-mode sleep|hibernate`
   - Explain that Sleep is faster, while Hibernate is slower but usually safer for battery and heat.

3. Whether LidGuard should play a warning sound before Sleep or Hibernate.
   - Parameter: `--post-stop-suspend-sound off|Asterisk|Beep|Exclamation|Hand|Question|<wav-path>`
   - Explain that this can warn nearby users right before the machine suspends.

4. Whether LidGuard should temporarily override the output volume while that warning sound plays.
   - Parameter: `--post-stop-suspend-sound-volume-override-percent off|<1-100>`
   - Explain that this is useful when the user wants the warning sound to be loud even if the current system volume is low. LidGuard restores the previous volume and mute state afterward.

5. How closed-lid permission requests should be handled.
   - Parameter: `--closed-lid-permission-request-decision deny|allow`
   - Explain this carefully: `allow` enables more complete automation while the lid is closed, including permission-required agent work, but it also allows actions to proceed while the user may not be watching the machine. `deny` automatically rejects those closed-lid permission requests, which is safer but can block unattended automation.

Also set the CLI UI culture to match the user's language when it is obvious:

```bash
lidguard settings --ui-culture ko
```

or:

```bash
lidguard settings --ui-culture en
```

Apply the user's answers with one or more `lidguard settings` commands. Example:

```bash
lidguard settings --prevent-display-sleep false --suspend-mode sleep --post-stop-suspend-sound off --post-stop-suspend-sound-volume-override-percent off --closed-lid-permission-request-decision deny
```

### Example Interactive Question Flow

If your environment provides an ask-user style tool, present questions like these. Use the user's language for the visible prompts and option labels. The examples below show English text plus the exact setting value each answer maps to.

Display sleep question:

```text
id: prevent_display_sleep
question: Should LidGuard keep the display awake while agents run?
options:
- No, system only (Recommended) -> --prevent-display-sleep false
  Keeps the computer awake for agent work while still allowing the display to sleep.
- Yes, keep display awake -> --prevent-display-sleep true
  Useful when the user wants visible progress, but it uses more power and screen time.
```

Suspend mode question:

```text
id: suspend_mode
question: What should LidGuard request after protected work completes?
options:
- Sleep (Recommended) -> --suspend-mode sleep
  Faster resume and usually enough for desk use.
- Hibernate -> --suspend-mode hibernate
  Slower resume, but safer for battery drain and heat during longer unattended periods.
```

Warning sound question:

```text
id: post_stop_suspend_sound
question: Should LidGuard play a warning sound before Sleep or Hibernate?
options:
- Off (Recommended) -> --post-stop-suspend-sound off
  Silent behavior; good when the machine is near other people.
- Asterisk -> --post-stop-suspend-sound Asterisk
  A mild system notification sound before suspend.
- Exclamation -> --post-stop-suspend-sound Exclamation
  A stronger warning sound before suspend.
```

If the user wants a different supported sound, accept a free-form value of `Beep`, `Hand`, `Question`, or a playable `.wav` path.

Warning sound volume question:

```text
id: post_stop_suspend_sound_volume
question: Should LidGuard temporarily override the output volume for that warning sound?
options:
- Off (Recommended) -> --post-stop-suspend-sound-volume-override-percent off
  Uses the current volume and never changes the master volume.
- 50 percent -> --post-stop-suspend-sound-volume-override-percent 50
  Makes the warning more likely to be heard, then restores the previous volume and mute state.
- 80 percent -> --post-stop-suspend-sound-volume-override-percent 80
  Loud warning for unattended runs; warn the user before choosing this.
```

If the user wants another value, accept a free-form integer from `1` through `100`.

Closed-lid permission request question:

```text
id: closed_lid_permission_request_decision
question: How should provider permission requests behave while the laptop lid is closed?
options:
- Deny while closed (Recommended) -> --closed-lid-permission-request-decision deny
  Safer default; permission-required agent actions are rejected while the user may not be watching.
- Allow while closed -> --closed-lid-permission-request-decision allow
  Enables more complete closed-lid automation, but permission-required actions can proceed unattended.
```

Before offering `allow`, explain the risk plainly: it can let the provider approve permission-required work while the laptop is closed, so the user must be comfortable with the agent continuing without direct supervision.

After collecting answers, apply them with a settings command assembled from the selected values, for example:

```bash
lidguard settings --prevent-display-sleep false --suspend-mode sleep --post-stop-suspend-sound off --post-stop-suspend-sound-volume-override-percent off --closed-lid-permission-request-decision deny
```

## 7. Explain Webhook Notifications

After installation, tell the user that LidGuard can also send `PreSuspend` and `PostSessionEnd` webhooks. With the optional `LidGuard.Notifications` companion server, they can receive mobile or secondary-device Web Push notifications and inspect completion results from another device.

Do not configure webhook URLs unless the user provides an endpoint or explicitly asks you to set up the notification server.

## 8. Final Message To The User

Before writing the final message, run:

```bash
lidguard status
```

Use the current `lidguard status` output as the source of truth for the final report. Report only the current settings concretely instead of saying only that settings were applied. Include the values shown in status for the relevant protection, suspend, sound, webhook, timeout, runtime cleanup, and localization settings when they are present.

Also tell the user that they can run this command to see detailed explanations for the settings parameters shown in status:

```bash
lidguard help settings
```

When finished, summarize:

- The detected platform.
- Whether .NET 10 was already present or installed.
- The installed LidGuard version if you can determine it.
- Which provider hooks and MCP servers were installed or skipped.
- Whether WSL was detected, and whether WSL hook/MCP integration was installed, skipped, or declined.
- Whether Provider MCP was installed for a non-native provider, including the provider name and config path.
- The concrete current settings from `lidguard status`.
- Any permission checks that still need manual attention.

Also tell the user that provider hooks and MCP changes may not affect the current conversation/session depending on the provider, but should be picked up from the next new conversation or provider session.
