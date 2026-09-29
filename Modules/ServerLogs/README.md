# ServerLogs module

Search and analysis of the **game server's own logs**: `server-main.log`, `server-audit.log`,
`server-debug.log` (and `server-chat.log` when enabled), including the game's `Logs/Archive/` folders.
The frontend page is `/server-logs` (`frontend/src/pages/ServerLogsPage.tsx`).
This is not the panel's own log. That one belongs to `Modules/Logging` and the `/logs` page.

## Features

| Feature | What it does |
|---|---|
| **Incremental indexing** | A background worker reads only the new bytes of each game log (and the `Archive/` folders) and stores them in a SQLite full-text index. Rotation is tracked by file identity, so nothing is read twice or lost. |
| **Full-text search** | One search box with `key:value` filters (log, level, player, action, item, source, near a location, …), free text, a time range and paging. Newest first. |
| **Autocomplete and saved searches** | Suggests filter keys and values from the actual data. Searches can be saved per user and server (up to 50) and reused. |
| **Facets and histogram** | Counts by log, level, source, player and action for the current query, and a histogram to zoom into a period. |
| **Context and live tail** | Show the lines around any entry across all logs. Follow new entries as they arrive. |
| **Export** | Download the current result as TXT or CSV (up to `MaximumExportRows`). |
| **Problems** | Warnings and errors grouped by signature with counts, a trend and a **NEW** badge for signatures first seen since the last start. A signature can be muted. The Overview's "Needs attention" list reuses this. |
| **Player activity** | From the audit log: per-player actions per day, items taken and put, kills, commands, deaths, joins and leaves, and busiest spots. |
| **Location lookup** | Who did what within a radius of x,(y),z, and which items were taken or put there. Useful for griefing and theft investigations. |
| **Startup timeline** | One row per server start with game version, mod count, time to ready, warnings and errors during startup, whether the previous run stopped cleanly or crashed, and which mods changed since the last start. |
| **Retention** | Each log kind has its own retention (`RetentionDays`). Older entries are skipped on ingest and pruned hourly. |

## Flow

```text
ServerLogsWorker (every IndexIntervalSeconds)
  -> ILogIndexer -> ILogSourceRepository -> IRemoteOperationsService -> Server/*/server-logs.py (read-only)
                 -> ILogIndexRepository  -> serverlogs.db (SQLite + FTS5)

HTTP request -> ServerLogs endpoint -> IServerLogService         -> ILogIndexRepository     (search, facets, problems)
                                    -> IServerLogInsightsService -> ILogInsightsRepository  (players, location, startups, saved searches)
```

Both repositories are implemented by `SqliteLogIndexRepository` (one database, one singleton).

## Page tabs

