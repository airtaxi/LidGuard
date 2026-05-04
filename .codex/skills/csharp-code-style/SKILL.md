---
name: csharp-code-style
description: Mandatory C# code style rules for naming, formatting, post-edit cleanup, and modern C# syntax. Use whenever Codex creates, modifies, reviews, refactors, or explains C# code or project files, including .cs, .csproj, and .NET project work.
---

# C# Code Style

## Overview

Read this skill before making any C#-related code changes. Apply every rule below strictly unless the user explicitly overrides a rule for the current task.

## Core Rules

- Use full, unabbreviated names for all variables and methods. For example, use `GreatestCommonDivisor` and `LeastCommonMultiple`; never use `Gcd` or `Lcm`.
- Maintain consistent indentation and spacing at all times.
- Prefer `var` declarations over explicit type declarations.
- Preserve the comment language and style of the referenced file unless explicitly instructed otherwise. If comments are in English, write comments in English. If comments are in Korean, write comments in Korean.
- After editing code, inspect the touched and nearby C# code. If you find code that violates this skill, fix it before finishing.

## Naming

- Private instance fields: `_camelCase`.
- Private static fields: `s_camelCase`.
- Properties, methods, classes, and enums: `PascalCase`.
- Variable names must never use abbreviations. Use full, descriptive names.

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
