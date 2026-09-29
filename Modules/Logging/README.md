# Logging module

Captures the **panel's own** application log (everything written through `ILogger`) into a SQLite
database and lets admins search it. It is not the game server's log. That is the
[ServerLogs](../ServerLogs/) module. The frontend page is `/logs` (`LogsPage.tsx`).

Routes live under `/api/logs` and are **admin-only**.

## Features

| Feature | What it does |
|---|---|
| **Persistent log capture** | `SqliteLogProvider` is registered as an `ILoggerProvider`, so every log call in the panel is copied into a bounded in-memory queue and then written to SQLite by a background worker (`LogWriteWorker`). Request handling never waits for the database. |
| **Structured entries** | Each row keeps time (UTC), level, category, event id, the rendered message, the structured state as JSON (truncated to `StructuredStateMaxBytes`) and, for exceptions, type, message and stack trace. |
| **Query and filter** | `GET /api/logs` filters by minimum level, source (logger category), time range and a free-text search (matched against the message text), with `limit` (1–1000, default 100) and `offset` paging. Returns `{ items, total, offset, limit }`. |
| **Source list** | `GET /api/logs/sources` returns the distinct categories that appear in the log, for the source filter. |
| **Level summary** | `GET /api/logs/summary` returns counts per level (Debug … Critical) for an optional time range. It feeds the badges on the Logs page. |
| **Errors view** | `GET /api/logs/errors` is a shortcut for entries at `Error` and above. |
| **Manual clean-up** | `DELETE /api/logs?olderThanDays=N` deletes entries older than N days (at least 1). Without the parameter it clears the whole log. Returns `{ deleted }`. |
| **Automatic retention** | Every `PruneIntervalMinutes` the worker deletes entries older than `MaximumRetainedDays` and trims the oldest ones beyond `MaximumRetainedEntries`. |
| **Back-pressure reporting** | If the queue is full, new entries are dropped rather than slowing the app. The worker counts them and logs a warning (to the console) with how many were lost. |
| **Graceful shutdown** | On stop, the worker writes whatever is still queued. |

## Design notes

- **EF Core noise is filtered.** Categories starting with `Microsoft.EntityFrameworkCore` are
  stored only at `Warning` and above. Without this, each log write would itself be logged and
  written again (a feedback loop), and the database would fill with SQL command traces.
- **Failure mode.** If the log database cannot be created at startup, the worker logs to the console
  and stops. The panel keeps running without persistent logs, and the query endpoints answer `503`
  ("Log store unavailable"). Invalid filters answer `400`.
- The log database is separate from the accounts database (`logs.db` next to `alegacy.db`), so log
  growth or corruption cannot affect logins.

## Configuration

Section `LogStore`:

| Key | Default | Meaning |
|---|---|---|
| `DatabasePath` | `/var/lib/alegacy/data/logs.db` | SQLite file. The directory is created if missing |
| `MinimumLevel` | `Information` | Lowest level stored |
| `MaximumRetainedDays` | `30` | Age limit for automatic pruning |
| `MaximumRetainedEntries` | `500000` | Row limit for automatic pruning |
| `BufferCapacity` | `4096` | Queue size before entries are dropped |
| `FlushBatchSize` | `100` | Entries written per batch (also flushed at least once a second) |
| `PruneIntervalMinutes` | `60` | How often pruning runs |
| `StructuredStateMaxBytes` | `4096` | Cap for the stored JSON state per entry |

All numeric values must be positive and `MinimumLevel` a valid .NET `LogLevel`, otherwise the app
refuses to start.

## Layout

```text
Endpoints/       LoggingRoutes, LoggingEndpoints (filter parsing, error mapping)
Services/        LoggingService (validation, DTO mapping)
Infrastructure/  SqliteLogProvider + SqliteLogWriter (ILogger side), LogWriteWorker (batch writer + pruning),
                 LogDropCounter, LoggingModule (DI, option validation)
Persistence/     LogDbContext, LogEntryModel, SqliteLogRepository
```
