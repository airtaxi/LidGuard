---
name: lidguard-mcp-runtime
description: "LidGuard MCP runtime reference. Use when working on regular MCP server tools, Provider MCP server behavior, MCP install/status/remove runtime expectations, model-managed session identifiers, MCP settings updates, or stdio logging constraints."
---

# LidGuard MCP Runtime

## Regular MCP Server

- Host the regular stdio MCP server through `lidguard mcp-server`.
- `mcp-status` inspects the provider's global/user MCP configuration and reports whether the `lidguard` server entry is present and still points at the current LidGuard executable plus `mcp-server`.
- `mcp-install` and `mcp-remove` register or remove the user/global LidGuard stdio MCP server named `lidguard` for Codex, Claude Code, and GitHub Copilot CLI.
- `mcp-install` refreshes an existing managed LidGuard MCP registration, including one that points at an older LidGuard executable, by removing the existing provider entry first, then reinstalling it with the current command and arguments.
- Prefer the current `lidguard.exe` path over the Windows `.cmd` shim when registering stdio MCP servers, because shim wrapper processes can remain visible under MCP clients and should not be mistaken for agent work.
- Expose `get_settings_status`, `list_sessions`, `update_settings`, `remove_session`, `set_session_soft_lock`, and `clear_session_soft_lock`.
- Make `list_sessions` return the active session list plus runtime lid/session state without the full settings payload.
- Make `update_settings` accept multiple setting fields in a single request and persist them together.
- Expose inactive session timeout through `sessionTimeoutMinutes`, accepting `off` or an enabled minute count of at least 1.
- Expose server runtime cleanup delay through `serverRuntimeCleanupDelayMinutes`, accepting `off` to keep the runtime alive, `0` for immediate exit, or a positive minute count to wait.
- Expose post-session-end webhook URL through `postSessionEndWebhookUrl`, accepting an empty string to clear it.
- Make `remove_session` manually remove active sessions by session identifier and optionally narrow removal to one provider and one MCP provider name.
- Keep `set_session_soft_lock` and `clear_session_soft_lock` general-purpose by accepting provider and session identifier inputs, so non-MCP providers can also use MCP-driven soft-lock control when they can supply those values.
- Use the same named-pipe client and settings store for MCP settings updates that the CLI uses.
- Do not launch `run-server` from MCP settings updates when no runtime is listening.
- Keep MCP server logging on stderr so stdio tool traffic remains clean.

## Provider MCP Server

- Host the separate stdio Provider MCP server through `lidguard provider-mcp-server --provider-name <name>`.
- `provider-mcp-install` and `provider-mcp-remove` directly edit a caller-supplied JSON config file and register or remove a managed stdio server entry for `provider-mcp-server`.
- `provider-mcp-status` inspects a caller-supplied JSON config file and reports whether the managed server entry still points at the current LidGuard executable plus `provider-mcp-server`.
- Do not use Codex, Claude Code, or GitHub Copilot CLI-specific MCP registration commands in the generic Provider MCP config path.
- Use the same MCP executable selection policy as `mcp-install`: prefer the current `lidguard.exe` path over the Windows `.cmd` shim.
- Expose `provider_start_session`, `provider_stop_session`, `provider_set_soft_lock`, and `provider_clear_soft_lock`.
- Use `provider_start_session` once before a brand-new provider session begins autonomous work.
- Generate an 8-character lowercase hexadecimal `sessionIdentifier` from the first block of a new GUID and return that value for reuse.
- Require the model to reuse the exact `sessionIdentifier` returned by `provider_start_session` in `provider_set_soft_lock`, `provider_clear_soft_lock`, and `provider_stop_session` until the work is truly complete.
- Use `provider_set_soft_lock` before a turn ends because the model needs user input and wants LidGuard to release keep-awake protection.
- Remember that the tool itself cannot end the turn; the model still has to stop or hand back the conversation after calling it.
- When resuming a previously soft-locked Provider MCP session after a user reply, call `provider_clear_soft_lock` with the earlier returned `sessionIdentifier` instead of starting a brand-new session.
- Document Provider MCP behavior as best-effort rather than guaranteed, because correct behavior depends on the model calling tools at the right times.

## Examples

```powershell
lidguard mcp-server
lidguard mcp-status codex
lidguard mcp-install codex
lidguard mcp-remove codex
lidguard mcp-status claude
lidguard mcp-install claude
lidguard mcp-remove claude
lidguard mcp-status copilot
lidguard mcp-install copilot
lidguard mcp-remove copilot
lidguard mcp-status all
lidguard mcp-install all
lidguard mcp-remove all
lidguard provider-mcp-status --config "C:\path\to\mcp.json"
lidguard provider-mcp-install --config "C:\path\to\mcp.json" --provider-name "ExampleProvider"
lidguard provider-mcp-remove --config "C:\path\to\mcp.json"
lidguard provider-mcp-server --provider-name "ExampleProvider"
```
