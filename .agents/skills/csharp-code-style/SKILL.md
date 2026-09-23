---
name: csharp-code-style
description: Mandatory C# code style rules for naming, formatting, post-edit cleanup, and modern C# syntax. Use whenever Codex creates, modifies, reviews, refactors, or explains C# code or project files, including .cs, .csproj, and .NET project work.
---

# C# Code Style

## Overview

Read this skill before making any C#-related code changes. Apply every rule below strictly unless the user explicitly overrides a rule for the current task.

## Repository Setup

- When creating a new Git repository for a .NET/C# project, generate the standard .NET `.gitignore` with `dotnet new gitignore`.
- If a WinUI project or another project type needs `.pubxml` publish profiles to remain tracked, comment out the default `.gitignore` rule that excludes `*.pubxml` instead of deleting it. Keep user-specific publish profile ignores such as `*.pubxml.user` in place.

## C# MCP Usage

- When working in a C# project, actively use the `csharp-lsp-mcp` MCP server as the default way to gather language-server context, especially for diagnostics, hover/type information, definitions, references, symbols, code actions, rename previews, and XAML analysis.
- Prefer checking LSP diagnostics and symbols before and after meaningful C# edits when the MCP server is available.
- If a task already requires or explicitly asks for a project build after edits, skip LSP diagnostic passes that would be superseded by that build and proceed directly to the build when validation is needed.
- Before using its C# or XAML tools in a project, call `csharp_set_workspace` with the solution or project directory.
- Before rebuilding a project after the MCP has loaded it, call `csharp_stop` to release LSP-held file locks, then call `csharp_set_workspace` again after the rebuild if more C# or XAML analysis is needed.

## CsWin32 Generated File Inspection

CsWin32 (`Microsoft.Windows.CsWin32`) is a Roslyn source generator — it does not write generated `.cs` files to disk by default. Generated P/Invoke signatures exist only in memory during compilation and are embedded directly into the assembly. When you need to inspect the exact generated signatures (parameter types, `unsafe` modifiers, `WIN32_ERROR` return types, pointer vs marshalling forms, etc.), use an isolated temporary inspection project so the real project is never modified.

### Workflow

