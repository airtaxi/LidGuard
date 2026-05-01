# LidGuard Localization Strategy

This document is an implementation-ready localization strategy for LidGuard. It is a working strategy document, not the product source of truth. `AGENTS.md` and `AGENTS.ko.md` remain the source of truth for product direction and runtime behavior; if localization changes core behavior or public guarantees, update those files in the same change.

## Decision

Use standard `.resx` resources with `System.Resources.ResourceManager`, accessed through small internal wrapper classes.

Why:

- `.resx`, `ResourceManager`, and satellite assemblies are the standard .NET localization mechanism and work across Windows, Linux, and macOS.
- LidGuard is a NativeAOT and trimming-sensitive CLI. A direct `ResourceManager` wrapper keeps the call graph explicit and avoids adding framework conventions to static command code.
- Most user-visible strings currently live in static CLI command, hook, MCP, and runtime result code. A small wrapper is easy to adopt incrementally across that surface.
- The wrapper keeps the implementation close to the repository's current command structure while still using the standard .NET resource fallback model.

## Resource Layout

Use one resource wrapper per assembly that owns user-facing text. Do not make lower-level libraries depend on the `LidGuard` app assembly for strings.

Recommended first layout:

```text
LidGuard/
  Localization/
    LidGuardText.cs
  Resources/
    LidGuardText.resx
    LidGuardText.ko.resx

LidGuardLib/
  Localization/
    LidGuardLibText.cs
  Resources/
    LidGuardLibText.resx
    LidGuardLibText.ko.resx

LidGuardLib.Commons/
  Localization/
    CommonText.cs
  Resources/
    CommonText.resx
    CommonText.ko.resx
```

Start with `LidGuard` if the first implementation only localizes CLI/help/status output. Add `LidGuardLib` and `LidGuardLib.Commons` resources when messages produced in those assemblies are localized at their source.

Avoid generated `.Designer.cs` files for the first pass. Use a manually maintained wrapper so the build does not depend on Visual Studio-specific resource designer behavior.

Add a neutral resource language attribute in each assembly that contains resources:

```csharp
[assembly: NeutralResourcesLanguage("en")]
```

Neutral English strings live in the main assembly. Korean strings live in `*.ko.resx` satellite resources.

## Wrapper Shape

Use explicit `ResourceManager` instances with stable base names. Do not dynamically discover resource assemblies.

Example shape:

```csharp
using System.Globalization;
using System.Resources;

namespace LidGuard.Localization;

internal static class LidGuardText
{
    private static readonly ResourceManager s_resourceManager = new("LidGuard.Resources.LidGuardText", typeof(LidGuardText).Assembly);

    public static string SettingsTitle => Get(nameof(SettingsTitle));

    public static string UnknownCommand(string commandName) => Format(nameof(UnknownCommand), commandName);

    private static string Get(string name)
        => s_resourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    private static string Format(string name, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(name), arguments);
}
```

Keep wrapper member names full and descriptive. Match the repository C# style: no abbreviated names, expression-bodied single-line members where practical, and `s_camelCase` for private static fields.

## Key Naming

Use semantic keys that describe the message purpose, not the English wording.

Recommended pattern:

- `Help_Usage_Title`
- `Help_Settings_Description`
- `Settings_FileLabel`
- `Settings_RuntimeUpdated`
- `Command_UnknownCommand`
- `Hook_Install_AlreadyInstalled`
- `Mcp_Status_ServerNameLabel`
- `Runtime_ProcessIdentifierRequired`

Guidelines:

- Prefer full-sentence values for translatable strings.
- Do not build localized sentences by concatenating fragments.
- Use numbered placeholders (`{0}`, `{1}`) and add translator comments in `.resx` for non-obvious values.
- Keep command names, option names, provider names, file names, enum values, and protocol tokens outside translation unless they are displayed as plain explanatory text.
- If pluralization becomes necessary, use separate keys for singular/plural or rewrite the sentence to avoid plural-sensitive grammar. Do not add a pluralization package in the first pass.

## Culture Selection

Initial behavior should follow .NET defaults:

