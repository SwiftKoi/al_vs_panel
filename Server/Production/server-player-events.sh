#!/bin/sh
# Prints player and server events from the current and archived server logs as
# tab-separated lines, first field is the event type, second the UTC timestamp
# (d.m.yyyy h:mm:ss):
#   J  <time> <player> <address> <port>   player joined
#   L  <time> <player>                    player left normally
#   K  <time> <player> <reason>           player was removed (lost connection, crash, shutdown, ...)
#   F  <time> <address> <reason>          connection refused before the player joined
#   S  <time>                             server ticking suspended (autosave)
#   R  <time>                             server ticking resumed
#   O  <time> <milliseconds>              server overloaded: one tick took this long
# Read-only; the analytics importer deduplicates repeated runs.
set -eu

LC_ALL=C
export LC_ALL

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/Data/Logs"

for log in Archive/*/server-main.log server-main.log; do
    [ -r "$log" ] || continue
    awk '
        function address(endpoint) {
            sub(/:[0-9]+$/, "", endpoint)
            gsub(/[][]/, "", endpoint)
            sub(/^::ffff:/, "", endpoint)
            return endpoint
        }
        function rest(from,    i, s) {
            s = $from
            for (i = from + 1; i <= NF; i++) s = s " " $i
            return s
        }
        { time = $1 " " $2 }
        $3 == "[Notification]" && /A Client attempts connecting via TCP on / {
            id = $NF
            endpoint[id] = $(NF - 4)
            sub(/,$/, "", endpoint[id])
            next
        }
        $3 == "[Event]" && $NF == "joins." && NF == 6 {
            port = $5
            sub(/.*:/, "", port)
            printf "J\t%s\t%s\t%s\t%s\n", time, $4, address($5), port
            joined[$5] = 1
            next
        }
        $3 == "[Event]" && ($4 == "Игрок" || $4 == "Player") && ($6 == "вышел." || $6 == "left.") && NF == 6 {
            printf "L\t%s\t%s\n", time, $5
            next
        }
        $3 == "[Event]" && $4 == "Игрок" && $6 == "исключен." && $7 == "Причина:" {
            printf "K\t%s\t%s\t%s\n", time, $5, rest(8)
            next
        }
        $3 == "[Event]" && $4 == "Player" && $6 == "got" && $7 == "removed." && $8 == "Reason:" {
            printf "K\t%s\t%s\t%s\n", time, $5, rest(9)
            next
        }
        $3 == "[Notification]" && $4 == "Client" && $5 ~ /^[0-9]+$/ && $6 == "disconnected:" && NF > 6 {
            id = $5
            if ((id in endpoint) && !(endpoint[id] in joined)) {
                printf "F\t%s\t%s\t%s\n", time, address(endpoint[id]), rest(7)
            }
            next
        }
        $3 == "[Notification]" && /Server ticking has been suspended$/ { printf "S\t%s\n", time; next }
        $3 == "[Notification]" && /Server ticking has been resumed$/ { printf "R\t%s\n", time; next }
        $3 == "[Warning]" && $4 == "Server" && $5 == "overloaded." && $9 ~ /^[0-9]+ms$/ {
            ms = $9
            sub(/ms$/, "", ms)
            printf "O\t%s\t%s\n", time, ms
            next
        }
    ' "$log"
done
