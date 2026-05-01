# LidGuard 현지화 전략

이 문서는 LidGuard 현지화를 바로 구현할 수 있도록 정리한 전략 문서다. 제품 방향과 런타임 동작의 기준 문서는 아니다. 제품 방향과 런타임 동작의 단일 기준은 `AGENTS.md`와 `AGENTS.ko.md`이며, 현지화 작업이 핵심 동작이나 공개 보장을 바꾸는 경우 같은 변경에서 두 파일을 함께 갱신해야 한다.

## 결정

표준 `.resx` 리소스와 `System.Resources.ResourceManager`를 사용하고, 작은 내부 래퍼 클래스를 통해 접근한다.

이유:

- `.resx`, `ResourceManager`, satellite assembly는 .NET의 표준 현지화 방식이며 Windows, Linux, macOS에서 동작한다.
- LidGuard는 NativeAOT 및 trimming에 민감한 CLI다. 직접 `ResourceManager` 래퍼를 쓰면 호출 경로가 명시적이고, 정적 명령 코드에 framework 관례를 퍼뜨리지 않아도 된다.
- 현재 사용자 표시 문자열 대부분은 정적 CLI 명령, hook, MCP, runtime result 코드에 직접 들어 있다. 작은 래퍼는 이 표면에 단계적으로 적용하기 쉽다.
- 이 래퍼 방식은 저장소의 현재 명령 구조와 가깝게 유지되면서도 표준 .NET resource fallback model을 사용한다.

## 리소스 배치

사용자 표시 문자열을 소유한 assembly마다 하나의 리소스 래퍼를 둔다. 하위 라이브러리가 `LidGuard` 앱 assembly의 문자열에 의존하게 만들지 않는다.

첫 구현에 권장하는 배치:

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

첫 작업이 CLI/help/status 출력만 현지화한다면 `LidGuard`부터 시작한다. `LidGuardLib` 또는 `LidGuardLib.Commons` 내부에서 생성되는 메시지를 그 위치에서 현지화해야 할 때 해당 assembly의 리소스를 추가한다.

첫 단계에서는 생성된 `.Designer.cs` 파일을 피한다. Visual Studio 전용 리소스 디자이너 동작에 빌드가 의존하지 않도록 수동 래퍼를 유지한다.

리소스를 포함하는 각 assembly에는 neutral resource language attribute를 추가한다:

```csharp
[assembly: NeutralResourcesLanguage("en")]
```

neutral 영어 문자열은 main assembly에 둔다. 한국어 문자열은 `*.ko.resx` satellite resource에 둔다.

## 래퍼 형태

안정적인 base name으로 명시적 `ResourceManager` 인스턴스를 만든다. 리소스 assembly를 동적으로 탐색하지 않는다.

예시 형태:

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

래퍼 멤버 이름은 축약하지 말고 의미가 드러나게 작성한다. 저장소의 C# 스타일을 따른다. private static field는 `s_camelCase`, 가능한 단일 줄 멤버는 expression-bodied 형태를 사용한다.

## 키 명명

키는 영어 문장이 아니라 메시지의 용도를 나타내야 한다.

권장 패턴:

- `Help_Usage_Title`
- `Help_Settings_Description`
- `Settings_FileLabel`
- `Settings_RuntimeUpdated`
- `Command_UnknownCommand`
- `Hook_Install_AlreadyInstalled`
- `Mcp_Status_ServerNameLabel`
- `Runtime_ProcessIdentifierRequired`

지침:

- 번역 대상 문자열은 가능한 완전한 문장으로 둔다.
- 현지화된 문장을 조각 문자열 연결로 만들지 않는다.
- numbered placeholder(`{0}`, `{1}`)를 사용하고, 값의 의미가 명확하지 않으면 `.resx`에 translator comment를 남긴다.
- 명령 이름, 옵션 이름, provider 이름, 파일 이름, enum 값, protocol token은 설명 문장 안의 일반 텍스트가 아닌 한 번역하지 않는다.
- 복수형 처리가 필요해지면 단수/복수 키를 분리하거나 복수형 문법에 민감하지 않은 문장으로 바꾼다. 첫 단계에서 pluralization 패키지를 추가하지 않는다.

