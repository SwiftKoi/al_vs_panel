#!/bin/sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

compose_service=vintagestory-server
log_file=Data/Logs/server-main.log
shutdown_message='Сервер будет выключен через 45 секунд!'
stop_timeout=45
log_tail_lines=200

usage() {
    cat >&2 <<'EOF'
Usage:
  ./server-control.sh start
  ./server-control.sh stop
  ./server-control.sh restart
  ./server-control.sh status
  ./server-control.sh command -- COMMAND [ARGUMENT ...]
  ./server-control.sh logs
EOF
}

running_services() {
    docker compose ps --status running --services
}

service_is_running() {
    running_services | grep -F -x -q "$compose_service"
}

announce_shutdown() {
    if service_is_running; then
        if ! ./server-command.sh /announce "$shutdown_message"; then
            echo "Warning: could not announce the server shutdown." >&2
        fi
    fi
}

start_server() {
    if service_is_running; then
        printf '%s\n' online
        return 0
    fi

    docker compose start "$compose_service"
    status_server
}

stop_server() {
    announce_shutdown
    docker compose stop --timeout "$stop_timeout" "$compose_service"
    printf '%s\n' offline
}

restart_server() {
    announce_shutdown

    if service_is_running; then
        docker compose restart --timeout "$stop_timeout" "$compose_service"
    else
        docker compose start "$compose_service"
    fi

    status_server
}

status_server() {
    if ! service_is_running; then
        printf '%s\n' offline
    else
        printf '%s\n' online
    fi
}

command_name=${1:-}
case "$command_name" in
    start)
        [ "$#" -eq 1 ] || { usage; exit 64; }
        start_server
        ;;
    stop)
        [ "$#" -eq 1 ] || { usage; exit 64; }
        stop_server
        ;;
    restart)
        [ "$#" -eq 1 ] || { usage; exit 64; }
        restart_server
        ;;
    status)
        [ "$#" -eq 1 ] || { usage; exit 64; }
        status_server
        ;;
    command)
        [ "$#" -ge 3 ] && [ "$2" = "--" ] || { usage; exit 64; }
        shift 2
        exec ./server-command.sh "$@"
        ;;
    logs)
        [ "$#" -eq 1 ] || { usage; exit 64; }
        exec tail -n "$log_tail_lines" -F -- "$log_file"
        ;;
    *)
        usage
        exit 64
        ;;
esac