- **Search**: the search box highlights filters as you type and autocompletes them.
  Typing a key shows its values: logs, levels and actions from a fixed list; players, items and sources from
  `/suggest` for the current time range, narrowed by the rest of the box (`player:Orex_ killed:` lists what Orex_
  killed). When the box is empty it offers filters, saved searches, and the last
  8 searches (kept in the browser's `localStorage`). ↑↓ choose, Tab/Enter complete, Enter searches.
  Also on this tab:
  - a search box, facets and a histogram (drag across it to zoom);
  - context around a line, live tail, and TXT/CSV export;
  - saved searches (per user).
- **Problems**: warnings and errors grouped by signature, with a trend and a **NEW** badge (first seen since the last start), and mute.
- **Players**, built from the audit log:
  - the player list for the range;
  - per player: activity per day (including rejected positions), action counts, items taken and put (summed quantities),
    busiest spots (32-block cells), kills, commands, deaths, and joins and leaves;
  - **location lookup**: who did what within a radius of x,(y),z, plus the items taken and put there.
- **Startups**: one row per "Server logger started.":
  - game version, mod count, and seconds to "Entering runphase GameReady";
  - warnings and errors during startup (links to them);
  - the state: running, stopped (a "Stopped the server!" line was logged), or unclean (the next start came
    without a stop line — a crash or a kill);
  - mods added, updated or removed since the previous start, parsed from the "Loaded mods (N):" table.
- The Overview page's "Needs attention" list shows new, unmuted problem signatures since the last start (`/problems/summary`).

## Incremental indexing

- `server-logs.py list` returns every allowed log file with its size and an **identity**: the SHA-1
  of its first line. The first line holds a timestamp, so the identity stays the same when the game moves
  the file into `Archive/<date>/` at its daily rotation.
- `log_files` stores the byte offset reached for each identity. Each pass reads only new bytes
  (`read <path> <offset> <max>` returns complete lines only), so a repeat pass with no changes costs a
  directory listing. A rotated file keeps its offset, and any tail written before the rotation is read from
  its archive path.
- The first pass backfills all existing archives. A pass reads at most `MaximumBytesPerPass`; while
  more remains, the worker continues after 5 s instead of the normal interval.
- Lines without a timestamp (stack traces, the startup mod table) are joined to the entry above them
  (`extra`). If they arrive in a later pass, they are appended to the file's last stored entry.
- Timestamps are read as UTC, the same assumption the Analytics module makes.
- Entries older than `RetentionDays[kind]` are skipped on ingest and pruned hourly.

Measured on production (2026-09-28): 90 MB of logs (5 days) gave 568k entries in 33 s at full
speed, and a 200 MB database (about 2.2× the raw text). Searches take under 40 ms, and facet counts over 7 days
take under 1 s.

## Parsing

- **Header:** `d.M.yyyy HH:mm:ss[.fff] [Level] message`. A leading `[tag]` on main/debug lines is stored as
  `source` (the mod or subsystem).
- **Audit lines** are classified by `AuditParser` into
  `action` / `player` / `item` / `qty` / `x,y,z` (`qty` is the stack size of take/put/move/give lines; databases from before
  it existed get the column added and backfilled on startup):
  - Actions: take, put, move, give, command, kill, death, damage, place, break, teleport, join, leave, and so on.
  - Guild tags like `[RAC]` and trailing symbols like ` ♣` are stripped from player names.
  - `other` is the other party. The victim of a player kill ("A killed game:player" names no victim) is taken
    from A's last damage line within 5 s ("B … damage … by A").
  - Parser changes that add fields bump `SchemaVersion`; on startup the stored audit lines are re-parsed once.
  - Unknown shapes become `other`.
- **Noise:** `click` (inventory clicks) and `packet-rejected` ("too far away") make up most of the audit log.
  They are stored but hidden unless `noise=true` or an `action:` filter asks for them.
- **Signatures:** every Warning/Error/Fatal gets a signature. `MessageSignature` replaces positions,
  GUIDs/hex ids, numbers, and `for <name>:` player names, so repeats group together. Signatures record their
  first sighting, which lets the Problems tab mark ones that are **new since the last "Server logger started"**.
  Signatures can be muted per server.

## Search syntax (`LogQuery`)

| Syntax | Meaning |
|---|---|
| words / `"a phrase"` | Full-text (FTS5, prefix match, Cyrillic OK). Every term is quoted, so FTS operators can't be injected. |
| `log:` `level:` `source:` `player:` `action:` | Filters; the same key repeated is OR, different keys are AND, and `-key:value` excludes. |
| `item:ingot-iron` | Substring of the audit item code. |
| `command:land` | Commands run (`/land`, with or without the slash; prefix match). |
| `killed:drifter` / `killed:nPOCTAK` | Kills of a creature (item code substring) or of a player by name. |
| `killedby:Hikkalibur` | Deaths whose message names the killer (prefix), and player kills by that player. |
| `took:` `put:` `placed:` `broke:` `gave:` | That action on an item (substring). |
| `with:Name` | The other party of any action: attacker on damage, killer on death, victim of a player kill, receiver of a gift. |
| `near:x,y,z~r` or `near:x,z~r` | Audit coordinates within a box of radius `r` (default 32). |
| `sig:<id>` | Entries of one signature (used by the Problems tab). |

An unknown `key:value` (e.g. `game:firewood`) is treated as text.

## HTTP API

All endpoints live under `/api/servers/{serverId}/server-logs` and are open to **admins and moderators**
(`PanelPolicies.Staff`). Write routes (mute, saved searches) also need the anti-forgery token.

| Method | Path | Notes |
|---|---|---|
| GET | `/status` | Index progress, entry count, oldest/newest entry, database size, last error |
| GET | `/search?q=&from=&to=&noise=&cursor=&limit=` | Newest first, keyset cursor `ts_id`, page ≤ `MaximumPageSize` |
| GET | `/facets?…` | Counts by log, level, source, player, action (top 25 each) |
| GET | `/histogram?…&buckets=` | Entries per time bucket |
| GET | `/entries/{id}/context?before=&after=&noise=&logs=` | Neighbouring entries across logs, ≤ `MaximumContextLines` per side |
| GET | `/signatures?from=&to=` | Grouped warnings/errors with a 24-bucket trend and a "new" flag |
| PUT | `/signatures/{id}/mute` | `{ "muted": true }`, antiforgery required |
| GET | `/suggest?key=&prefix=&q=&from=&to=` | Autocomplete values for any filter key except `near`/`sig`, most frequent first (12), within entries matching `q`; codes also match after the `domain:` |
| GET | `/problems/summary` | New unmuted error/warning signatures since the last start (Overview page) |
| GET | `/players?from=&to=` | Audit actors with action, command, kill, death, rejected-position and join counts |
| GET | `/players/{player}/activity?from=&to=` | Per-player breakdown (see Page tabs) |
| GET | `/location?x=&y=&z=&radius=&from=&to=` | Who did what near a spot; radius 1–1000, default 32 |
| GET | `/boots?from=&to=` | Server starts, newest first (default 30 days) |
| GET/POST | `/saved-searches` | The caller's saved searches; POST `{ name, query, range }` (preset range), antiforgery, max 50 per user and server |
| DELETE | `/saved-searches/{id}` | Only the owner's; antiforgery |
| GET | `/export?…&format=txt\|csv` | Up to `MaximumExportRows`; CSV cells starting with `= + - @` are prefixed with `'` |

The default time range is the last 24 hours, and the maximum is 400 days.

## Configuration

```jsonc
"ServerLogs": {
  "DatabasePath": "/var/lib/alegacy/data/serverlogs.db",
  "IndexIntervalSeconds": 30,
  "ReadChunkBytes": 1048576,
  "MaximumBytesPerPass": 8388608,
  "RetentionDays": { "main": 180, "audit": 30, "debug": 14, "chat": 30 },
  "MaximumPageSize": 500, "MaximumContextLines": 200, "MaximumExportRows": 100000,
  "Servers": { "production-local": { "Operation": "production-local-logs-read", "IncludeChat": false } }
}
```

Each server needs a RemoteOperations command that runs `python3 Server/*/server-logs.py <Data dir>`.
Servers without an entry show "not configured".

## Security

- The helper is read-only and allowlists `server-{main,audit,debug,chat}.log` directly in `Logs/`, in `Logs/Archive/`,
  and one folder below it. It rejects `..`, symlinks, and anything that resolves outside `Logs/`.
- Chat logs are players' conversations, so they are indexed only with `IncludeChat: true`.
- User input reaches SQL only as parameters, and reaches FTS only as quoted terms. There are no server-side regexes built from user input.
- Saved searches belong to the signed-in user (the `ClaimTypes.Name` user id); other users can neither list nor delete them.
- Audit coordinates show where players build. The page is for panel staff only (admins and moderators).

## Tests

`Tests/Unit` covers:
- header, continuation, audit and signature parsing;
- query parsing and the FTS quoting;
- the indexer against an in-memory source (partial lines, continuation across passes, rotation by identity,
  retention);
- search filters, facets, context, paging, signatures with mute, and CSV export on a temporary SQLite file;
- player activity (quantities, places, days), location lookup, the startup timeline (states, mod diff),
  the problem summary, saved searches (per user, validation, limit), and the `qty` column migration.
