#!/usr/bin/env sh
set -u

NO_PAUSE=0
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
PACK_SCRIPT=$SCRIPT_DIR/pack-local.sh
REINSTALL_SCRIPT=$SCRIPT_DIR/reinstall-local.sh

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

cd "$REPO_ROOT" || exit 1

if [ ! -f "$PACK_SCRIPT" ]; then
    echo "Pack script was not found: $PACK_SCRIPT" >&2
    exit 1
fi

if [ ! -f "$REINSTALL_SCRIPT" ]; then
    echo "Reinstall script was not found: $REINSTALL_SCRIPT" >&2
    exit 1
fi

clean_build_output_directories || {
    echo "Failed." >&2
    exit 1
}

echo "Running local pack step..."
sh "$PACK_SCRIPT" --no-pause || {
    echo "Failed." >&2
    exit 1
}

echo
echo "Running local reinstall step..."
sh "$REINSTALL_SCRIPT" --no-pause || {
    echo "Failed." >&2
    exit 1
}

echo "Done."
