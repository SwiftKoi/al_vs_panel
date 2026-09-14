#!/bin/sh
set -eu

console_pipe=/run/vintagestory/console

if [ "$#" -eq 0 ]; then
    echo "Usage: vintagestory-command COMMAND [ARGUMENT ...]" >&2
    exit 64
fi

if [ ! -p "$console_pipe" ]; then
    echo "Vintage Story command pipe is unavailable" >&2
    exit 1
fi

printf '%s\n' "$*" >"$console_pipe"
