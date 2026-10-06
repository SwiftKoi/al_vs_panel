# Audit module

An append-only record of **who did what** in the panel: server control, moderation actions, file
changes, mod updates, user management and similar privileged operations. It stores the entries,
prunes them by age and gives admins a searchable viewer at `/audit` (`AuditPage.tsx`).

The module is small on purpose. Recording is done by a shared endpoint filter in `Core`
(`Core/Auditing`), so a module opts a route in with one line and never has to call the audit store.
This module only stores and serves what the filter records.

## Features

| Feature | What it does |
|---|---|
| **Recording by route** | A route opts in with `.Audited(AuditCategories.Files, "delete")`. Every call is recorded after authorization: actor, role, client IP, time, category, action, server, target and details. |
| **Failures are recorded too** | A call that throws or returns a `4xx`/`5xx` is stored as *failed* with the reason (for example `Server instance 'x' was not found.`), so refused and broken attempts are visible. |
| **Secrets never stored** | Fields whose name looks secret (`password`, `token`, `key`, `code`, `secret`, `otp`, `cookie`) are dropped. File contents (`content`) are dropped. Console commands that mention a password, token or secret keep only the command name. |
| **Bounded entries** | Values are cut at 200 characters and the details object at 4000. An oversized one is replaced by `{"truncated":true}`. |
| **Never breaks the action** | If the audit store is down, the error is logged and the audited action still succeeds. |
| **Attribution for automation** | Calls made with the [AutomationApi](../AutomationApi/) key are recorded with actor `automation-api` and role `api`. |
| **Searchable viewer** | `GET /api/audit` filters by actor, category, action, server, outcome, time range and free text (target, details, error, actor), newest first, with paging (default 100, at most `MaximumPageSize`). |
| **Filter values** | `GET /api/audit/facets` lists the actors, category/action pairs and servers that occur, for the viewer's drop-downs. |
| **Append-only** | There is no HTTP route to add, edit or delete an entry. Entries disappear only through retention pruning. |
| **Retention** | A background worker deletes entries older than `RetentionDays` (365) every `PruneIntervalMinutes` (360). |
| **Own database** | Entries live in `audit.db`, separate from accounts and application logs, so log clean-ups or growth cannot touch them. |

## What is recorded

Only actions that change something or hand out data. Reads (listing files, viewing logs, status)
and sign-in attempts are not here. Sign-ins have their own history (`/auth/login-logs`).

| Category | Actions |
|---|---|
| `server` | `start`, `stop`, `restart`, `command` (console) |
| `moderation` | `gamemode`, `teleport`, `warn`, `kick`, `ban`, `hardban`, `unban`, `landclaim`, `allowcharselonce` |
| `files` | `save`, `upload`, `mkdir`, `rename`, `move`, `delete`, `download-archive`, `trash-restore`, `trash-purge`, `trash-empty`, `compress`, `extract`, `cancel-operation` |
| `mods` | `update`, `rollback`, `pin` |
| `users` | `create`, `delete`, `change-role` |
| `account` | `change-password`, `2fa-enable`, `2fa-disable` |
| `logs` | `clear` (application log), `mute-signature` (server-log problem) |
| `remote` | `execute` |

Moderators' actions are recorded like anyone else's. Only admins can read the trail.

## Entry shape

```json
{
  "id": 42,
  "timestampUtc": "2026-09-29T18:42:51+00:00",
  "actor": "admin", "actorRole": "Admin", "ipAddress": "203.0.113.7",
  "category": "files", "action": "delete",
  "serverId": "main", "target": "Mods/old.zip",
  "detailsJson": "{\"root\":\"data\",\"path\":\"Mods/old.zip\",\"permanent\":\"false\"}",
  "succeeded": true, "error": null
}
```

`target` is the main object: a player, a file path, a mod id, a username or user id, or a remote
operation name. `detailsJson` holds the remaining route, query and request-body fields.

## Access

| Route | Who |
|---|---|
| `GET /api/audit`, `GET /api/audit/facets` | admins only (the default policy) |

## Configuration

Section `AuditTrail`:

| Key | Default | Meaning |
|---|---|---|
| `DatabasePath` | `/var/lib/alegacy/data/audit.db` | SQLite file. The directory is created if missing |
| `RetentionDays` | `365` | Entries older than this are pruned |
| `PruneIntervalMinutes` | `360` | How often pruning runs |
| `MaximumPageSize` | `500` | Largest page a query may ask for |

The database is created on first use. Include the `app_data` volume in backups.

## Adding audit to a new route

```csharp
group.MapPost("/{serverId}/things", ThingEndpoints.CreateAsync)
    .RequireAntiforgery()
    .Audited(AuditCategories.Files, "create-thing");   // category from Core, short verb
```

Then add the action's label to `audit.actions.<category>.<action>` in both locale files
(`frontend/src/locales`). The filter takes the server from the `serverId` route value, the target
from the request's `PlayerName`, `Username`, `Path`… fields (or the `path` query, or the route id),
and puts the rest in the details. Put every new mutating route behind `.Audited(...)`.

## Layout

```text
Core/Auditing/    IAuditTrail + AuditEvent + AuditCategories (the contract),
                  AuditEndpointExtensions (.Audited filter), AuditRequestDescriber (what to record, what to strip)
Endpoints/        AuditRoutes (admin-only, read-only), AuditEndpoints
Services/         AuditTrailService (implements IAuditTrail, never throws), AuditQueryService (validation)
Persistence/      AuditDbContext, AuditEventModel, SqliteAuditRepository (creates the schema on first use)
Infrastructure/   AuditModule (DI, option validation), AuditPruneWorker (retention)
```

## Limits

- The trail records what the **panel** did. Changes made directly on the host, in the game, or by
  another route to the same files (SSH, Docker) are not seen.
- It does not keep before/after file contents, so a file edit cannot be undone from here.
- Downloads and other reads are not recorded, apart from `download-archive`.
- Whoever can write to the `app_data` volume can alter `audit.db`. Treat it as an
  operational record, not tamper-proof evidence.