1. **Primary agent picks a temp project path** under the OpenCode temp directory, e.g. `C:/Users/kck41/AppData/Local/Temp/opencode/cswin32-inspect-<timestamp>`. Pass this absolute path and the list of Win32 functions/methods to inspect to the subagent.
2. **Delegate to a subagent** via the `Task` tool. The subagent should (reason and respond in English):
   - Create a new class library project at the path the primary agent provided: `dotnet new classlib -o <tempPath>`.
   - Add the same `Microsoft.Windows.CsWin32` package version that the real project uses (check the real project's `.csproj` or `packages.lock.json` for the exact version): `dotnet add package Microsoft.Windows.CsWin32 --version <version>` — work inside `<tempPath>`.
   - Copy the real project's `NativeMethods.json` into the temp project root, or create one listing the functions to generate. The CsWin32 configuration (`allowMarshaling`, `emitSingleFile`, `public`, etc.) must match the real project, because these options change the emitted signature shapes significantly.
   - Add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to the temp project's `<PropertyGroup>`.
   - Match the real project's `<TargetFramework>` so the generated code targets the same framework.
   - Build the temp project: `dotnet build <tempPath>` — the source generator writes files under `obj/<Platform>/<Configuration>/<TargetFramework>/generated/` (or the CsWin32 subfolder within).
   - Read the generated `.cs` file(s) and report back the exact method signatures.
3. **Do NOT clean up the temp project immediately.** If follow-up inspections are needed (missing functions, different `NativeMethods.json` options, etc.), reuse or re-run the same subagent against the same temp project so the context is preserved.
4. **Clean up the temp project only as the final step** when all inspection goals have been met and no further CsWin32 signature lookups are expected in the current session. Delete the entire `<tempPath>` directory.

Always verify P/Invoke signatures against the actual generated output rather than guessing from memory or other projects.

## Core Rules

- Use full, unabbreviated names for all variables and methods. For example, use `GreatestCommonDivisor` and `LeastCommonMultiple`; never use `Gcd` or `Lcm`.
- Abbreviations are allowed only when the abbreviated form is overwhelmingly more common than the expanded form, such as `IP` or `AC`/`DC`, or when the abbreviation is effectively a standard term, such as `Regex`.
- Maintain consistent indentation and spacing at all times.
- Prefer `var` declarations over explicit type declarations.
- Preserve the comment language and style of the referenced file unless explicitly instructed otherwise. If comments are in English, write comments in English. If comments are in Korean, write comments in Korean.
- After editing code, inspect the touched and nearby C# code. If you find code that violates this skill, fix it before finishing.
- When adding a NuGet package to a project, always use `dotnet add package` against the actual project so the latest available version is resolved automatically. Never guess or hard-code a package version from memory. If a specific version is required, pass it explicitly with `--version`, but still run `dotnet add package` to apply it rather than hand-editing `PackageReference` entries.

## Naming

- Private instance fields: `_camelCase`.
- Private static fields: `s_camelCase`.
- Properties, methods, classes, and enums: `PascalCase`.
- Variable names must never use abbreviations unless the abbreviated form is overwhelmingly more common than the expanded form or is effectively a standard term, such as `IP`, `AC`/`DC`, or `Regex`. Use full, descriptive names otherwise.

## File Organization

- Each `.cs` file should contain a single primary type (class, record, struct, enum, interface, or delegate). Do not place multiple top-level types in one file.
- Tightly coupled helper types that exist solely to support a single primary type — such as a private nested `enum`, a private nested `record` used only by that type, or a file-scoped helper type — may remain in the same file when they are small and have no independent reuse. When in doubt, split them into their own files.
- The file name must match the primary type name (e.g., `FileSystemProviderSnapshotStore.cs` for `class FileSystemProviderSnapshotStore`).
- When generating new source files, prefer one responsibility per file so each type can be located, reviewed, and tested independently.

## C# Formatting

- Simple top-level single-statement control statements in a method, local function, accessor, or lambda body must omit braces and keep the body on the same physical line when the resulting physical line is 320 characters or shorter. This applies to `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, `lock`, `using` statements, and `fixed` statements.
- Treat `else if` as a single chain form. Do not rewrite `else if` as `else { if (...) ... }`.
- In an `if`/`else if`/`else` chain, keep each branch keyword on its own physical line; do not collapse the entire chain onto one physical line.
- `try`, `catch`, `finally`, `switch`, `checked`, `unchecked`, and `unsafe` blocks are not subject to the single-line omit-braces rule because they require or should keep braced blocks.
- A control statement is nested when it appears inside the braced body of another control statement, method, local function, accessor, or lambda, at any depth. When a nested `if` without `else`, `for`, `foreach`, `while`, `do`, `lock`, `using`, or `fixed` statement is the only statement inside that braced block, it must use expanded block formatting with braces, even when its body is a single statement and would fit on one physical line. `if`/`else if`/`else` chains with at least one `else` follow the single-line branch rule even when they are the only nested statement inside another control block, but each branch body must still be a simple non-control statement.
- When a braced block contains multiple statements, simple nested `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, `lock`, `using`, and `fixed` statements follow the single-line omit-braces rule.
- If the single-line omit-braces rule and the only-nested-control-statement rule conflict, the only-nested-control-statement rule wins except for `if`/`else if`/`else` chains with at least one `else`.

Forbidden:

```csharp
if (condition)
    return;

for (var index = 0; index < count; index++)
    ProcessItem(items[index]);

if (File.Exists(candidateSkillFilePath))
{
    if (TryCreateSkillItem(providerKind, skillsRootPath, childDirectoryPath, out var skillItem)) skillItems.Add(skillItem);
}

if (outerCondition)
{
    if (firstCondition) ProcessFirst();
}

if (shouldUpdate) UpdateState();
else lock (_gate) ResetState();

foreach (var item in items)
{
    for (var index = 0; index < item.Count; index++) ProcessItem(item[index]);
}

public IReadOnlyList<int> GetItems()
{
    lock (_gate) return [..items];
}

private void NavigateToPage(Type pageType, object pageParameter)
{
    if (Frame is not null)
    {
        Frame.Navigate(pageType, pageParameter);
    }
    else
    {
        ManageWindow.Navigate(pageType, pageParameter);
    }
}
```

Correct:

```csharp
if (condition) return;
if (shouldRefreshLayout) RefreshLayout();
if (shouldApplyLayout) ApplyLayout();
else if (shouldInvalidateLayout) InvalidateLayout();
else ResetLayout();
for (var index = 0; index < count; index++) ProcessItem(items[index]);
foreach (var item in items) ProcessItem(item);
while (enumerator.MoveNext()) ProcessItem(enumerator.Current);
do ProcessItem(item); while (shouldContinue);
lock (_gate) UpdateState();

if (File.Exists(candidateSkillFilePath))
{
    if (TryCreateSkillItem(providerKind, skillsRootPath, childDirectoryPath, out var skillItem))
    {
        skillItems.Add(skillItem);
    }
}
else ScanSkillDirectoriesRecursive(providerKind, skillsRootPath, childDirectoryPath, skillItems);

if (outerCondition)
{
    if (firstCondition) ProcessFirst();
    else if (secondCondition) ProcessSecond();
    else ProcessDefault();
}

if (firstCondition)
{
    if (secondCondition) ProcessSecond();
    else if (firstCondition) ProcessFirst();
    else ProcessDefault();
    lock (_gate) ProcessFirst();
}
else ProcessDefault();

if (firstCondition)
{
    if (secondCondition)
    {
        if (thirdCondition)
        {
            if (fourthCondition)
            {
                ProcessItem();
            }
        }
    }
}

if (shouldUpdate)
{
    lock (_gate)
    {
        UpdateState();
    }
}
else
{
    lock (_gate)
    {
        ResetState();
    }
}

public IReadOnlyList<int> GetItems()
{
    lock (_gate)
    {
        return [..items];
    }
}

private void NavigateToPage(Type pageType, object pageParameter)
{
    if (Frame is not null) Frame.Navigate(pageType, pageParameter);
    else ManageWindow.Navigate(pageType, pageParameter);
}
```

- Single-line methods must use expression-bodied syntax (`=>`).
- If an expression-bodied member's `=>` is split onto the next line, move `=>` onto the declaration line when the resulting physical line is 320 characters or shorter. For object initializers, keep the `{` and `}` at the same indentation depth as the member declaration. For collection expressions, keep the `[` and `]` at the same indentation depth as the member declaration.
- Attribute lists on expression-bodied members must stay on their own physical lines before the member declaration; never merge an attribute list and the declaration into one line.
- Keep short method calls, short method definitions, and short argument lists on a single line. Do not split clearly short calls or signatures only for visual wrapping.
- Ternary conditional expressions (`condition ? whenTrue : whenFalse`) must stay on a single physical line. The resulting line may exceed 320 characters; that is allowed for this rule.
- Logical AND, logical OR, and null-coalescing expressions (`left && right`, `left || right`, `left ?? right`) must stay on a single physical line. The resulting line may exceed 320 characters; that is allowed for this rule.
- Multiline `switch` expressions are excluded from automatic single-line cleanup; keep them multiline even when the containing expression-bodied member, control statement, or argument expression could otherwise fit on one physical line.
- Collection expressions returned by `return` or `yield return` must keep one space after the keyword. Use `return [..items];`, never `return[..items];`.
- Object creation argument lists must not have whitespace between the type and opening parenthesis. Use `new string(' ', count)`, never `new string (' ', count)`.
- Parameter and argument lists whose contents can be safely represented as a single expression must stay on a single physical line no matter how long they are, for both calls and definitions/declarations. This applies to method calls, method declarations, constructor calls, constructor declarations, delegates, lambdas, and primary constructors. The resulting line may exceed 320 characters; definitions/declarations such as method declarations must remain on one physical line even when they exceed 320 characters. Do not force CSG0002 on argument lists that contain multi-statement braced lambdas or other multiline braced syntax that should remain expanded. Simple block lambdas with exactly one `return expression;` statement or one expression statement should be collapsed to expression lambdas during cleanup.
- Do not compress braced blocks into one line inside methods, `if`, `else if`, `else`, `for`, `foreach`, `while`, `do`, `lock`, `using`, `fixed`, `try`, `catch`, or `finally` blocks.
- When a method, control statement, `try`, `catch`, or `finally` block has nested control flow or nested braced logic compressed into the same physical line, use expanded block formatting to unfold that compact nested structure. For an already multiline parent control block, expand a nested control statement only when it is the only statement in that parent block; otherwise keep simple nested control statements on one line when they fit.

Forbidden:

```csharp
private void ProcessItems() { foreach (var item in items) { if (ShouldProcessItem(item)) { ProcessItem(item); } } }

foreach (var item in items) { if (ShouldProcessItem(item)) { ProcessItem(item); } }

s_data.Favorites.Where(favorite =>
{
    return source == null || favorite.Source == source;
});
```

Correct:

```csharp
private void ProcessItems()
{
    foreach (var item in items)
    {
        if (ShouldProcessItem(item))
        {
            ProcessItem(item);
        }
    }
}

s_data.Favorites.Where(favorite => source == null || favorite.Source == source);

dispatcherQueue.TryEnqueue(async () =>
{
    var bitmapImage = new BitmapImage { AutoPlay = SettingsManager.GifPlaybackEnabled };
    await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
    taskCompletionSource.SetResult(bitmapImage);
});
```

- Empty and simple single-statement `try`, `catch`, and `finally` blocks must each stay on one line when the resulting physical line is 320 characters or shorter. If the block contains nested control flow or nested braced logic, expand the outer and nested blocks instead.

```csharp
try { ProcessItem(item); }
catch (Exception exception) { LogException(exception); }
```

- Empty constructors with `this` or `base` initializers must stay on one line when the resulting physical line is 320 characters or shorter.

```csharp
public FileSystemProviderSnapshotStore() : this(rootDirectoryPath, windowsDataProtectionService) { }
```

- Use primary constructors wherever possible.
- Use collection expressions (`[item1, item2]`) wherever possible.
- Actively use the latest C# language features and syntax.

## Automated Guard

### Workflow (MANDATORY)

When validating C# style after edits, you MUST follow this exact workflow. Do NOT deviate from it.

1. **Run `--fix` first, never `--check` then hand-edit.** Always run the guard's `--fix` mode on the relevant project paths before reporting C# work as complete, and before any commit that includes C# changes. Do NOT run `--check` and then manually rewrite every diagnostic by hand — `--fix` already rewrites the safe cases automatically. Only manually fix the spans that `--fix` reports as skipped (unsafe spans: line comments, multiline comments, preprocessor directives, disabled text, unsafe multiline braced syntax).
2. **Do NOT re-read files just to verify guard-applied auto-fixes.** After `--fix` completes, if the guard did NOT report any unsafe/skipped spans, do NOT open the affected files in a `Read` call to "confirm" what the guard changed. The guard's fix report is authoritative. Re-reading those files wastes cache-read tokens for no benefit. Only open a file when you need to manually fix an unsafe span that the guard explicitly reported as skipped.
3. **If the guard reports unsafe/skipped spans**, manually fix only those spans, then you MAY re-run `--fix` on the affected files to confirm they are clean. After that confirmation run, do NOT re-read the files again unless the confirmation report still shows remaining unsafe spans.

### Guard details

- The guard project lives at `tools/CSharpStyleGuard/CSharpStyleGuard.csproj` inside this skill directory (the same directory as this `SKILL.md`). All `dotnet run` commands below use the relative path `tools/CSharpStyleGuard/CSharpStyleGuard.csproj` from this `SKILL.md`. If this skill is installed under a different `CODEX_HOME`/`ZCODE_HOME`, resolve the path relative to this `SKILL.md` instead of hard-coding `.codex`.
- For C# formatting verification or automatic cleanup of the ternary-expression, logical/null-coalescing binary-expression, pattern spacing, collection-expression keyword spacing, object-creation argument-list spacing, single-expression parameter/argument-list, single-statement control-flow, nested braced block, constructor initializer, expression-bodied member, and block-bodied-to-expression-bodied conversion rules above, use the Roslyn-based guard in `tools/CSharpStyleGuard`.
- If repository-local restrictions prevent running `dotnet run`, state that explicitly before finishing.
- Check files or directories with:

```powershell
dotnet run --project tools/CSharpStyleGuard/CSharpStyleGuard.csproj -- --check <path>
```

- Automatically rewrite safe cases in the current git diff with:

```powershell
dotnet run --project tools/CSharpStyleGuard/CSharpStyleGuard.csproj -- --fix <path>
```

- In a git repository, `--fix` rewrites only diagnostics whose spans intersect staged or unstaged git diff lines by default. Untracked C# files under the input paths are treated as fully changed.
- If no git repository is found for the input paths, `--fix` falls back to fixing the full input paths and prints a warning in the report.
- To run the previous full-input fix behavior intentionally, pass `--all`:

```powershell
dotnet run --project tools/CSharpStyleGuard/CSharpStyleGuard.csproj -- --fix --all <path>
```

- The guard intentionally allows lines over 320 characters for ternary, logical/null-coalescing, and single-expression parameter/argument-list rules. The guard applies the 320-character threshold to control-flow, exception-handling, constructor-initializer, expression-bodied member, and block-bodied-to-expression-bodied conversion rewrites that would otherwise create a new single physical line. For block-bodied-to-expression-bodied conversions (CSG0013), when the single-line form exceeds 320 characters, the guard splits the expression onto the next line with `=>` remaining on the declaration line.
- The guard skips automatic rewriting for spans that contain line comments, multiline comments, preprocessor directives, disabled text, or unsafe multiline braced syntax, and reports those cases for manual cleanup. For CSG0002 specifically, the guard reports only cases it can safely rewrite automatically, including simple block lambdas with one `return expression;` or one expression statement. For CSG0013 specifically, the guard converts block-bodied members (methods, local functions, constructors, destructors, operators, conversion operators, and accessors) with a single `return expression;` or single expression statement to expression-bodied syntax, skipping collection-return bodies (handled by CSG0008) and multiline switch expressions.
- The guard's `dotnet run` commands are exempt from repository-local build restrictions and may be run whenever needed, even when ordinary project builds are prohibited.
