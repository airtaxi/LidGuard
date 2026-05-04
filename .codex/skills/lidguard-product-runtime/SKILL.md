---
name: lidguard-product-runtime
description: "LidGuard product runtime overview and routing guide. Use when working on high-level product goals or deciding which LidGuard runtime skill to read for power/lid/suspend behavior, session lifecycle, CLI/settings, MCP behavior, or failure handling."
---

# LidGuard Product Runtime

## Overview

Use this skill as the lightweight entry point for LidGuard runtime behavior. For detailed work, load only the narrow runtime skill that matches the task.

- Power management, lid policy, suspend eligibility, and Emergency Hibernation: read `.codex/skills/lidguard-power-runtime/SKILL.md`.
- Active session lifecycle, soft-locking, transcript activity, process watching, runtime cleanup, and session-end webhook semantics: read `.codex/skills/lidguard-session-runtime/SKILL.md`.
- CLI command routing, settings defaults, permission commands, examples, suspend history, and failure modes: read `.codex/skills/lidguard-cli-runtime/SKILL.md`.
- Regular MCP server tools and model-managed Provider MCP runtime behavior: read `.codex/skills/lidguard-mcp-runtime/SKILL.md`.
- Provider hook installation, provider-specific payloads, and provider deployment behavior: read `.codex/skills/lidguard-provider-integrations/SKILL.md`.

## Product Goal

LidGuard is a Windows-first utility with systemd/logind Linux support and macOS support for long-running local AI coding agents such as Codex, Claude Code, and GitHub Copilot CLI.

The goal is to keep the supported local system awake while at least one tracked agent session still needs protection, then restore the user's original power policy after the session ends or becomes suspend-eligible.

- Agent sessions start through provider hooks or Provider MCP tools.
- LidGuard detects and tracks active sessions.
- Claude Code and GitHub Copilot CLI sessions can enter a runtime-managed soft-lock state when provider notifications show the agent is waiting on user input.
- Codex, Claude Code, GitHub Copilot CLI, and Provider MCP sessions can also be soft-locked through runtime policy or MCP tools when autonomous work has paused.
- While at least one non-soft-locked session is active, LidGuard applies platform keep-awake protection.
- If every remaining active session is soft-locked, LidGuard releases temporary keep-awake protection, restores temporary lid policy changes, and starts the configured suspend flow only when the lid is closed and no suspend-blocking visible display monitors remain attached.
- If a session has no activity after the configured session timeout, LidGuard transitions it to the soft-locked state and applies the same keep-awake release flow used for normal soft-lock operations.
- When sessions stop, all temporary power settings must be restored to the user's original values.
- After the last active session stops, LidGuard should request suspend when the laptop lid is closed and no suspend-blocking visible display monitors remain attached to the desktop.
- If active sessions remain but all of them are soft-locked, LidGuard should follow the same suspend path without waiting for stop hooks.
- The suspend mode remains user-selectable: Sleep by default, Hibernate optional.
- The post-stop suspend delay remains user-selectable: 10 seconds by default, `0` for immediate suspend.
- The inactive session timeout remains user-selectable: 12 minutes by default, `off` optional, and enabled values must be at least 1 minute.
- Optional pre-suspend and post-session-end webhook URLs remain off by default and must not block cleanup beyond their configured timeout.
- While keep-awake protection is applied and the laptop lid is closed with no suspend-blocking visible display monitors remaining, optional Emergency Hibernation should request immediate hibernation when the configured thermal threshold is reached.

## Core Design Rules

- Treat normal idle sleep and lid-close sleep as separate problems.
- Use platform idle sleep prevention for ordinary keep-awake behavior: Windows power requests, Linux systemd/logind inhibitors, and macOS `caffeinate`.
- Use platform lid-close policy protection only for lid-close behavior: Windows `LIDACTION`, Linux `handle-lid-switch` inhibition, and macOS `pmset disablesleep`.
- Restore user power policy after protection ends or during the next recovery path where a pending backup exists.
- Keep runtime state ref-counted by active session, not by provider process alone.
- Treat soft-locked sessions as suspend-eligible while preserving enough identity to resume or clean them up correctly.
- Keep runtime cleanup delayed until in-flight suspend, restore, webhook, sound playback, or equivalent cleanup work finishes.
- Use hook process ancestry, not working-directory process scans, to auto-attach watched processes for managed provider hooks. Working directory remains metadata for status, logs, transcript fallback, and webhook payloads.
- Store persisted timestamps from UTC sources; convert to system local time only for user-facing CLI or web output.
