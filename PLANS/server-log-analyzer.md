# Server Log Analyzer — implementation plan

Status (2026-09-28): **all four phases implemented**:
- Search: facets, histogram, context, export, live tail, saved searches.
- Problems: signatures, NEW badge, mute, and the Overview hook.
- Players: per-player breakdown and location lookup.
- Startups: version, startup time, clean or unclean stop, and mod changes.

Decisions made along the way:
- Retention: main 180 days, audit **30** (per your request), debug 14.
- Chat is off; mod-specific logs are ignored.
- Timestamps are read as UTC.
- Saved searches are per user.

Not done (optional later):
- chat or mod logs;
- notifications to Discord;
- showing coordinates relative to spawn.

See `Modules/ServerLogs/README.md`.

A new panel page (`/server-logs`) to search and understand the Vintage Story server's own logs
(`Data/Logs/server-main.log`, `server-audit.log`, `server-debug.log`, plus their archives).
This is not the existing `/logs` page, which shows the panel's own log (`Modules/Logging`).

---

## 1. What the logs look like (measured on production, 2026-09-28)

### Files and rotation

```
Data/Logs/
  server-main.log      1.1 MB   11.6k lines  (today so far)
  server-audit.log     7.3 MB   39k lines
  server-debug.log     1.9 MB   14.4k lines
  server-chat.log      0.3 MB
  server-build.log, server-worldgen.log, alegacy-anticheat.log, arcanumlib-diagnostics.log
  alquest/             mod-specific logs (directory)
  Archive/
    2026-09-26_22_00_07/   server-main.log, server-audit.log (13.7 MB), server-debug.log, ...
    ... five dated folders (the game keeps ~5 days)
    server-main.log, server-debug.log   (loose files from 2026-08-08, an older layout)
```

- **The game rotates once a day.** Around 22:00 in the log's clock (00:00 on the host), it moves the current files into
  `Archive/<yyyy-MM-dd_HH_mm_ss>/` and starts new ones. **It keeps only about 5 archive folders**, so
  anything older is gone. The analyzer's own index is the only way to keep a longer history.
- Volume: about 15–25 MB of text per day, and 70–90% of it is the audit log (roughly 75k audit lines on a busy
  day). The whole `Logs` folder is 176 MB today.

### Line formats

| File | Format | Levels seen (count today) |
|---|---|---|
| main | `d.M.yyyy HH:mm:ss [Level] message` | Notification 5312, Event 4899, Warning 916, Error 154 |
| debug | `d.M.yyyy HH:mm:ss.fff [Level] message` (milliseconds) | VerboseDebug 10683, Debug 3285 |
| audit | `d.M.yyyy HH:mm:ss [Audit] message` | Audit only |
| chat | `d.M.yyyy HH:mm:ss [Chat] <html-ish name>: text` | Chat |

- **Multi-line entries exist.** main has 307 lines and debug has 448 lines without a timestamp: stack traces and
  the startup mod table (`ModID Version Name FileName ...`). They belong to the entry above them.
- Many messages start with a mod tag, like `[vinconomy] ...` or `[StackSyncSafety] ...`, and debug lines use `[modid]`
  or `[File.dll]`. That tag is a useful "source" facet.
- Clock: the Analytics module already treats these timestamps as UTC. The analyzer must use the same
  assumption so both pages agree. The UTC+3 idea was dropped on purpose.

### What is actually in them

**main**, the signal worth surfacing:
- Warnings and errors, most of them repeated endlessly with only the numbers changing:
  - `Step parented shape … did not define …` (227×, a mod asset problem)
  - `Server overloaded. A tick took Nms` (67×)
  - `Cannot fix itemstack mapping, item/block id N not found … Will delete stack` (66×, items being deleted!)
  - `At position X,Y,Z for block aculinaryartillery:spile-copper-east a BlockEntitySpile threw an error` (51×, plus a stack trace)
  - `[StackSyncSafety] Join-scan failed for <player>` (per player)
  - `Patch N in …json: File … not found`, recipe errors, `[Config lib] Error on parsing patch …`