- Resource lookup uses `CultureInfo.CurrentUICulture`.
- User-facing formatting uses `CultureInfo.CurrentCulture`.
- Protocol, JSON, logs, ISO timestamps, and machine-readable output use invariant or existing stable formatting.
- IPC and JSON payload text, including `message`, error, and denial text read by agents or provider clients, stays English.

Do not enable `InvariantGlobalization`. Localization needs culture data on Linux and macOS. App-local ICU is not required for text-only localization at this stage; revisit it only if date, number, or sorting behavior must be identical across all platforms.

Override support:

- Add `LIDGUARD_UI_CULTURE` as a process-level override for testing and support.
- Parse it once near CLI startup before any text is emitted.
- Invalid culture values should produce a clear English fallback error or be ignored with a warning.

## Language Change Option Considerations

Adding a user-visible language option is a public behavior change. When implementing this plan, update `AGENTS.md`, `AGENTS.ko.md`, help text, settings documentation, and packaging checks in the same change.

Recommended policy:

- Add a persisted `settings` option for UI language with `auto` as the default.
- Expose it through `lidguard settings --ui-culture <auto|en|ko|culture-name>` and the interactive settings flow.
- Use `auto` to mean "use the process/OS default UI culture."
- Keep `auto` in `settings.json` when the user has not selected a concrete language.
- Add `LIDGUARD_UI_CULTURE` as an environment override for testing, support, and scripted use.
- Language settings affect human CLI presentation only. They do not change IPC, hook JSON, MCP JSON, settings JSON schema, persisted log text, or generated configuration file content.

Precedence should be explicit and stable:

1. `LIDGUARD_UI_CULTURE`, if set.
2. Persisted LidGuard `settings` UI culture value, if not `auto`.
3. Process or OS `CultureInfo.CurrentUICulture`.

Validation rules:

- Accept BCP 47 culture names that `CultureInfo.GetCultureInfo` can resolve, such as `en`, `en-US`, `ko`, and `ko-KR`.
- Let .NET resource fallback handle regional values. For example, `ko-KR` can fall back to `ko`.
- Store the culture name as a string. Do not store a `CultureInfo` object.
- Keep stored values non-localized. Use `auto`, `en`, `ko`, or the exact culture name, not translated labels.
- Invalid values should not corrupt settings. Report the invalid value and keep the previous effective culture.

Scope rules:

- A language option should initially affect `CultureInfo.CurrentUICulture` only.
- Do not change `CultureInfo.CurrentCulture` unless the feature is explicitly a formatting-culture option. Changing `CurrentCulture` also changes date, number, and currency formatting, which can surprise users and tests.
- Machine-readable outputs must remain stable regardless of language.
- IPC and JSON `message` fields remain English because agents, provider clients, or scripts may read them.
- Command names, option names, JSON property names, MCP tool names, hook event names, and stored settings keys must never change by language.
- Any human-facing CLI presentation should be localized, even when the backing data originated from protocol-facing or persisted English fields.
- This includes localized rendering of runtime or inspection result messages, session list summaries, hook/MCP/provider management status text, interactive prompts, placeholder text such as `none` or `<none>`, and display labels for enum-like values.
- If a raw value must stay stable for IPC, logs, settings, or generated files, localize the presentation layer instead of the stored or serialized value.

Runtime and IPC considerations:

- Do not rely on the detached `run-server` process culture to match the culture requested by a subsequent CLI invocation.
- The `settings` command must persist the UI culture setting and send the updated setting to a running runtime, just like other runtime settings updates.
- The runtime must remember the latest UI culture setting in memory and include it in settings/status snapshots so helper processes can observe the current setting.
- MCP server and Provider MCP server processes must apply the effective UI culture on startup by reading persisted settings, then refresh their local effective culture when a runtime/settings response reports a newer value.
- When LidGuard starts a child or detached LidGuard process, pass the effective UI culture through `LIDGUARD_UI_CULTURE` so the child process does not depend on stale OS culture.
- Keep `LidGuardPipeRequest` and `LidGuardPipeResponse` raw protocol payload text stable. If runtime response `Message` values remain English for protocol or logging compatibility, the CLI must not surface them verbatim when a localized human-facing rendering can be derived.
- Prefer message codes plus arguments or structured response fields so the CLI can localize runtime/session management sentences, enum display text, placeholders, and inspection summaries without changing stored or protocol-facing text.
- Runtime state can remember and propagate the UI culture setting, but IPC messages themselves remain English.
- Log message text is not localized in v1. JSONL log field names, structured event names, and any persisted log text remain stable.

