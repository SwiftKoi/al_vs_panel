# ModManager module

Shows the mods installed on a game server next to the official Vintage Story ModDB
(`https://mods.vintagestory.at`), and updates them in one click with a backup to roll back to.
The frontend page is `/mods` (`frontend/src/pages/ModsPage.tsx`).

## Features

Routes are under `/api/servers/{serverId}/mods` and are **admin-only**.

| Feature | What it does |
|---|---|
| **Installed mod scan** | Lists the mods on the server (zips, folders, single-file `.cs`/`.dll`) with id, name and installed version, and whether the running server has loaded each one. |
| **ModDB comparison** | Looks every mod up on the official Vintage Story ModDB and assigns a status: update available, up to date, ahead, no compatible release, not on ModDB, check failed or unidentified. Results are cached for `CacheMinutes`. **Check ModDB** forces a refresh. |
| **Game-version compatibility** | Only releases compatible with the server's game version (`major.minor`) are offered for one-click update. Mods already on a pre-release stay on that track. |
| **Version history and changelogs** | Per-mod detail with every release, its game-version tags and a sanitised changelog. Any release can be installed, including newer pre-releases. |
| **Verified one-click update** | Downloads from trusted ModDB hosts only, checks the zip and its `modinfo.json` (id and version must match), stages it on the server and swaps it in all-or-nothing. It runs as a background job, one per server. |
| **Update all** | Updates every mod that has a compatible newer release, skipping pinned ones. |
| **Backup and rollback** | Replaced files go to `Data/ModBackups/`. **Rollback** restores the last update and removes what it installed. Only one update is kept. |
| **Pinning** | Pin a mod to keep it out of **Update all**. Pins are saved in `mod-manager.json`. |
| **Restart hint** | Shows "restart required" when a mod file is newer than the server's start, and offers a restart button through ServerManagement. Nothing restarts automatically. |

## Flow

```text
HTTP request -> ModManager endpoint -> IModManagerService
             -> IModTargetRepository -> IRemoteOperationsService -> Server/*/mod-manager.py
             -> IModDbRepository     -> ModDB HTTP API (cached)
```

- **Scan.** The helper lists `Data/Mods` (zips, mod folders, single-file `.cs`/`.dll`) and returns
  the raw `modinfo.json` bytes. The panel parses them with Newtonsoft, the same lenient parser
  the game uses (comments, trailing commas, unquoted keys).
- **Game version.** Read from the last `Game Version: v…` line in `Data/Logs/server-main.log`.
  It can be overridden per server with `GameVersion`. The same log's
  `Mods, sorted by dependency:` line tells whether each mod was loaded by the running server.
- **ModDB lookup.** `GET /api/mod/{modid}` returns every release with its game-version tags and
  HTML changelog. Lookups are cached for `CacheMinutes`; **Check ModDB** in the UI bypasses the cache.
- **Compatibility.** A release is compatible when one of its tags has the server's `major.minor`
  and is not newer than the server (`ReleaseResolver`).
  - One-click update targets the newest compatible **stable** release.
  - A mod already on a pre-release (e.g. `4.0.0-rc.10`) stays on the pre-release track.
  - Newer pre-releases are always shown and installable from the version history.
- **Statuses:**
  - `UpdateAvailable`, `UpToDate`
  - `Ahead`: installed is newer than anything published
  - `NoCompatibleRelease`
  - `NotOnModDb`: private or server-only mods
  - `CheckFailed`: ModDB unreachable
  - `Unidentified`: no readable modinfo
- **Update** (`ModUpdateRunner`, background job, one per server):
  1. Download from the trusted ModDB hosts only, re-checked after redirects and size-capped.
  2. Verify the zip: no unsafe paths, a root `modinfo.json` whose modid and version match the requested release.
  3. Stage the file on the server with `mod-manager.py stage`.
  4. Swap every staged file into `Mods` with `apply`. The swap is all-or-nothing and moves the
     replaced files to `Data/ModBackups/`.

  Mods that fail steps 1–3 are skipped, and nothing in `Mods` changes before the swap.
- **Rollback.** `rollback` restores the files from the last update and removes the ones it
  installed. Only **one** update is kept.
- **Restart.** Mods load at server start, so nothing is restarted automatically. The page shows
  "restart required" when a mod file (or the last update) is newer than the server's start time,
  and offers a restart button that uses the ServerManagement lifecycle.
- **Pins.** Pinned mods are skipped by **Update all**. Pins are stored in
  `/var/lib/alegacy/data/mod-manager.json` (`ModManager:SettingsPath`).

## API

| Method | Path | |
|---|---|---|
| GET | `/api/servers/{serverId}/mods?refresh=` | overview with statuses |
| GET | `/api/servers/{serverId}/mods/{modId}` | detail: releases with sanitised changelogs |
| PUT | `/api/servers/{serverId}/mods/{modId}/pin` | `{ "pinned": true }` |
| POST | `/api/servers/{serverId}/mods/update` | `{ "items": [{ "modId", "version" }] }` → job (202) |
| GET | `/api/servers/{serverId}/mods/update` | current/last job (204 when none) |
| POST | `/api/servers/{serverId}/mods/rollback` | undo the last update |

Mutating routes require the CSRF token.

## Public catalog

`IModManagerService.GetPublicCatalogAsync` projects the same snapshot onto what the public website may
list (`PublicModCatalogBuilder`). The rules live in one place:

- only mods published on ModDB (`NotOnModDb`, `Unidentified` and `CheckFailed` are left out)
- only client-side or both-sides mods: `modinfo.json` `universal` and ModDB `both` are `both`, a missing side is `both`
  (the game's default), `server` and unrecognised values are never listed
- one entry per mod id; the ModDB page URL uses the numeric `/show/mod/{assetId}` form
- `DownloadUrl` / `DownloadFileName` come from the ModDB release whose version equals the installed
  version, and only on a `TrustedDownloadHosts` host over https; otherwise they are null
- `Complete` is false if any ModDB lookup failed, so a consumer can tell "removed" from "could not be checked"

It is exposed to machine clients by [AutomationApi](../AutomationApi/) (`GET /api/v1/servers/{serverId}/mods`).

## Configuration

```json
"Remote": { "Commands": { "demo-local-mods": {
  "Target": "demo-local-host", "Command": "python3",
  "Arguments": ["%WorkspacePath%/Server/Development/mod-manager.py", "%WorkspacePath%/Server/Development/Data"] } } },
"ModManager": { "Servers": { "demo-local": { "Operation": "demo-local-mods" } } }
```

Optional settings (defaults in `ModManagerOptions`):

- `ModDbBaseUrl`
- `TrustedDownloadHosts`
- `CacheMinutes`
- `RequestTimeoutSeconds`
- `MaximumApiResponseBytes`
- `MaximumDownloadBytes`
- `MaximumConcurrentRequests`
- `SettingsPath`
- per server: `GameVersion`

## Security

- Changelog HTML is sanitised server-side with an allowlist (basic formatting and `https` links
  only, opened with `rel="noopener noreferrer nofollow"`) before the browser renders it.
- The helper only writes inside `Mods/`, `ModBackups/` and `.mod-staging/`. It rejects path
  separators and dot-files in names and refuses to overwrite a file it is not replacing.
- Mods run arbitrary code on the game server. A ModDB release is only as trustworthy as its author.
