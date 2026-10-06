# AutomationApi

Secret-authenticated HTTP API for external automation. It exposes configured game-server file transfers and lifecycle operations to machine clients without browser cookies or CSRF tokens.

## Features

| Feature | What it does |
| --- | --- |
| **API-key authentication** | A single shared secret in the `X-Api-Key` header replaces the browser cookie and CSRF token. It is read from a file on every request, so rotating it needs no restart. |
| **Server discovery** | `GET /api/v1/servers` lists the configured server profiles, so a client does not need hard-coded ids. |
| **Runtime status** | `GET …/status` returns the authoritative status of one server (whether it is running, and so on). |
| **File listing** | `GET …/files/{root}` lists a directory under a configured file root. |
| **File download** | `GET …/files/{root}/download` streams a file, for example a world backup. |
| **File upload** | `POST …/files/{root}/upload` writes one multipart file into a writable root, for example to deploy a mod or restore a save. |
| **Lifecycle control** | `POST …/start`, `…/stop` and `…/restart` control the game server. A second lifecycle request while one is running returns `409`. |
| **Mods catalog** | `GET …/servers/{serverId}/mods` returns the mods the public website may list: published on ModDB and client-side or both-sides. Server-side mods, private mods and unreadable files are left out. It is behind its own read-only key (`ModsKeyFile`). |
| **Opt-in exposure** | The whole API is off unless `AutomationApi:Enabled` is `true`. When off, the routes are not mapped at all. |

All file and lifecycle behaviour is delegated to [FileManager](../FileManager/) and
[ServerManagement](../ServerManagement/), and the mods catalog to [ModManager](../ModManager/), so the same roots, limits and path checks apply as in the browser UI.

## Authentication

Every route requires the `X-Api-Key` header. The key is read from the file configured by `AutomationApi:KeyFile` (default `/run/secrets/api_key`) on every request, so replacing the mounted secret rotates the key without a restart. The presented key is compared against the configured secret with a length-independent, timing-safe comparison and is never logged.

The API is disabled by default. Set `AutomationApi:Enabled` to `true` and provide the secret file to map the routes. When disabled, the routes do not exist (`404`). A missing or empty secret file with the API enabled fails application startup.

This key authenticates only the `/api/v1` routes below. It does not grant access to the browser endpoints under `/api/servers`, `/api/logs`, or `/auth`.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/servers` | List configured server profiles. |
| `GET` | `/api/v1/servers/{serverId}/status` | Read the authoritative runtime status. |
| `GET` | `/api/v1/servers/{serverId}/files/{root}?path=<dir>` | List a directory under a configured file root. |
| `GET` | `/api/v1/servers/{serverId}/files/{root}/download?path=<file>` | Stream a file. |
| `POST` | `/api/v1/servers/{serverId}/files/{root}/upload?path=<dir>` | Upload one `multipart/form-data` file part into a writable root. |
| `POST` | `/api/v1/servers/{serverId}/start` | Start the configured server. |
| `POST` | `/api/v1/servers/{serverId}/stop` | Stop the configured server. |
| `POST` | `/api/v1/servers/{serverId}/restart` | Restart the configured server. |
| `GET` | `/api/v1/servers/{serverId}/mods` | Read-only mods catalog for the public website. Uses the **mods key**, not the automation key. |

### Mods catalog

Authenticated with the `X-Api-Key` header, using the key in `AutomationApi:ModsKeyFile` (default empty: the route is not mapped). That key opens this route only, and the automation key does not open it.

```json
{
  "serverId": "main",
  "gameVersion": "1.22.7",
  "generatedUtc": "2026-10-06T12:00:00+00:00",
  "complete": true,
  "mods": [
    { "modId": "alegacyattire", "name": "Alegacy Attire", "version": "0.2.0", "side": "both",
      "description": "…", "authors": ["…"], "modDbAssetId": 72123,
      "modDbUrl": "https://mods.vintagestory.at/show/mod/72123", "logoUrl": "https://moddbcdn.vintagestory.at/…",
      "downloadUrl": "https://moddbcdn.vintagestory.at/AlegacyAttire_….zip?dl=AlegacyAttire.zip", "downloadFileName": "AlegacyAttire.zip" }
  ]
}
```

- `side` is `client` or `both` (`modinfo.json` `universal` is reported as `both`). Mods with side `server` are never listed.
- Only mods published on ModDB are listed. A mod that is not on ModDB (private), has no readable `modinfo.json`, or failed its ModDB check is omitted.
- `complete` is `false` when a ModDB lookup failed. A mod may then be missing only because it could not be checked, so a consumer must not treat absence as removal.
- `downloadUrl` / `downloadFileName` are ModDB's file for **exactly the installed version**, on a trusted ModDB host over https, so a download of "all mods" matches what the server runs. They are `null` when ModDB has no release or file for that version; a different release is never substituted.
- It reads the ModManager snapshot (the same scan and ModDB cache as the Mods page). `404` unknown or unconfigured server, `502` mod folder or ModDB unavailable.

`serverId`, `root`, and relative paths follow the same configuration and validation rules as the browser FileManager and ServerManagement modules. Unknown servers or roots return `404`, paths outside a root return `400`, mutations on read-only roots return `403`, and a concurrent lifecycle operation returns `409`.

## Examples

```sh
export PANEL=https://panel.example.com
export API_KEY_FILE=./secrets/api-key
export API_KEY=$(cat "$API_KEY_FILE")

curl -sS -H "X-Api-Key: $API_KEY" "$PANEL/api/v1/servers"

curl -sS -H "X-Api-Key: $API_KEY" \
  -o world.zip \
  "$PANEL/api/v1/servers/main/files/data/download?path=backups/world.zip"

curl -sS -H "X-Api-Key: $API_KEY" \
  -F "file=@world.zip" \
  "$PANEL/api/v1/servers/main/files/data/upload?path=backups"

curl -sS -X POST -H "X-Api-Key: $API_KEY" \
  "$PANEL/api/v1/servers/main/restart"
```

## Configuration

```json
{
  "AutomationApi": {
    "Enabled": true,
    "KeyFile": "/run/secrets/api_key"
  }
}
```

`python3 manage.py setup` generates the local secret file at `secrets/api-key`, and the mods catalog key at `secrets/mods-api-key`. Compose mounts them and sets `AutomationApi__Enabled=true` and `AutomationApi__ModsKeyFile=/run/secrets/mods_api_key` for the application container. Keep the secret out of source control, application settings, and logs; rotate it by replacing the file content.
