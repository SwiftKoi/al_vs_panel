#!/bin/sh
set -eu

if [ "$#" -eq 0 ]; then
    echo "Usage: $0 COMMAND [ARGUMENT ...]" >&2
    exit 64
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

exec docker compose exec -T vintagestory-server vintagestory-command "$@"
