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

## Core Rules

- Use full, unabbreviated names for all variables and methods. For example, use `GreatestCommonDivisor` and `LeastCommonMultiple`; never use `Gcd` or `Lcm`.
- Abbreviations are allowed only when the abbreviated form is overwhelmingly more common than the expanded form, such as `IP` or `AC`/`DC`, or when the abbreviation is effectively a standard term, such as `Regex`.
- Maintain consistent indentation and spacing at all times.
- Prefer `var` declarations over explicit type declarations.
- Preserve the comment language and style of the referenced file unless explicitly instructed otherwise. If comments are in English, write comments in English. If comments are in Korean, write comments in Korean.
- After editing code, inspect the touched and nearby C# code. If you find code that violates this skill, fix it before finishing.

## Naming

- Private instance fields: `_camelCase`.
- Private static fields: `s_camelCase`.
- Properties, methods, classes, and enums: `PascalCase`.
- Variable names must never use abbreviations unless the abbreviated form is overwhelmingly more common than the expanded form or is effectively a standard term, such as `IP`, `AC`/`DC`, or `Regex`. Use full, descriptive names otherwise.

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

- For C# formatting verification or automatic cleanup of the ternary-expression, logical/null-coalescing binary-expression, pattern spacing, collection-expression keyword spacing, object-creation argument-list spacing, single-expression parameter/argument-list, single-statement control-flow, nested braced block, constructor initializer, and expression-bodied member rules above, use the Roslyn-based guard in `tools/CSharpStyleGuard`.
- Before reporting C# work as complete, and before any commit that includes C# changes, run the guard's `--fix` mode on the relevant project paths. If repository-local restrictions prevent running `dotnet run`, state that explicitly before finishing.
- Check files or directories with:

```powershell
dotnet run --project C:\Users\kck41\.codex\skills\csharp-code-style\tools\CSharpStyleGuard\CSharpStyleGuard.csproj -- --check <path>
```

- Automatically rewrite safe cases in the current git diff with:

```powershell
dotnet run --project C:\Users\kck41\.codex\skills\csharp-code-style\tools\CSharpStyleGuard\CSharpStyleGuard.csproj -- --fix <path>
```

- In a git repository, `--fix` rewrites only diagnostics whose spans intersect staged or unstaged git diff lines by default. Untracked C# files under the input paths are treated as fully changed.
- If no git repository is found for the input paths, `--fix` falls back to fixing the full input paths and prints a warning in the report.
- To run the previous full-input fix behavior intentionally, pass `--all`:

```powershell
dotnet run --project C:\Users\kck41\.codex\skills\csharp-code-style\tools\CSharpStyleGuard\CSharpStyleGuard.csproj -- --fix --all <path>
```

- The guard intentionally allows lines over 320 characters for ternary, logical/null-coalescing, and single-expression parameter/argument-list rules. The guard applies the 320-character threshold to control-flow, exception-handling, constructor-initializer, and expression-bodied member rewrites that would otherwise create a new single physical line.
- The guard skips automatic rewriting for spans that contain line comments, multiline comments, preprocessor directives, disabled text, or unsafe multiline braced syntax, and reports those cases for manual cleanup. For CSG0002 specifically, the guard reports only cases it can safely rewrite automatically, including simple block lambdas with one `return expression;` or one expression statement.
- The guard's `dotnet run` commands are exempt from repository-local build restrictions and may be run whenever needed, even when ordinary project builds are prohibited.