## 문화권 선택

초기 동작은 .NET 기본값을 따른다.

- 리소스 조회는 `CultureInfo.CurrentUICulture`를 사용한다.
- 사용자 표시 형식화는 `CultureInfo.CurrentCulture`를 사용한다.
- protocol, JSON, log, ISO timestamp, machine-readable output은 invariant 또는 기존의 안정적인 형식을 유지한다.
- Agent 또는 provider client가 읽는 IPC 및 JSON payload text는 `message`, error, denial text를 포함해 영어로 유지한다.

`InvariantGlobalization`은 켜지 않는다. Linux와 macOS에서도 현지화에는 culture data가 필요하다. 텍스트 현지화만 하는 현재 단계에서는 app-local ICU가 필요하지 않다. 날짜, 숫자, 정렬 동작까지 모든 플랫폼에서 동일하게 맞춰야 하는 요구가 생기면 다시 검토한다.

Override 지원:

- 테스트와 지원을 위해 process-level override인 `LIDGUARD_UI_CULTURE`를 추가한다.
- 어떤 텍스트도 출력하기 전 CLI 시작 지점 근처에서 한 번만 파싱한다.
- 잘못된 culture 값은 명확한 영어 fallback 오류를 출력하거나 경고 후 무시한다.

## 언어 변경 옵션 제공 시 고려사항

사용자에게 보이는 언어 변경 옵션은 공개 동작 변경이다. 이 계획을 구현할 때는 같은 변경에서 `AGENTS.md`, `AGENTS.ko.md`, help text, settings 문서, packaging check를 함께 갱신한다.

권장 정책:

- 기본값이 `auto`인 persisted `settings` UI language option을 추가한다.
- `lidguard settings --ui-culture <auto|en|ko|culture-name>` 및 interactive settings flow를 통해 노출한다.
- `auto`는 "process/OS 기본 UI culture 사용"을 의미한다.
- 사용자가 구체적인 언어를 선택하지 않은 경우 `settings.json`에는 `auto`를 유지한다.
- 테스트, 지원, script 용도로 `LIDGUARD_UI_CULTURE` 환경 변수 override를 추가한다.
- Language setting은 사람이 읽는 CLI presentation에만 영향을 준다. IPC, hook JSON, MCP JSON, settings JSON schema, persisted log text, 생성되는 configuration file content는 바꾸지 않는다.

우선순위는 명확하고 안정적이어야 한다.

1. 설정된 경우 `LIDGUARD_UI_CULTURE`.
2. `auto`가 아닌 persisted LidGuard `settings` UI culture value.
3. Process 또는 OS의 `CultureInfo.CurrentUICulture`.

검증 규칙:

- `en`, `en-US`, `ko`, `ko-KR`처럼 `CultureInfo.GetCultureInfo`가 해석할 수 있는 BCP 47 culture name을 허용한다.
- 지역별 값의 fallback은 .NET resource fallback에 맡긴다. 예를 들어 `ko-KR`은 `ko`로 fallback될 수 있다.
- Culture name은 string으로 저장한다. `CultureInfo` object를 저장하지 않는다.
- 저장 값은 현지화하지 않는다. 번역된 label이 아니라 `auto`, `en`, `ko` 또는 정확한 culture name을 사용한다.
- 잘못된 값이 settings를 망가뜨리면 안 된다. 잘못된 값을 보고하고 이전 effective culture를 유지한다.

범위 규칙:

- 언어 옵션은 초기에는 `CultureInfo.CurrentUICulture`에만 영향을 줘야 한다.
- 명시적으로 formatting-culture option을 제공하는 것이 아니라면 `CultureInfo.CurrentCulture`는 바꾸지 않는다. `CurrentCulture`를 바꾸면 날짜, 숫자, 통화 형식도 바뀌므로 사용자와 테스트에 예상치 못한 영향을 줄 수 있다.
- Machine-readable output은 언어와 관계없이 안정적으로 유지한다.
- IPC 및 JSON `message` field는 agent, provider client, script가 읽을 수 있으므로 영어로 유지한다.
- Command name, option name, JSON property name, MCP tool name, hook event name, stored settings key는 언어에 따라 절대 바뀌면 안 된다.
- 최종적으로 사람이 읽는 CLI presentation은, 그 기반 데이터가 protocol-facing 또는 persisted English field에서 왔더라도 현지화해야 한다.
- 여기에는 runtime 또는 inspection result message의 표시 문장, session list summary, hook/MCP/provider management status text, interactive prompt, `none`이나 `<none>` 같은 placeholder, enum 성격 값의 display label이 포함된다.
- IPC, log, settings, generated file 때문에 raw value를 안정적으로 유지해야 하는 경우에는 저장값이나 직렬화값이 아니라 presentation layer를 현지화한다.