- Startup block: game version, .NET, CPU and RAM, world seed, the loaded mod table, and startup warnings.
- `[Event]`: joins and leaves, which Analytics already imports, plus save cycles (`World saved! Saved N chunks…` 256×),
  shutdown flavour text, and so on.

**audit**, the "who did what, where" record:

| Kind | Example | Volume |
|---|---|---|
| Inventory click | `X left clicked slot 20 in backpack-<uid>. Before: (…), after: (…)` | ~22k/day (left + shift clicks), mostly noise |
| Move | `X moved 1xgame:plank-ebony from craftinggrid-… to craftinggrid-…` | high |
| Take / put | `X Took 6xgame:fruit-cranberry from game:fruitingbush… at x, y, z.` / `X Put 8xgame:firewood into Ground storage at x, y, z.` | high, **the most useful kind** |
| Place block | `X placed a chute at x, y, z` / `tried to place a block but rejected …` | medium |
| Kill | `Player [RAC] RiddlE9 killed albase:sand-skeleton at x, y, z` | medium |
| Command | `command for Orex_ /we g s 5`, `/land claim load 1` | ~300/day, **high value** |
| Join / leave | `LittleJester joined.` | low |
| Mount | `[RAC] Orex_ mounts/embarks a tameddeer-elk… at …` | low |
| Rejected position | `Rejected player/mount position update for X. Client sent …, server pos was …` | ~4k/day, **anti-cheat / lag signal** |
| Open bag | `X opened held bag inventory (…) on entity N/…` | low |

- Player names can carry a guild tag (`[RAC] RiddlE9`) or be animals (`Енот (самка) Took …`). The
  parser has to split the tag from the name, and treat non-players as "entity" actors.
- Coordinates are absolute world coordinates (`480668, 166, 527807`). The in-game map shows them relative to
  spawn, so the UI should show both once the spawn offset is known (open question 5).
- Item codes are `domain:path` (`game:ingot-iron`), but the click lines use translated names
  (`12xАндезит`), so item search has to match both.

**debug** is mostly mod-loading and system detail (`VerboseDebug`). It helps when a mod breaks at
startup, and is otherwise rarely needed. It should be searchable but hidden by default.

---

## 2. Goals and non-goals

**Goals**
1. One fast search across main, audit and debug (optionally chat), over **weeks**, not just the
   game's 5 days.
2. Answers to the questions an admin actually asks, one click away:
   - "What errors is the server throwing, and which ones are new?"
   - "What did player X do yesterday?" / "What commands did they run?"
   - "Who took things from the chest at x,y,z?" / "Who was near this spot around 21:00?"
   - "Why did startup take long / which mod is complaining?"
   - "Is someone's client being rejected a lot (lag or a cheat)?"
3. Easy to use: facets and clickable chips instead of a query language you have to learn, with an optional
   text syntax for power users.
4. Never touch the game server. Read the log files only; never restart, write to, or lock them.

**Non-goals (for now)**
- Editing, deleting or "clearing" game logs.
- Real-time alerting to Discord and similar (it can build on this later; see phase 4).
- Parsing every mod-specific log format (`alquest/`, anticheat). Those are indexed as plain text only if enabled.

---

## 3. Architecture

Same layering as the other modules:
`endpoint → IServerLogService → service → repositories → RemoteOperations / SQLite`.

