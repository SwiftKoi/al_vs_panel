#!/bin/sh
set -eu

console_pipe=/run/vintagestory/console

rm -f "$console_pipe"
mkfifo "$console_pipe"
chmod 0660 "$console_pipe"

if [ ! -w /data ]; then
  echo "Error: /data is not writable by $(id -u):$(id -g). Configure SERVER_UID and SERVER_GID to match its host owner." >&2
  exit 1
fi

if [ ! -f /server/INSTALLED ]; then
  /server/install.sh
fi

# Keep both ends open so server stdin remains usable between command writers.
exec 3<> "$console_pipe"
exec dotnet /server/VintagestoryServer.dll --dataPath /data "$@" <&3
