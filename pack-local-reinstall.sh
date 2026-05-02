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

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PACK_SCRIPT=$SCRIPT_DIR/pack-local.sh
REINSTALL_SCRIPT=$SCRIPT_DIR/reinstall-local.sh

cd "$SCRIPT_DIR" || exit 1

if [ ! -f "$PACK_SCRIPT" ]; then
    echo "Pack script was not found: $PACK_SCRIPT" >&2
    exit 1
fi

if [ ! -f "$REINSTALL_SCRIPT" ]; then
    echo "Reinstall script was not found: $REINSTALL_SCRIPT" >&2
    exit 1
fi

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
