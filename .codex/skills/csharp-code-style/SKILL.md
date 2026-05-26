---
name: csharp-code-style
description: Mandatory C# code style rules for naming, formatting, post-edit cleanup, and modern C# syntax. Use whenever Codex creates, modifies, reviews, refactors, or explains C# code or project files, including .cs, .csproj, and .NET project work.
---

# C# Code Style

## Overview

Read this skill before making any C#-related code changes. Apply every rule below strictly unless the user explicitly overrides a rule for the current task.

## C# MCP Usage

- When working in a C# project, actively use the `csharp-lsp-mcp` MCP server as the default way to gather language-server context, especially for diagnostics, hover/type information, definitions, references, symbols, code actions, rename previews, and XAML analysis.
- Prefer checking LSP diagnostics and symbols before and after meaningful C# edits when the MCP server is available.
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

- Simple single-line `if`, `for`, `foreach`, and `while` statements must omit braces and keep the body on the same physical line when the resulting physical line is 320 characters or shorter: `if (condition) myValue = true;`.
- If a simple `if`, `for`, `foreach`, or `while` body is a single statement and the resulting physical line is 320 characters or shorter, keep the control statement and body on one line.

Forbidden:

```csharp
if (condition)
    return;

for (var index = 0; index < count; index++)
    ProcessItem(items[index]);
```

Correct:

```csharp
if (condition) return;
if (shouldRefreshLayout) RefreshLayout();
for (var index = 0; index < count; index++) ProcessItem(items[index]);
foreach (var item in items) ProcessItem(item);
while (enumerator.MoveNext()) ProcessItem(enumerator.Current);
```

- Single-line methods must use expression-bodied syntax (`=>`).
- If an expression-bodied member's `=>` is split onto the next line, move `=>` onto the declaration line when the resulting physical line is 320 characters or shorter. For object initializers, keep the `{` and `}` at the same indentation depth as the member declaration. For collection expressions, keep the `[` and `]` at the same indentation depth as the member declaration.
- Keep short method calls, short method definitions, and short argument lists on a single line. Do not split clearly short calls or signatures only for visual wrapping.
- Ternary conditional expressions (`condition ? whenTrue : whenFalse`) must stay on a single physical line. The resulting line may exceed 320 characters; that is allowed for this rule.
- Logical AND, logical OR, and null-coalescing expressions (`left && right`, `left || right`, `left ?? right`) must stay on a single physical line. The resulting line may exceed 320 characters; that is allowed for this rule.
- Parameter lists must stay on a single physical line no matter how long they are, for both calls and definitions/declarations. This applies to method calls, method declarations, constructor calls, constructor declarations, delegates, lambdas, and primary constructors. The resulting line may exceed 320 characters; definitions/declarations such as method declarations must remain on one physical line even when they exceed 320 characters.
- Do not compress braced blocks into one line inside methods, `if`, `for`, `foreach`, `while`, `using`, `try`, `catch`, or `finally` blocks.
- When a method, control statement, `try`, `catch`, or `finally` block has nested control flow or nested braced logic compressed into the same physical line, use expanded block formatting to unfold that compact nested structure. For an already multiline parent block, apply nested expansion only when the parent body is that single compact braced nested statement; otherwise the single-line control-statement rule still applies to simple nested `if`, `for`, `foreach`, and `while` bodies.

Forbidden:

```csharp
private void ProcessItems() { foreach (var item in items) { if (ShouldProcessItem(item)) { ProcessItem(item); } } }

foreach (var item in items) { if (ShouldProcessItem(item)) { ProcessItem(item); } }
```

Correct:

```csharp
private void ProcessItems()
{
    foreach (var item in items)
    {
        if (ShouldProcessItem(item)) ProcessItem(item);
    }
}
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

- For C# formatting verification or automatic cleanup of the ternary-expression, logical/null-coalescing binary-expression, pattern spacing, parameter-list, single-statement control-flow, nested braced block, constructor initializer, and expression-bodied member rules above, use the Roslyn-based guard in `tools/CSharpStyleGuard`.
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

- The guard intentionally allows lines over 320 characters for ternary, logical/null-coalescing, and parameter-list rules. The guard applies the 320-character threshold to control-flow, exception-handling, constructor-initializer, and expression-bodied member rewrites that would otherwise create a new single physical line.
- The guard skips automatic rewriting for spans that contain line comments, multiline comments, preprocessor directives, disabled text, or unsafe multiline braced syntax, and reports those cases for manual cleanup.
- The guard's `dotnet run` commands are exempt from repository-local build restrictions and may be run whenever needed, even when ordinary project builds are prohibited.
