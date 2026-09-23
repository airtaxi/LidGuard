---
name: lidguard-repo-workflow
description: "Repository-local workflow rules for LidGuard. Use for every task in this repository before planning, editing, reviewing, validating, documenting, committing, staging, or running tools."
---

# LidGuard Repository Workflow

## Overview

Use this skill before every LidGuard task. `AGENTS.md` is only a router; the repository-local skills under `.codex/skills` are the authoritative project instructions.

## Mandatory Rules

- Respond in Korean. The user is a South Korean native speaker.
- Never run `git commit` or `git push` unless the user explicitly requests it.
- Before any user-requested commit, inspect recent commits and match the repository convention; LidGuard commit messages must be written in English.
- When staging many files where only a few files must be excluded, stage everything first, then unstage only those few excluded files.
- When using `rg`, use `C:\Data\Utils\Path\rg.exe`.
- Do not run builds unless the user explicitly asks for one, except when the changes are huge.
- If something is unclear or ambiguous during work, ask the user immediately and provide selectable choices when possible.
- Before creating, modifying, reviewing, refactoring, or explaining C# code or project files, read `.codex/skills/csharp-code-style/SKILL.md`. If a global skill has the same name, use this local skill instead of the global one.
- This repository is NativeAOT and trimming sensitive. Avoid APIs that trigger IL2026 / IL3050 warnings, and prefer AOT-safe overloads plus source-generated `System.Text.Json` serializers over reflection-driven or dynamic JSON helpers.
- Windows native interop must stay centralized through CsWin32-generated APIs. Do not add direct `[DllImport]` / `[LibraryImport]`, `NativeLibrary` / `GetProcAddress`, or manual COM vtable calls in project code unless CsWin32 or available metadata cannot express the API and the exception is documented in the local skills.
- `Microsoft.Windows.WDK.Win32Metadata` is intentionally referenced only to let CsWin32 generate WDK-backed APIs such as `NtQueryInformationProcess`; keep it `PrivateAssets="all"` and do not use it as permission to add hand-written native declarations.
- Persisted timestamps for sessions, runtime logs, hook logs, suspend history, backup state, notification data, and timestamped backup file names must be recorded from UTC sources such as `DateTimeOffset.UtcNow`; user-facing CLI and web output must convert stored timestamps to the current system local time immediately before display.

## Document Policy

- `AGENTS.md` must stay a short skill router, not a product/design source document.
- The repository-local skills under `.codex/skills` are the source of truth for LidGuard product direction, technical design, current implementation state, and next work.
- `AGENTS.ko.md` has been retired. Do not recreate Korean AGENTS mirrors or other duplicated planning documents.
- All other `*.ko.md` files are Korean user-readable mirrors or translations. Do not read them during routine context gathering because they duplicate source documents and waste context; read and update a `*.ko.md` file only when its source document changes meaningfully or when the user explicitly asks.
- `Plan.md` was removed to avoid duplicated planning content.
- When changing core behavior, update the relevant local skill instead of reintroducing duplicated design notes elsewhere.
- Any future repository-wide README that documents Provider MCP or model-managed MCP session flows must explicitly state that the behavior is not guaranteed, because correct operation depends entirely on the model choosing to call the LidGuard MCP tools at the right times.
- Any future repository-wide README that documents managed hook/session lifecycle behavior must explicitly state that LidGuard resolves watched processes from hook process ancestry when possible, including Codex App `app-server`, and that working directory is metadata rather than a watcher resolution source.