Runtime 및 IPC 고려사항:

- Detached `run-server` process의 culture가 이후 실행되는 CLI invocation에서 요청한 culture와 같다고 가정하지 않는다.
- `settings` command는 다른 runtime settings update와 마찬가지로 UI culture setting을 저장하고, 실행 중인 runtime에 갱신된 설정을 전달해야 한다.
- Runtime은 최신 UI culture setting을 메모리에 기억하고 settings/status snapshot에 포함해 helper process가 현재 설정을 관찰할 수 있게 해야 한다.
- MCP server와 Provider MCP server process는 startup 시 persisted settings를 읽어 effective UI culture를 적용하고, runtime/settings response에서 더 최신 값이 보고되면 local effective culture를 갱신해야 한다.
- LidGuard가 child 또는 detached LidGuard process를 시작할 때는 effective UI culture를 `LIDGUARD_UI_CULTURE`로 전달해 child process가 오래된 OS culture에 의존하지 않게 한다.
- `LidGuardPipeRequest`와 `LidGuardPipeResponse`의 raw protocol payload text는 안정적으로 유지한다. Protocol 또는 log 호환성 때문에 runtime response의 `Message` 값이 영어로 남아 있어도, CLI는 현지화된 human-facing rendering을 만들 수 있을 때 그 raw 값을 그대로 사용자에게 노출하면 안 된다.
- 저장값이나 protocol-facing text를 바꾸지 않고도 CLI가 runtime/session management 문장, enum display text, placeholder, inspection summary를 현지화할 수 있도록 message code + argument 또는 structured response field를 우선 사용한다.
- Runtime state는 UI culture setting을 기억하고 전파할 수 있지만, IPC message 자체는 영어로 유지한다.
- Log message text는 v1에서 현지화하지 않는다. JSONL log field name, structured event name, persisted log text는 안정적으로 유지한다.

Hook 및 MCP 고려사항:

- Provider hook stdout은 모든 언어에서 유효한 JSON이어야 하며 schema가 유지되어야 한다.
- MCP tool name, argument name, protocol schema로 쓰이는 description, response property name은 안정적으로 유지한다.
- Hook, MCP, provider JSON의 `message`, `error`, denial text는 영어로 유지한다. 이 payload는 end user보다 agent, provider client, automation이 읽는 값이다.
- MCP process culture는 startup 시 persisted settings에서 동기화하고, 실행 중에는 runtime/settings response를 통해 동기화한다. v1에서는 protocol-visible language parameter를 추가하지 않는다.

구현 메모:

- Command startup 근처에 작은 `LidGuardCulture` 또는 `LidGuardCultureOptions` helper를 추가한다.
- 기본값이 `auto`인 `UiCulture` 또는 `UserInterfaceCulture` 같은 settings field를 추가한다.
- `LidGuardSettings.Normalize`, settings parsing, settings printing, interactive settings editing, source-generated settings JSON context를 갱신한다.
- `settings --ui-culture`가 이미 실행 중인 runtime에 전달되도록 runtime settings propagation을 갱신한다.
- MCP process가 effective UI culture를 갱신할 수 있도록 MCP server startup과 settings/status tool path를 갱신한다.
- 사람이 읽는 CLI help/error output과 option validation message 전에 culture를 적용한다.
- IPC, hook stdout, MCP JSON, settings JSON, JSONL log의 protocol serialization path에는 현지화를 적용하지 않는다.
- Helper는 AOT-safe해야 한다. 첫 단계에서는 동적 culture discovery table이 필요하지 않다.
- 설정 추가 후 NativeAOT publish warning을 검증한다.

