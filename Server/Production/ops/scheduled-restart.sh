#!/bin/sh
set -u

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
project_dir=$(CDPATH= cd -- "$script_dir/.." && pwd)
cd "$project_dir" || exit 1

restart_at=$(date -d 'tomorrow 00:00:00' +%s) || exit 1

sleep_until() {
    target=$1
    now=$(date +%s)
    delay=$((target - now))
    if [ "$delay" -gt 0 ]; then
        sleep "$delay"
    fi
}

announce() {
    message=$1
    if ! ./server-command.sh /announce "$message"; then
        echo "Warning: could not send announcement: $message" >&2
    fi
}

announce "Перезапуск сервера через 10 минут"
sleep_until $((restart_at - 300))

announce "Перезапуск сервера через 5 минут"
sleep_until $((restart_at - 60))

announce "Перезапуск сервера через 1 минуту"
sleep_until "$restart_at"

exec docker compose restart vintagestory-server
