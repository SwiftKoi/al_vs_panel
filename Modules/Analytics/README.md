# Analytics

Records player activity and connection quality over time so the panel can answer
"who is having connection problems, when, and why" and show general player
statistics. Live, point-in-time views (such as the player connections table) stay
in ServerManagement; this module owns everything that needs history.

## Data sources

All sources are read from outside the game server; nothing execs into, restarts,
or modifies the game server container.

| Source | How it is read | Stored as |
|---|---|---|
| Live client sockets | `IServerManagementService.GetConnectionsAsync` (the `ConnectionsOperation` probe) every `SampleIntervalSeconds` | `ConnectionSamples` — one row per connection per round; all rows of a round share one timestamp |
| Server metrics | `IServerManagementService.GetMetricsAsync` (the `MetricsOperation`) in the same round as connection sampling | `ServerMetricSamples` — CPU %, memory % of limit, memory bytes |
| Server log events | `Analytics:Servers:<id>:PlayerEventsOperation` every `EventImportIntervalMinutes`; for production, `Server/Production/server-player-events.sh` reads `Data/Logs/server-main.log` and `Data/Logs/Archive/*/server-main.log` and prints typed lines (join, normal leave, removal with reason, pre-join failure, tick suspend/resume, overloaded tick) | `PlayerJoins`, `SessionEnds`, `ConnectionFailures`, `ServerPauses`, `ServerOverloads` — each deduplicated on its natural key, so re-imports and archive backfill are safe |

Server-log timestamps are UTC. Statistics are bucketed into calendar days in
`Analytics:TimeZone` (IANA ID, default `UTC`).

## Configuration

```json
"Analytics": {
  "Enabled": true,
  "DatabasePath": "/var/lib/alegacy/data/analytics.db",
  "SampleIntervalSeconds": 60,
  "EventImportIntervalMinutes": 5,
  "SampleRetentionDays": 90,
  "EventRetentionDays": 400,
  "TimeZone": "UTC",
  "ProxyAddresses": ["10.77.0.1", "10.77.0.0/16"],
  "Servers": { "production-local": { "PlayerEventsOperation": "production-local-player-events" } }
}
```

`ProxyAddresses` lists single IPs or CIDR ranges used by a connection proxy.
Proxy classification happens at query time, so changing it applies to history
too. It is deployment-specific; set it through an environment override such as
`Analytics__ProxyAddresses__0`.

On startup the store creates any missing tables and indexes (`CREATE ... IF NOT
EXISTS` generated from the EF model). Schema changes must therefore be additive:
new tables or indexes. Changing an existing table's columns needs a migration path
so deployed data is not lost.

## API

`GET /api/analytics/{serverId}/players?days=30` (1–90 days, authenticated):
rolling 24h/7d/30d windows (unique, proxy, new players, joins) and per-day rows
(unique, proxy, new, joins, peak concurrent from connection samples).

`GET /api/analytics/{serverId}/disconnects?days=7` (1–90 days): sessions built
by pairing each join with the player's next leave/removal, classified as left,
lost connection, client crash, server shutdown, server error, kicked, unknown (no
end logged), or still open. A **drop** is a lost-connection or client-crash end; a
**quick rejoin** is any non-shutdown end followed by a rejoin within 5 minutes.
Each drop is correlated with autosave pauses starting up to 15 s before it, other
players' drops within 60 s, and the player's last connection sample within 3
minutes. The response also includes proxy vs direct drop rates, pre-join failure
reasons, per-day counts, and the players with the most drops and quick rejoins.

`GET /api/analytics/{serverId}/health?hours=24` (1–720 hours): per-minute CPU,
memory, players online (connection count), traffic to and from players (summed
per-connection byte growth per second), and the longest autosave pause per
interval, averaged into at most 720 points, plus summary figures and the ten
longest autosave pauses.

`GET /api/analytics/{serverId}/connection-quality?hours=24` (1–720 hours): median
and 95th-percentile RTT, average jitter, interval loss, and stall count (samples
where the player sent nothing for 5 s or more) for everyone, proxy and direct
players, and each player. Loss is computed per interval: the growth of
retransmitted bytes divided by the growth of bytes sent between consecutive
samples of the same TCP connection, so it reflects the period rather than the
connection's lifetime.

`GET /api/analytics/{serverId}/connection-quality/{playerName}?hours=24`: the
player's RTT, jitter, interval loss, and last-received time as a time series
(averaged into at most 720 points), plus their sessions in the range so drops and
rejoins can be overlaid.

Health also includes tick lag: the slowest overloaded tick per interval, overload
counts, and the ten worst ticks. The disconnect report adds, for each drop, the
slowest tick logged in the 60 s before it.

`GET /api/analytics/{serverId}/players/heatmap?days=28`: average and peak distinct
players online for each weekday × hour in `Analytics:TimeZone`.

`GET /api/analytics/{serverId}/players/list?days=30`: every player with sessions
in the range — playtime, sessions, average session, drops, quick rejoins, proxy
use, first and last seen.

`GET /api/analytics/{serverId}/players/list/{playerName}?days=30`: one player's
profile — totals, end reasons, connection quality, proxy share, and up to 200
recent sessions.

## Roadmap

Status: ✅ done · ⏳ next · ☐ planned

1. ✅ **History store and recorder** — SQLite store, background worker, connection
   sampling, log join import with archive backfill, retention pruning.
2. ✅ **Unique players per day/week/month** with new-vs-returning split.
3. ✅ **Proxy usage share** from configured proxy addresses.
4. ✅ **Peak concurrent players per day** (from connection samples; starts when sampling starts).
5. ✅ **Session timeline** — pair joins with disconnects (`Client N connected` → id → join,
   `Client N disconnected`, kick reason lines such as client crash) to get session
   length, and flag short sessions followed by a quick rejoin as drops. Drops per
   player per day.
6. ✅ **Disconnect correlation** — for each disconnect: that player's RTT/loss in the
   preceding minute, whether an autosave pause happened within seconds, and whether
   several players dropped together (server/proxy-side vs. client-side).
7. ✅ **Per-player connection history** — RTT, jitter, loss, and stall charts from samples, with drops and joins overlaid.
8. ✅ **Proxy vs direct comparison** — drop rate, RTT, jitter, loss, and stalls per group.
9. ✅ **Live stall detector** — the live connections view flags players silent for 5 s or more, or whose send queue grew on consecutive polls past 64 KB.
10. ✅ **Server health history** — CPU/memory from the metrics operation vs. player count.
11. ✅ **Autosave pause duration** — time between "ticking suspended/resumed" lines.
12. ✅ **Tick lag** — the server's own `Server overloaded. A tick took N ms` warnings
    (logged for ticks over 500 ms), charted on server health and linked to drops
    (slowest tick in the 60 s before each drop).
13. ✅ **Network throughput** — aggregate bytes per minute from samples.
14. ✅ **Activity heatmap** — average and peak distinct players online by weekday × hour.
15. ✅ **Playtime** — per-player totals, average session length, first and last seen.
16. ✅ **Player profile** — sessions, drops, end reasons, connection quality, proxy
    share, first/last seen, and the connection timeline.

All planned items are complete. Session lengths whose end was never logged are
capped (2 h for a missing end line, 24 h for a session still open) so a missing
line cannot inflate playtime.

Stored data includes player names and IP addresses; keep retention limits in place
and expose it only on authenticated routes.
