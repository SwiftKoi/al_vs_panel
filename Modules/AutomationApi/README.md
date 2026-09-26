# AutomationApi

Secret-authenticated HTTP API for external automation. It exposes configured game-server file transfers and lifecycle operations to machine clients without browser cookies or CSRF tokens.

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

`python3 manage.py setup` generates the local secret file at `secrets/api-key`. Compose mounts it and sets `AutomationApi__Enabled=true` for the application container. Keep the secret out of source control, application settings, and logs; rotate it by replacing the file content.
