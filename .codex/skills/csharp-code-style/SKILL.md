---
name: csharp-code-style
description: Mandatory C# code style rules for naming, formatting, post-edit cleanup, and modern C# syntax. Use whenever Codex creates, modifies, reviews, refactors, or explains C# code or project files, including .cs, .csproj, and .NET project work.
---

# C# Code Style

## Overview

Read this skill before making any C#-related code changes. Apply every rule below strictly unless the user explicitly overrides a rule for the current task.

## C# MCP Usage

- When working in a C# project, use the `csharp-lsp-mcp` MCP server whenever language-server context would help, especially for diagnostics, hover/type information, definitions, references, symbols, code actions, rename previews, and XAML analysis.
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

- Simple single-line `if`, `for`, `foreach`, and `while` statements must omit braces and keep the body on the same physical line: `if (condition) myValue = true;`.
- If a simple `if`, `for`, `foreach`, or `while` body is a single statement, keep the control statement and body on one line.

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
- Keep short method calls, short method definitions, and short argument lists on a single line. Do not split clearly short calls or signatures only for visual wrapping.
- Ternary conditional expressions (`condition ? whenTrue : whenFalse`) must stay on a single physical line. The resulting line may exceed 220 characters; that is allowed for this rule.
- Logical AND, logical OR, and null-coalescing expressions (`left && right`, `left || right`, `left ?? right`) must stay on a single physical line. The resulting line may exceed 220 characters; that is allowed for this rule.
- Parameter lists must stay on a single physical line no matter how long they are, for both calls and definitions/declarations. This applies to method calls, method declarations, constructor calls, constructor declarations, delegates, lambdas, and primary constructors. The resulting line may exceed 220 characters; definitions/declarations such as method declarations must remain on one physical line even when they exceed 220 characters.
- Do not compress braced blocks into one line inside methods, `if`, `for`, `foreach`, `while`, `using`, `try`, `catch`, or `finally` blocks.
- When a method or control statement contains nested braced logic, use expanded block formatting for the outer and nested blocks.

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
        if (ShouldProcessItem(item))
        {
            ProcessItem(item);
        }
    }
}
```

- Single-line `try`, `catch`, and `finally` blocks must each stay on one line.

```csharp
try { ProcessItem(item); }
catch (Exception exception) { LogException(exception); }
```

- Use primary constructors wherever possible.
- Use collection expressions (`[item1, item2]`) wherever possible.
- Actively use the latest C# language features and syntax.

## Automated Guard

- For C# formatting verification or automatic cleanup of the ternary-expression, logical/null-coalescing binary-expression, and parameter-list rules above, use the Roslyn-based guard in `tools/CSharpStyleGuard`.
- Check files or directories with:

```powershell
dotnet run --project C:\Users\kck41\.codex\skills\csharp-code-style\tools\CSharpStyleGuard\CSharpStyleGuard.csproj -- --check <path>
```

- Automatically rewrite safe cases with:

```powershell
dotnet run --project C:\Users\kck41\.codex\skills\csharp-code-style\tools\CSharpStyleGuard\CSharpStyleGuard.csproj -- --fix <path>
```

- The guard intentionally allows lines over 220 characters when enforcing these single-line rules.
- The guard skips automatic rewriting for spans that contain line comments, multiline comments, preprocessor directives, disabled text, or multiline braced syntax, and reports those cases for manual cleanup.
- `dotnet run` may build the guard project before execution, so obey repository-local build restrictions before running it.
