# LidGuard Agent Instructions

This file is only a router for repository-local skills. The authoritative LidGuard instructions live in `.codex/skills/*/SKILL.md`.

If your agent platform does not auto-load skills, open these Markdown files directly and follow them in the order below.

## Required First Step

Read `.codex/skills/lidguard-repo-workflow/SKILL.md` before any LidGuard work.

## Task Skill Map

| Work type | Additional local skills to read |
| --- | --- |
| C# code, project files, .NET code review, refactoring, or explanation | `.codex/skills/csharp-code-style/SKILL.md` |
| High-level product behavior or choosing the right runtime reference | `.codex/skills/lidguard-product-runtime/SKILL.md` |
| Power management, lid policy, suspend eligibility, post-stop suspend, or Emergency Hibernation | `.codex/skills/lidguard-power-runtime/SKILL.md` |
| Runtime session lifecycle, soft-locking, transcript monitoring, watchdogs, webhooks, or cleanup | `.codex/skills/lidguard-session-runtime/SKILL.md` |
| CLI commands, settings defaults, permission commands, examples, suspend history, or failure modes | `.codex/skills/lidguard-cli-runtime/SKILL.md` |
| Regular MCP server behavior or Provider MCP runtime semantics | `.codex/skills/lidguard-mcp-runtime/SKILL.md` |
| Repository structure, subsystem ownership, or design constraints | `.codex/skills/lidguard-implementation-map/SKILL.md` |
| Provider MCP, Codex hooks, Claude Code hooks, GitHub Copilot CLI hooks, provider installation/status/removal behavior, or provider-specific deployment notes | `.codex/skills/lidguard-provider-integrations/SKILL.md` |
| Build validation guidance, release validation, missing work, packaging, NuGet publishing, or local install smoke tests | `.codex/skills/lidguard-release-validation/SKILL.md` |

## Local Skill Precedence

Repository-local skills take precedence over global or external skills with the same name. If `.codex/skills/<name>/SKILL.md` exists, read and follow that local file instead of any global `<name>` skill.

For C# work, read `.codex/skills/csharp-code-style/SKILL.md`; do not read the global `csharp-code-style` skill when this local copy exists.
