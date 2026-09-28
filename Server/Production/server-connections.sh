#!/bin/sh
# Lists established client TCP connections on the game server's listening
# ports, with kernel TCP statistics (RTT, jitter, retransmissions) and player
# names resolved from the server log. Prints one JSON object.
#
# The game server container is never exec'd into, restarted, or modified:
# a short-lived probe container shares only its network namespace and reads
# socket statistics through `ss`.
set -eu

LC_ALL=C
export LC_ALL

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

compose_service=vintagestory-server
server_log=Data/Logs/server-main.log
probe_dir=netprobe

container_id=$(docker compose ps --status running -q "$compose_service")
if [ -z "$container_id" ]; then
    echo "The server service is not running." >&2
    exit 1
fi

# Tag the probe image by its Dockerfile checksum so edits rebuild it once.
probe_image="alegacy-netprobe:$(cksum < "$probe_dir/Dockerfile" | cut -d ' ' -f 1)"
if ! docker image inspect "$probe_image" >/dev/null 2>&1; then
    docker build --quiet --tag "$probe_image" - < "$probe_dir/Dockerfile" >/dev/null
fi

# "E <host>:<port> <name>" for the latest join from each endpoint and
# "J <name> <count>" for how many times each player joined in the current log.
player_map=""
if [ -r "$server_log" ]; then
    player_map=$(tail -n 200000 "$server_log" | awk '
        $3 == "[Event]" && $NF == "joins." && NF == 6 {
            endpoint = $5
            gsub(/[][]/, "", endpoint)
            sub(/^::ffff:/, "", endpoint)
            name[endpoint] = $4
            joins[$4]++
        }
        END {
            for (e in name) print "E", e, name[e]
            for (n in joins) print "J", n, joins[n]
        }')
fi

docker run --rm --pull never --network "container:$container_id" \
    --read-only --cap-drop ALL --security-opt no-new-privileges \
    --memory 32m --log-driver none \
    --env "PLAYER_MAP=$player_map" \
    "$probe_image" sh -c '
{ ss -tlnH | sed "s/^/L /"; ss -tinH state established; } | awk '"'"'
function port(a) { sub(/.*:/, "", a); return a }
function host(a) { sub(/:[0-9]+$/, "", a); gsub(/[][]/, "", a); sub(/^::ffff:/, "", a); return a }
function str(s) { gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s); return "\"" s "\"" }
function val(k) { return (k in v) ? v[k] : 0 }
BEGIN {
    n = split(ENVIRON["PLAYER_MAP"], lines, "\n")
    for (i = 1; i <= n; i++) {
        split(lines[i], f, " ")
        if (f[1] == "E") player[f[2]] = f[3]
        else if (f[1] == "J") joins[f[2]] = f[3]
    }
    printf "{\"connections\":["
}
$1 == "L" { listening[port($5)] = 1; next }
/^[ \t]/ {
    if (!pending) next
    pending = 0
    delete v
    for (i = 1; i <= NF; i++) {
        if (split($i, kv, ":") != 2) continue
        if (kv[1] == "rtt") { split(kv[2], r, "/"); v["rtt"] = r[1]; v["rttvar"] = r[2] }
        else if (kv[1] == "retrans") { split(kv[2], r, "/"); v["retrans"] = r[2] }
        else v[kv[1]] = kv[2]
    }
    endpoint = peer_host ":" peer_port
    name = (endpoint in player) ? player[endpoint] : ""
    sent = val("bytes_sent") + 0
    retrans_pct = sent > 0 ? val("bytes_retrans") * 100 / sent : 0
    printf "%s{\"remoteAddress\":%s,\"remotePort\":%d,\"localPort\":%d,\"playerName\":%s,\"joinCount\":%d,", \
        (count++ ? "," : ""), str(peer_host), peer_port, local_port, (name == "" ? "null" : str(name)), \
        (name in joins) ? joins[name] : 0
    printf "\"rttMs\":%.1f,\"rttVarianceMs\":%.1f,\"minRttMs\":%.1f,\"retransmitPercent\":%.2f,\"retransmitsTotal\":%d,\"unackedSegments\":%d,", \
        val("rtt"), val("rttvar"), val("minrtt"), retrans_pct, val("retrans"), val("unacked")
    printf "\"receiveQueueBytes\":%d,\"sendQueueBytes\":%d,\"bytesSent\":%.0f,\"bytesReceived\":%.0f,\"lastReceiveMs\":%d,\"lastSendMs\":%d}", \
        recv_q, send_q, sent, val("bytes_received"), val("lastrcv"), val("lastsnd")
    next
}
{
    pending = 0
    local_port = port($3)
    if (!(local_port in listening)) next
    recv_q = $1; send_q = $2
    peer_host = host($4); peer_port = port($4)
    pending = 1
}
END { printf "]}\n" }
'"'"'
'