Hook and MCP considerations:

- Provider hook stdout must remain valid JSON and keep the same schema in every language.
- MCP tool names, argument names, descriptions used as protocol schema, and response property names must stay stable.
- Hook, MCP, and provider JSON `message`, `error`, and denial text should stay English. These payloads are primarily read by agents, provider clients, and automation, not by end users.
- MCP process culture is synchronized from persisted settings on startup and from runtime/settings responses while running. Do not add protocol-visible language parameters in v1.

Implementation notes:

- Add a small `LidGuardCulture` or `LidGuardCultureOptions` helper near command startup.
- Add a settings field such as `UiCulture` or `UserInterfaceCulture` with default value `auto`.
- Update `LidGuardSettings.Normalize`, settings parsing, settings printing, interactive settings editing, and the source-generated settings JSON context.
- Update runtime settings propagation so `settings --ui-culture` reaches an already running runtime.
- Update MCP server startup and settings/status tool paths so MCP processes can refresh their effective UI culture.
- Apply culture before human CLI help/error output and option validation messages.
- Do not apply localization to protocol serialization paths for IPC, hook stdout, MCP JSON, settings JSON, or JSONL logs.
- Keep the helper AOT-safe: no dynamic culture discovery table is required for the first pass.
- Verify NativeAOT publish warnings after adding the setting.

## What To Localize

Localize human-facing CLI output:

- `help` output and command descriptions.
- `status`, `settings`, `remove-session`, `cleanup-orphans`, and diagnostics output labels and summaries.
- Session list lines, soft-lock summaries, lid/monitor/suspend status text, and other user-facing runtime/session presentation.
- Interactive prompts in settings and provider selection flows, plus interactive validation and follow-up guidance text.
- Hook, MCP, and provider-MCP install/status/remove sections and inspection summaries printed as normal CLI text.
- CLI-owned failure messages printed directly to the terminal.
- Human-facing CLI presentation derived from runtime results, inspection results, or other structured response fields, even when the raw backing field remains English in IPC, settings, or logs.
- Placeholder text such as `none` and `<none>`, and display labels for enum-like values such as lid state, suspend mode, soft-lock state, emergency hibernation temperature mode, and permission-request decisions.
- Transient runtime-decision messages shown only to the user during the current CLI invocation.

Keep these stable and not localized:

- CLI command names and option names.
- JSON property names and protocol values.
- Raw IPC request/response payload text at the serialization boundary, including runtime `Message` field values when they are transported or persisted as protocol data.
- Hook, MCP, and provider JSON `message`, `error`, and denial text.
- MCP tool names, argument names, and response property names.
- Hook event names and generated provider configuration keys.
- `settings.json` property names and stored enum values.
- Text written into generated provider configuration snippets or other files.
- Session identifiers, provider identifiers, process identifiers, paths, and timestamps.
- JSONL log field names and structured event names.
- Machine-readable stdout produced for provider hooks, including all text fields inside that protocol payload.

## First Migration Targets

Localize in this order so each step is small and testable:

1. Add resource files and `LidGuardText` wrapper in the `LidGuard` app.
2. Add the persisted UI culture setting with default `auto` and `lidguard settings --ui-culture <auto|en|ko|culture-name>`.
3. Apply the effective UI culture before CLI output is produced.
4. Localize [LidGuardCommandConsole.cs](LidGuard/Commands/LidGuardCommandConsole.cs) labels and unknown-command/help rendering.
5. Localize [LidGuardHelpContent.cs](LidGuard/Commands/LidGuardHelpContent.cs), keeping command syntax and option names literal.
6. Localize [LidGuardSettingsCommand.cs](LidGuard/Commands/LidGuardSettingsCommand.cs) prompts and settings update messages.
7. Localize [HookManagementCommand.cs](LidGuard/Commands/HookManagementCommand.cs), [McpManagementCommand.cs](LidGuard/Commands/McpManagementCommand.cs), and [ProviderMcpManagementCommand.cs](LidGuard/Commands/ProviderMcpManagementCommand.cs) only for human-facing CLI text and inspection presentation, not generated config content or protocol payloads.
8. Replace direct terminal printing of raw runtime or inspection `Message` strings with localized CLI presentation derived from structured fields or message codes while keeping raw protocol/log text stable where required.
9. Audit hook output DTOs and MCP tool responses to ensure protocol JSON text remains English.
10. Add `LidGuardLibText` and `CommonText` only when localizing human CLI presentation messages produced in those assemblies.

Avoid broad string churn in one change. Each migration step should leave the CLI behavior and exit codes unchanged.

## NativeAOT And Trimming Rules

Localization code must preserve LidGuard's NativeAOT/trimming posture.

Rules:

- Use explicit `ResourceManager` construction with `typeof(WrapperType).Assembly`.
- Avoid dynamic assembly loading, runtime type discovery, and reflection-driven resource lookup.
- Treat IL2026, IL3050, and related AOT/trim warnings as blockers.

## Packaging Checks

Localized publish output must be verified for every packaged RID:

- `win-x64`
- `win-x86`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

For each RID, verify that Korean resources are present and loadable. Depending on publish/package behavior, this may mean checking for a `ko` culture folder with a `*.resources.dll` file or validating that the published native executable can resolve `ko` resources at runtime.

Do not assume a NativeAOT publish will behave like a framework-dependent publish. Check the actual artifact layout before releasing.

## Testing Plan

Minimum validation:

- Unit-level checks for selected keys in English and Korean by temporarily setting `CultureInfo.CurrentUICulture`.
- CLI smoke checks for `help`, `status`, `settings`, `hook-status`, `mcp-status`, and provider MCP commands under English and Korean UI cultures.
- Hook smoke checks to confirm stdout JSON remains valid and English, including denial message text.
- MCP smoke checks to confirm tool names, JSON response property names, and protocol message values remain English.
- Publish artifact inspection for at least one Windows RID and one Unix RID before expanding to every RID.

Suggested test cases:

- Missing key returns the key name rather than throwing.
- Missing Korean translation falls back to English.
- Placeholder ordering works in Korean for messages with paths, provider names, and counts.
- `settings.json` stores `auto` by default and stores culture names as non-localized strings.
- Environment overrides do not rewrite `settings.json`.
- `settings --ui-culture` updates persisted settings and updates a running runtime.
- MCP server processes use the persisted or runtime-reported UI culture for human-facing presentation.
- IPC and hook/MCP JSON output remains unchanged except for fields that are explicitly not part of protocol payloads.
- Human-facing CLI output no longer leaks raw English runtime or inspection `Message` text when a localized rendering is available.
- Runtime-decision messages, session summaries, enum display values, placeholders, and management status text displayed by the CLI are localized even when the raw backing values remain stable in protocol or persisted data.
- JSONL logs remain structurally stable.

## Translation Policy

English is the neutral source language. Korean is the first localized language.

Translation rules:

- Preserve product and provider names: `LidGuard`, `Codex`, `Claude Code`, `GitHub Copilot CLI`, `MCP`.
- Preserve command and option tokens exactly: `settings`, `--provider`, `mcp-install`.
- Translate explanatory text naturally, not word-for-word.
- Use polite but concise Korean for CLI text.
- Keep error messages actionable: what failed, which value/path caused it, and what the user can do next when appropriate.
- Add translator comments for placeholders and protocol-sensitive strings.

## Documentation Maintenance

When `Localization.md` changes meaningfully, update `Localization.ko.md` in the same turn.

When localization changes public behavior, update `AGENTS.md` and `AGENTS.ko.md` as the source of truth. This includes:

- New user-visible settings such as a language override.
- New guarantees about localized output.
- Changes to hook, MCP, or settings protocol behavior.
- Packaging requirements that affect release artifacts.