```
Modules/ServerLogs/            (name avoids confusion with Modules/Logging = panel logs)
  module.json
  Configuration/ServerLogsOptions.cs
  Contracts/ServerLogsDtos.cs
  Endpoints/ServerLogsRoutes.cs, ServerLogsEndpoints.cs
  Services/
    IServerLogService.cs, ServerLogService.cs      search, facets, digests, context
    LogIndexer.cs                                   BackgroundService: incremental ingest
    Parsing/LineParser.cs                           timestamp, level, multi-line join
    Parsing/AuditParser.cs                          structured audit fields
    Parsing/MessageSignature.cs                     normalise numbers/coords/uids to group repeats
  Persistence/
    ILogSourceRepository.cs, RemoteLogSourceRepository.cs   → Server/*/server-logs.py
    ILogIndexRepository.cs, SqliteLogIndexRepository.cs     → serverlogs.db
  Tests/Unit, Tests/Feature
Server/Production/server-logs.py, Server/Development/server-logs.py
```

### 3.1 Reading files: `server-logs.py` (target-side, read-only)

It runs through RemoteOperations like `file-manager.py` and `mod-manager.py`, so SSH targets work the same way.
- `list` prints JSON of every known log file: relative path, size, mtime, and a hash of the first 4 KB
  (**identity**, so a file that moved into `Archive/` is recognised and not re-read).
- `read <relpath> <offset> <maxBytes>` prints raw bytes from the offset up to the last complete line.
- The allowed paths are fixed by the script: `server-{main,audit,debug,chat}.log` in `Logs/` and
  `Logs/Archive/*/`. There is no free path from the client. It opens files read-only and never takes locks. The game
  appends while we read, and a trailing partial line is left for the next pass.

### 3.2 Indexing: incremental, into `serverlogs.db` (SQLite + FTS5)

Why an index instead of grepping on demand:
- The game deletes archives after about 5 days, and the index keeps history for as long as we configure.
- A grep over 100+ MB for every keystroke or facet is slow, while FTS5 answers in milliseconds.
- Structured audit fields (player, action, item, coordinates) become real columns, so queries like
  "near x,y,z" or "by player" are cheap.
- SQLite is already how Analytics works (1.1 MB there). At this volume SQLite is still fine. Estimated size:
  about 1.5–2× the raw text of what we keep (see retention below).

Indexer loop (a `BackgroundService` every `IndexIntervalSeconds`, default 30):
1. `list`, then match each file to its known identity (hash + path). New files are read from 0 and known files from
   their stored offset. A file that shrank or has a new hash is treated as rotated (the old content already sits
   under its archive identity).
2. `read` in 1 MB chunks, parse, and insert in one transaction per chunk, then store the new offset.
3. The first run backfills everything that exists (all archives). It is throttled so it doesn't compete with the game for
   disk I/O (`MaxBytesPerMinute`).
4. Retention job (hourly): delete entries older than their per-file retention.

Tables (draft):
```
log_files(id, server_id, rel_path, identity_hash, kind, size, indexed_offset, first_ts, last_ts)
entries(id, server_id, file_id, ts, ts_ms, kind /*main|audit|debug|chat*/, level, source /*mod tag*/,
        signature_id NULL, message, extra TEXT NULL /*continuation lines*/)
entries_fts(message, extra)  -- FTS5, external content = entries, unicode61 tokenizer (Cyrillic OK)
audit(entry_id PK, actor, actor_tag, actor_is_player, action, item_code, item_label, qty,
      container, x, y, z)
signatures(id, kind, level, source, template, first_ts, last_ts, count)   -- "error digest"
boots(id, server_id, started_ts, game_version, mod_count, startup_seconds, warnings, errors)
```
Indexes: `(server_id, ts)`, `(server_id, level, ts)`, `audit(actor, ts)`, `audit(action, ts)`,
`audit(x, z)` for coordinate boxes.

Noise control: inventory click lines (`clicked slot`, about 30% of audit) are **stored** by default but
excluded from default searches (a "show inventory clicks" toggle). The config can drop them entirely
(`Audit:IndexInventoryClicks=false`) if disk use matters.

### 3.3 Message signatures (the heart of the "what's wrong" view)

Normalise a message by replacing numbers, coordinates, GUID/uid-looking tokens and player names with
placeholders. `At position 480668, 166, 527807 for block … threw an error` becomes
`At position <pos> for block … threw an error`. Group by (level, source, template) and store the count and first/last
seen. This turns 916 warnings into about 40 distinct rows and makes "new since last restart" possible.

