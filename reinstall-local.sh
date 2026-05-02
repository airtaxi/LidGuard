#!/usr/bin/env sh
set -u

NO_PAUSE=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --no-pause)
            NO_PAUSE=1
            shift
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

PACKAGE_VERSION=0.1.0
MAX_ATTEMPT_COUNT=2
CURRENT_ATTEMPT=1
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$SCRIPT_DIR
PACKAGE_DIR=$REPO_ROOT/artifacts/packages
TEMP_CONFIG_DIR=
EXIT_CODE=0

detect_target() {
    SYSTEM_NAME=$(uname -s 2>/dev/null || printf unknown)
    MACHINE_NAME=$(uname -m 2>/dev/null || printf unknown)

    case "$SYSTEM_NAME" in
        Linux)
            TOOL_OS=linux
            ;;
        Darwin)
            TOOL_OS=osx
            ;;
        *)
            echo "Unsupported operating system: $SYSTEM_NAME" >&2
            return 1
            ;;
    esac

    case "$MACHINE_NAME" in
        x86_64|amd64|AMD64)
            TOOL_ARCH=x64
            ;;
        arm64|aarch64|ARM64)
            TOOL_ARCH=arm64
            ;;
        *)
            echo "Unsupported processor architecture: $MACHINE_NAME" >&2
            return 1
            ;;
    esac

    TOOL_RID=$TOOL_OS-$TOOL_ARCH
    PACKAGE_FILE=$PACKAGE_DIR/lidguard.$PACKAGE_VERSION.nupkg
    RID_PACKAGE_FILE=$PACKAGE_DIR/lidguard.$TOOL_RID.$PACKAGE_VERSION.nupkg
    return 0
}

cleanup_temp_config() {
    if [ -n "$TEMP_CONFIG_DIR" ] && [ -d "$TEMP_CONFIG_DIR" ]; then
        rm -rf "$TEMP_CONFIG_DIR"
    fi

    TEMP_CONFIG_DIR=
}

stop_running_lidguard() {
    echo "Stopping running LidGuard processes..."
    if command -v pkill >/dev/null 2>&1; then
        pkill -x lidguard >/dev/null 2>&1 || true
        pkill -x LidGuard >/dev/null 2>&1 || true
        sleep 1

        if command -v pgrep >/dev/null 2>&1; then
            if pgrep -x lidguard >/dev/null 2>&1 || pgrep -x LidGuard >/dev/null 2>&1; then
                echo "Unable to stop all LidGuard processes." >&2
                return 1
            fi
        fi
    else
        echo "pkill was not found; skipping process stop."
    fi

    return 0
}

create_nuget_config() {
    TEMP_CONFIG_DIR=$(mktemp -d "${TMPDIR:-/tmp}/lidguard-local-source.XXXXXX") || return 1
    NUGET_CONFIG=$TEMP_CONFIG_DIR/NuGet.Config

    cat > "$NUGET_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="lidguard-local" value="$PACKAGE_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="lidguard-local">
      <package pattern="lidguard" />
      <package pattern="lidguard.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="System.*" />
      <package pattern="ModelContextProtocol" />
      <package pattern="WmiLight" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
}

remove_cache_path() {
    CACHE_TARGET=$1
    case "$CACHE_TARGET" in
        "$GLOBAL_PACKAGES_ROOT"/*)
            if [ -e "$CACHE_TARGET" ]; then
                rm -rf "$CACHE_TARGET"
            fi
            ;;
        *)
            echo "Refusing to remove unexpected cache path: $CACHE_TARGET" >&2
            return 1
            ;;
    esac

    return 0
}

clear_local_cache_entries() {
    GLOBAL_PACKAGES_LINE=$(dotnet nuget locals global-packages --list) || return 1
    GLOBAL_PACKAGES_ROOT=$(printf '%s\n' "$GLOBAL_PACKAGES_LINE" | sed 's/^global-packages:[[:space:]]*//')

    if [ -z "$GLOBAL_PACKAGES_ROOT" ] || [ ! -d "$GLOBAL_PACKAGES_ROOT" ]; then
        echo "Could not resolve the NuGet global packages directory." >&2
        return 1
    fi

    echo "Removing local NuGet cache entries for lidguard $PACKAGE_VERSION..."
    remove_cache_path "$GLOBAL_PACKAGES_ROOT/lidguard/$PACKAGE_VERSION" || return 1
    remove_cache_path "$GLOBAL_PACKAGES_ROOT/lidguard.$TOOL_RID/$PACKAGE_VERSION" || return 1
    return 0
}

uninstall_existing_tool() {
    if dotnet tool list --global | awk 'NR > 2 && $1 == "lidguard" { found = 1 } END { exit found ? 0 : 1 }'; then
        echo "Uninstalling existing lidguard global tool..."
        dotnet tool uninstall --global lidguard || return 1
    fi

    return 0
}

run_once() {
    cleanup_temp_config

    if ! detect_target; then
        return 1
    fi

    if [ ! -f "$PACKAGE_FILE" ]; then
        echo "Local package was not found: $PACKAGE_FILE" >&2
        echo "Run pack-local.sh first, then rerun reinstall-local.sh." >&2
        return 1
    fi

    if [ ! -f "$RID_PACKAGE_FILE" ]; then
        echo "Local package was not found: $RID_PACKAGE_FILE" >&2
        echo "Run pack-local.sh first, then rerun reinstall-local.sh." >&2
        return 1
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "dotnet CLI was not found on PATH." >&2
        return 1
    fi

    echo "Detected target: $SYSTEM_NAME/$MACHINE_NAME (installing --arch $TOOL_ARCH from $TOOL_RID package)"
    stop_running_lidguard || return 1

    echo "Creating temporary NuGet config..."
    create_nuget_config || return 1

    clear_local_cache_entries || return 1
    uninstall_existing_tool || return 1

    echo "Installing lidguard $PACKAGE_VERSION from local packages..."
    dotnet tool install --global lidguard --configfile "$NUGET_CONFIG" --version "$PACKAGE_VERSION" --arch "$TOOL_ARCH" || return 1

    echo "Verifying lidguard command..."
    if ! command -v lidguard >/dev/null 2>&1; then
        echo "lidguard command was not found on PATH after install." >&2
        return 1
    fi

    lidguard --help || return 1
    cleanup_temp_config
    return 0
}

cd "$REPO_ROOT" || exit 1
trap cleanup_temp_config EXIT HUP INT TERM

while :; do
    echo "Reinstalling local LidGuard tool. Attempt $CURRENT_ATTEMPT of $MAX_ATTEMPT_COUNT."
    if run_once; then
        EXIT_CODE=0
        break
    fi

    cleanup_temp_config
    if [ "$CURRENT_ATTEMPT" -ge "$MAX_ATTEMPT_COUNT" ]; then
        EXIT_CODE=1
        break
    fi

    CURRENT_ATTEMPT=$((CURRENT_ATTEMPT + 1))
    echo
    echo "Previous reinstall attempt failed. Retrying attempt $CURRENT_ATTEMPT of $MAX_ATTEMPT_COUNT."
    echo
done

if [ "$EXIT_CODE" -eq 0 ]; then
    echo "Done."
else
    echo "Failed." >&2
fi

exit "$EXIT_CODE"