## 현지화할 대상

사람이 읽는 CLI 출력은 현지화한다.

- `help` 출력과 명령 설명.
- `status`, `settings`, `remove-session`, `cleanup-orphans`, diagnostics 출력의 label과 summary.
- Session list line, soft-lock summary, lid/monitor/suspend status text 같은 user-facing runtime/session presentation.
- settings 및 provider 선택 흐름의 interactive prompt와 interactive validation 및 후속 안내 text.
- 일반 CLI text로 출력되는 hook, MCP, provider-MCP install/status/remove 섹션과 inspection summary.
- CLI가 직접 terminal에 출력하는 failure message.
- IPC, settings, log에 raw backing field가 영어로 남아 있어도, runtime result, inspection result, 기타 structured response field에서 파생되는 human-facing CLI presentation text.
- `none`, `<none>` 같은 placeholder와 lid state, suspend mode, soft-lock state, emergency hibernation temperature mode, permission-request decision 같은 enum 성격 값의 display label.
- 현재 CLI invocation 중 사용자에게 보이는 transient runtime-decision message.

아래 항목은 안정적으로 유지하고 현지화하지 않는다.

- CLI command name과 option name.
- JSON property name과 protocol value.
- 직렬화 경계에서의 raw IPC request/response payload text. Protocol data로 운반되거나 저장되는 runtime `Message` field 값도 포함한다.
- Hook, MCP, provider JSON의 `message`, `error`, denial text.
- MCP tool name, argument name, response property name.
- Hook event name과 생성되는 provider configuration key.
- `settings.json` property name과 저장되는 enum value.
- 생성되는 provider configuration snippet 또는 다른 파일에 쓰이는 text.
- Session identifier, provider identifier, process identifier, path, timestamp.
- JSONL log field name과 structured event name.
- Provider hook용 machine-readable stdout. 해당 protocol payload 안의 모든 text field를 포함한다.

## 첫 마이그레이션 대상

각 단계가 작고 검증 가능하도록 다음 순서로 현지화한다.

1. `LidGuard` 앱에 resource file과 `LidGuardText` 래퍼를 추가한다.
2. 기본값 `auto`의 persisted UI culture setting과 `lidguard settings --ui-culture <auto|en|ko|culture-name>`를 추가한다.
3. CLI output이 생성되기 전에 effective UI culture를 적용한다.
4. [LidGuardCommandConsole.cs](LidGuard/Commands/LidGuardCommandConsole.cs)의 label, unknown-command, help rendering을 현지화한다.
5. [LidGuardHelpContent.cs](LidGuard/Commands/LidGuardHelpContent.cs)를 현지화하되 command syntax와 option name은 literal로 유지한다.
6. [LidGuardSettingsCommand.cs](LidGuard/Commands/LidGuardSettingsCommand.cs)의 prompt와 settings update message를 현지화한다.
7. [HookManagementCommand.cs](LidGuard/Commands/HookManagementCommand.cs), [McpManagementCommand.cs](LidGuard/Commands/McpManagementCommand.cs), [ProviderMcpManagementCommand.cs](LidGuard/Commands/ProviderMcpManagementCommand.cs)는 generated config content나 protocol payload가 아니라 human-facing CLI text와 inspection presentation만 현지화한다.
8. Raw runtime 또는 inspection `Message` string을 terminal에 직접 출력하지 않도록 바꾸고, 필요한 경우 structured field 또는 message code에서 localized CLI presentation을 만들되 raw protocol/log text는 필요한 범위에서 안정적으로 유지한다.
9. Hook output DTO와 MCP tool response를 점검해 protocol JSON text가 영어로 유지되는지 확인한다.
10. 해당 assembly 안에서 생성되는 human CLI presentation message를 현지화해야 할 때만 `LidGuardLibText`와 `CommonText`를 추가한다.

한 변경에서 광범위한 문자열 이동을 하지 않는다. 각 단계는 CLI 동작과 exit code를 바꾸지 않아야 한다.

## NativeAOT 및 Trimming 규칙

현지화 코드는 LidGuard의 NativeAOT/trimming 기조를 유지해야 한다.

규칙:

- `typeof(WrapperType).Assembly`를 사용해 명시적으로 `ResourceManager`를 만든다.
- dynamic assembly loading, runtime type discovery, reflection-driven resource lookup을 피한다.
- IL2026, IL3050 및 관련 AOT/trim warning은 차단 이슈로 취급한다.

## 패키징 점검

현지화된 publish output은 모든 패키징 RID에서 검증해야 한다.

- `win-x64`
- `win-x86`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

각 RID에서 한국어 리소스가 존재하고 로드되는지 확인한다. publish/package 동작에 따라 `ko` culture folder와 `*.resources.dll` 존재 여부를 확인하거나, published native executable이 런타임에 `ko` 리소스를 해석할 수 있는지 확인해야 한다.

NativeAOT publish가 framework-dependent publish와 동일하게 동작한다고 가정하지 않는다. 릴리스 전에 실제 artifact layout을 확인한다.

## 테스트 계획

최소 검증:

- `CultureInfo.CurrentUICulture`를 임시로 바꿔 English/Korean 주요 key를 확인하는 unit-level check.
- English/Korean UI culture에서 `help`, `status`, `settings`, `hook-status`, `mcp-status`, provider MCP command smoke check.
- stdout JSON이 계속 유효하고 denial message text를 포함해 영어로 유지되는지 확인하는 hook smoke check.
- Tool name, JSON response property name, protocol message value가 영어로 유지되는지 확인하는 MCP smoke check.
- 모든 RID로 확장하기 전에 Windows RID 하나와 Unix RID 하나 이상에서 publish artifact inspection.

권장 테스트 케이스:

- 누락 key는 예외가 아니라 key name으로 fallback한다.
- 누락된 한국어 번역은 영어로 fallback한다.
- Path, provider name, count가 들어가는 메시지에서 한국어 placeholder 순서가 올바르다.
- `settings.json`은 기본값으로 `auto`를 저장하고 culture name은 현지화하지 않은 string으로 저장한다.
- 환경 변수 override는 `settings.json`을 다시 쓰지 않는다.
- `settings --ui-culture`는 persisted settings를 갱신하고 실행 중인 runtime도 갱신한다.
- MCP server process는 persisted 또는 runtime-reported UI culture를 사용해 human-facing presentation에 적용한다.
- IPC 및 hook/MCP JSON output은 protocol payload 밖의 명시적 비프로토콜 field를 제외하고 바뀌지 않는다.
- Localized rendering이 가능할 때 human-facing CLI output에 raw English runtime 또는 inspection `Message` text가 그대로 새어 나오지 않는다.
- CLI가 표시하는 runtime-decision message, session summary, enum display value, placeholder, management status text는 raw backing value가 protocol이나 persisted data에 안정적으로 남아 있어도 현지화된다.
- JSONL log 구조는 안정적으로 유지된다.

## 번역 정책

영어가 neutral source language다. 한국어는 첫 localized language다.

번역 규칙:

- 제품 및 provider 이름은 유지한다: `LidGuard`, `Codex`, `Claude Code`, `GitHub Copilot CLI`, `MCP`.
- Command와 option token은 그대로 유지한다: `settings`, `--provider`, `mcp-install`.
- 설명 문장은 직역보다 자연스러운 한국어로 번역한다.
- CLI 텍스트는 공손하지만 간결한 한국어를 사용한다.
- 오류 메시지는 실행 가능해야 한다. 무엇이 실패했는지, 어떤 값/path가 원인인지, 적절하다면 사용자가 다음에 무엇을 할 수 있는지 포함한다.
- Placeholder와 protocol-sensitive string에는 translator comment를 남긴다.

## 문서 유지 관리

`Localization.md`가 의미 있게 바뀌면 같은 변경에서 `Localization.ko.md`를 함께 갱신한다.

현지화가 공개 동작을 바꾸면 기준 문서인 `AGENTS.md`와 `AGENTS.ko.md`도 갱신한다. 여기에 포함되는 예:

- Language override 같은 새 사용자 표시 설정.
- Localized output에 대한 새 보장.
- Hook, MCP, settings protocol 동작 변경.
- Release artifact에 영향을 주는 packaging 요구사항.