---

## 4. The page (`/server-logs`, new NavigationMenu item)

Server picker (like the other pages), a time-range control (Last hour / Today / 24 h / 7 d / Custom),
and four tabs:

### 4.1 Search (default tab)
- One search box. Plain words use FTS (Cyrillic works). Optional chips/syntax that autocomplete as you type:
  `player:Orex_`, `level:error`, `log:audit`, `action:took`, `item:ingot-iron`, `source:vinconomy`,
  `near:480668,166,527807~30`, `"exact phrase"`. Each facet can also be set by clicking.
- A left facet column with counts for the current result: Log (main/audit/debug/chat), Level, Source (mod tag),
  Player, Action. Clicking a facet toggles a filter.
- A virtualized result list with colour by level, matches highlighted, and the timestamp shown the same way as elsewhere in
  the panel (browser-local). Continuation lines such as stack traces collapse under the entry.
- A mini histogram above the results (count per time bucket). Dragging on it zooms the time range.
- Clicking a row opens **Context**: ±50 lines around that entry in the original file, across all logs
  interleaved by time. This is the "what happened right before this error" view.
- The URL holds the whole query, so results can be bookmarked or shared with another admin.
- Export the current result as `.txt` (original lines) or `.csv` (parsed columns), capped at `MaxExportRows`.

### 4.2 Problems (error and warning digest)
- A table of signatures: level, source (mod), template, count in range, trend sparkline, first seen, last
  seen, with a **NEW** badge when first seen is after the last server start.
- Sorted by "new first, then count". Clicking a row opens Search filtered to that signature.
- Special cards on top when present: "Items deleted by mapping fix" (`Cannot fix itemstack mapping`),
  "Block entity errors" with coordinates, "Server overloaded" count (links to Analytics performance).
- A "Hide" (mute) signature option for known harmless spam (e.g. `Step parented shape`), stored per server.

### 4.3 Players (audit view)
- Pick a player (autocomplete from the audit actors) to see a timeline of their session: joins and leaves, commands,
  kills, blocks placed, and items taken and put (grouped: "Took 199× ingot-copper from 12 containers").
- A **Location** mode: enter coordinates (or paste an audit line) plus a radius to see who interacted there and when.
  "Who emptied this chest" is answered here.
- A **Commands** list: every `command for X /…` line, filterable, because this is what admins check most.
- **Rejected position** counter per player per hour, which highlights people who are lagging or cheating.

### 4.4 Startups
- One card per server boot, parsed from main: time, game version, number of mods, how long startup took
  (from "Server logger started" to "Entering runphase GameReady"; about 31 s today), and warning and error counts during startup (link to them). A
  diff against the previous boot: mods added, removed or updated (this ties in nicely with the Mod Manager).

### 4.5 Small conveniences
- A **Live tail** toggle on Search: it polls every few seconds while the tab is visible (same `usePolling` pattern
  as the dashboard) and appends new matching lines.
- Saved searches (per user, stored in the DB).
- Dashboard hook: "Needs attention" gains a "N new error types since last restart" item that links to the
  Problems tab.

---

## 5. HTTP API (cookie + CSRF, like the other browser modules)

```
GET  /api/server-logs/{serverId}/search?q=&from=&to=&log=&level=&player=&action=&near=&cursor=&limit=
GET  /api/server-logs/{serverId}/facets?(same filters)
GET  /api/server-logs/{serverId}/histogram?(same filters)&buckets=
GET  /api/server-logs/{serverId}/entries/{id}/context?before=50&after=50
GET  /api/server-logs/{serverId}/signatures?from=&to=&level=
POST /api/server-logs/{serverId}/signatures/{id}/mute        (antiforgery)
GET  /api/server-logs/{serverId}/players?prefix=
GET  /api/server-logs/{serverId}/players/{name}/activity?from=&to=
GET  /api/server-logs/{serverId}/boots
GET  /api/server-logs/{serverId}/export?(filters)&format=txt|csv
GET  /api/server-logs/{serverId}/status     index progress, last indexed time, db size
```
Keyset pagination on `(ts, id)`, since offsets over millions of rows are slow.

