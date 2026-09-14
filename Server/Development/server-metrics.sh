#!/bin/sh
set -eu

LC_ALL=C
export LC_ALL

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

compose_service=vintagestory-server
data_path=Data

container_id=$(docker compose ps --status running -q "$compose_service")
if [ -z "$container_id" ]; then
    echo "The server service is not running." >&2
    exit 1
fi

stats=$(docker stats --no-stream \
    --format '{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}|{{.BlockIO}}' \
    "$container_id")

cpu_percent_raw=${stats%%|*}
remaining=${stats#*|}
memory_usage_pair=${remaining%%|*}
remaining=${remaining#*|}
memory_percent_raw=${remaining%%|*}
block_io_pair=${remaining#*|}

cpu_percent=$(printf '%s\n' "$cpu_percent_raw" | sed 's/%$//')
memory_usage=$(printf '%s\n' "$memory_usage_pair" | awk -F ' / ' '{ print $1 }')
memory_limit=$(printf '%s\n' "$memory_usage_pair" | awk -F ' / ' '{ print $2 }')
memory_percent=$(printf '%s\n' "$memory_percent_raw" | sed 's/%$//')
block_read=$(printf '%s\n' "$block_io_pair" | awk -F ' / ' '{ print $1 }')
block_write=$(printf '%s\n' "$block_io_pair" | awk -F ' / ' '{ print $2 }')

disk_stats=$(df -P -B1 "$data_path" | awk 'NR == 2 { print $2 "|" $3 "|" $4 "|" $5 }')
disk_total_bytes=${disk_stats%%|*}
remaining=${disk_stats#*|}
remaining=${remaining#*|}
disk_available_bytes=${remaining%%|*}
disk_used_bytes=$(du -s -B1 "$data_path" | awk 'NR == 1 { print $1 }')
disk_total_bytes=$((disk_used_bytes + disk_available_bytes))
disk_percent=$((disk_used_bytes * 100 / disk_total_bytes))

printf '{"cpuPercent":%s,"memoryUsage":"%s","memoryLimit":"%s","memoryPercent":%s,"blockRead":"%s","blockWrite":"%s","diskUsedBytes":%s,"diskTotalBytes":%s,"diskAvailableBytes":%s,"diskPercent":%s}\n' \
    "$cpu_percent" \
    "$memory_usage" \
    "$memory_limit" \
    "$memory_percent" \
    "$block_read" \
    "$block_write" \
    "$disk_used_bytes" \
    "$disk_total_bytes" \
    "$disk_available_bytes" \
    "$disk_percent"
