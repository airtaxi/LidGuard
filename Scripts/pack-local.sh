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

MAX_ATTEMPT_COUNT=2
CURRENT_ATTEMPT=1
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
PROJECT_FILE=$REPO_ROOT/LidGuard/LidGuard.csproj
PACKAGE_DIR=$REPO_ROOT/artifacts/packages
EXIT_CODE=0

read_project_version() {
    if [ ! -f "$PROJECT_FILE" ]; then
        echo "LidGuard project file was not found: $PROJECT_FILE" >&2
        return 1
    fi

    PROJECT_VERSION=$(sed -n 's/.*<PackageVersion>\(.*\)<\/PackageVersion>.*/\1/p' "$PROJECT_FILE" | sed -n '1p')
    if [ -z "$PROJECT_VERSION" ]; then
        PROJECT_VERSION=$(sed -n 's/.*<VersionPrefix>\(.*\)<\/VersionPrefix>.*/\1/p' "$PROJECT_FILE" | sed -n '1p')
    fi
    if [ -z "$PROJECT_VERSION" ]; then
        PROJECT_VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROJECT_FILE" | sed -n '1p')
    fi

    PROJECT_VERSION=$(printf '%s' "$PROJECT_VERSION" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
    if [ -z "$PROJECT_VERSION" ]; then
        echo "Could not find PackageVersion, VersionPrefix, or Version in the project file." >&2
        return 1
    fi

    printf '%s\n' "$PROJECT_VERSION"
}

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
    return 0
}

clean_build_output_directories() {
    echo "Removing build output directories..."
    for BUILD_OUTPUT_TARGET in "$REPO_ROOT/LidGuard/bin" "$REPO_ROOT/LidGuard/obj"; do
        case "$BUILD_OUTPUT_TARGET" in
            "$REPO_ROOT"/LidGuard/bin|"$REPO_ROOT"/LidGuard/obj)
                if [ -e "$BUILD_OUTPUT_TARGET" ]; then
                    rm -rf "$BUILD_OUTPUT_TARGET"
                fi
                ;;
            *)
                echo "Refusing to remove unexpected build output path: $BUILD_OUTPUT_TARGET" >&2
                return 1
                ;;
        esac
    done

    return 0
}

run_once() {
    if [ ! -f "$PROJECT_FILE" ]; then
        echo "LidGuard project file was not found: $PROJECT_FILE" >&2
        return 1
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "dotnet CLI was not found on PATH." >&2
        return 1
    fi

    if ! detect_target; then
        return 1
    fi

    echo "Detected target: $SYSTEM_NAME/$MACHINE_NAME (packing $TOOL_RID package)"
    echo "Using package version from project: $PACKAGE_VERSION"
    clean_build_output_directories || return 1

    echo "Removing stale $PACKAGE_VERSION package outputs..."
    rm -f \
        "$PACKAGE_DIR/lidguard.$PACKAGE_VERSION.nupkg" \
        "$PACKAGE_DIR/lidguard.$TOOL_RID.$PACKAGE_VERSION.nupkg" \
        "$REPO_ROOT/LidGuard/obj/Release/lidguard.$PACKAGE_VERSION.nuspec" \
        "$REPO_ROOT/LidGuard/obj/Release/lidguard.$TOOL_RID.$PACKAGE_VERSION.nuspec"

    echo "Packing lidguard $PACKAGE_VERSION..."
    dotnet pack "$PROJECT_FILE" -c Release || return 1

    echo "Packing lidguard.$TOOL_RID $PACKAGE_VERSION..."
    dotnet pack "$PROJECT_FILE" -c Release -r "$TOOL_RID" || return 1

    if [ ! -f "$PACKAGE_DIR/lidguard.$PACKAGE_VERSION.nupkg" ]; then
        echo "Expected package was not created: $PACKAGE_DIR/lidguard.$PACKAGE_VERSION.nupkg" >&2
        return 1
    fi

    if [ ! -f "$PACKAGE_DIR/lidguard.$TOOL_RID.$PACKAGE_VERSION.nupkg" ]; then
        echo "Expected package was not created: $PACKAGE_DIR/lidguard.$TOOL_RID.$PACKAGE_VERSION.nupkg" >&2
        return 1
    fi

    return 0
}

PACKAGE_VERSION=$(read_project_version) || exit 1
cd "$REPO_ROOT" || exit 1

while :; do
    echo "Packing local LidGuard packages. Attempt $CURRENT_ATTEMPT of $MAX_ATTEMPT_COUNT."
    if run_once; then
        EXIT_CODE=0
        break
    fi

    if [ "$CURRENT_ATTEMPT" -ge "$MAX_ATTEMPT_COUNT" ]; then
        EXIT_CODE=1
        break
    fi

    CURRENT_ATTEMPT=$((CURRENT_ATTEMPT + 1))
    echo
    echo "Previous pack attempt failed. Retrying attempt $CURRENT_ATTEMPT of $MAX_ATTEMPT_COUNT."
    echo
done

if [ "$EXIT_CODE" -eq 0 ]; then
    echo "Done."
else
    echo "Failed." >&2
fi

exit "$EXIT_CODE"