---

## 6. Configuration

```jsonc
"ServerLogs": {
  "IndexIntervalSeconds": 30,
  "MaxBytesPerMinute": 20971520,          // backfill throttle
  "RetentionDays": { "main": 180, "audit": 30, "debug": 14, "chat": 30 },
  "Audit": { "IndexInventoryClicks": true },
  "MaxExportRows": 100000,
  "Servers": {
    "production-local": { "Operation": "production-local-logs-read", "IncludeChat": false }
  }
}
```
Plus one RemoteOperations command per server running `server-logs.py <Data dir>`. The name
`production-local-logs` is already taken by the console log command, hence `-logs-read`.

Disk estimate with these defaults: audit ≈ 15 MB/day × 60 days × ~1.7 ≈ **1.5 GB**. The rest is small.
If that is too much, disable click lines (≈ −30%) or shorten audit retention. See open question 1.

---

## 7. Security and privacy

- The panel is for admins only (the existing auth). There are no extra roles yet. When roles arrive, audit and chat should be
  admin-only.
- **Chat logs are private conversations.** They are off by default (`IncludeChat=false`) and need an explicit opt-in.
- The script enforces a fixed file allowlist, and `read` validates offset and size. The panel never sends a path the
  script didn't list.
- Search input goes into FTS5 `MATCH` only through a builder that quotes terms, so users can't inject SQL or FTS syntax.
  There is no user-supplied regex on the server (no ReDoS). The optional "regex" filter runs client-side on the
  loaded page only.
- Export is capped in size. Responses never include host paths, only relative log names.
- Coordinates reveal where players' bases are. That's fine for admins, and it's noted in Docs/Security.md.

---

## 8. Testing

- Unit: the timestamp and level parser (both formats, and ms on debug), multi-line joining, each audit line kind
  (with guild tags, Cyrillic names, animals as actors, translated item names), signature normalisation,
  the FTS query builder (quoting and special characters), and rotation and identity detection.
- Fixtures: small anonymised excerpts of real main, audit and debug logs, checked into Tests (no full logs).
- Feature: the indexer against a fake `ILogSourceRepository` (append, rotate, truncate, partial last line),
  search pagination and filters, retention.
- Manual: first backfill on production while players are online. Watch game-server CPU and disk (the throttle).

---

## 9. Delivery phases

1. **Index plus search.** Script, indexer and backfill, main/debug/audit as plain text, the Search tab with
   facets, histogram, context, and URL state. This alone replaces SSH plus grep.
2. **Problems tab.** Signatures, NEW badges, mute, special cards, and the dashboard hook.
3. **Audit structure.** The parsed audit table, Players tab, location queries, commands list, and
   rejected-position counters.
4. **Startups tab, live tail, saved searches, export.** Later: optional chat and mod logs, and
   notifications for new error signatures.

Each phase is deployable on its own. Build and try it on the test server first, as agreed.

---

## 10. Open questions for you

1. **Retention / disk:** is about 1.5 GB for 60 days of audit OK, or should audit be kept shorter or drop
   inventory-click lines?
2. **Chat log:** include it (opt-in), or leave it out entirely?
3. **Mod logs** (`alquest/`, `alegacy-anticheat.log`, `arcanumlib-diagnostics.log`): index them as plain text,
   or ignore them?
4. **Timestamps:** keep treating log times as UTC (consistent with Analytics), or should the page
   show exactly the time written in the file?
5. **Map coordinates:** do you want coordinates shown relative to spawn (like the in-game map)? If so, we
   need the spawn position (from the world config) once.
6. **Priorities:** is the phase order right, or is the audit/player view more urgent than the error digest?
