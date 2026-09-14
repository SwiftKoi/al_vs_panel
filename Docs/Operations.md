# Operations

## Development build and start

Validate and build the Compose application with:

```sh
docker compose config --quiet
docker compose build
```

Before starting the application, create the local secret files described in [Configuration](Configuration.md). Start it with `docker compose up`; the development Compose configuration deliberately has automatic restart disabled.

The development application is exposed locally on `127.0.0.1:3000`. Do not expose it beyond the local machine.

## Production build and start

The production stack uses a separate Caddy gateway. Caddy serves the built frontend, terminates HTTPS, automatically obtains and renews certificates, and proxies `/auth`, `/api`, and `/health` to the private ASP.NET service. The ASP.NET service has no published host port.

Point the production DNS `A`/`AAAA` record at the Docker host, allow inbound TCP ports 80 and 443, and configure/start the stack using:

```sh
# Initialize secrets and configure the production DOMAIN
python3 manage.py setup --domain yourdomain.com

# Start the production compose stack
python3 manage.py prod
```

The production game server's scheduled midnight restart is a separate host
systemd timer. Install and enable it from the repository root with:

```sh
python3 manage.py install-restart-timer
```

The command derives the current absolute `Server/Production` path and host
user/group, installs the generated service and timer in `/etc/systemd/system`,
reloads systemd, and enables the timer immediately. It uses `sudo` when needed.
Run it again if the repository is moved or the production server account
changes. The timer is deliberately non-persistent, so a host that is offline at
23:50 does not perform a delayed restart after boot.


The gateway needs persistent `caddy_data` and `caddy_config` volumes for certificate and runtime state. Do not delete them during routine deployments. Caddy's automatic HTTPS requires the hostname to resolve to this host and ports 80/443 to be reachable for certificate issuance.

The production file intentionally does not configure game-server targets or commands. Supply those through production configuration/environment values, and do not expose the Docker socket unless local execution is explicitly required. Do not expose the SSH management capability directly to the public internet.

## Application logs

The Logging module persists every captured `ILogger<T>` event to a dedicated SQLite database at `/var/lib/alegacy/data/logs.db` inside the `app_data` volume. The database is separate from the authentication database so log activity cannot affect authentication. Retention is bounded by `LogStore` configuration and pruned in the background; the buffer drops entries without blocking when full and reports the drop count in the fallback console log.

To inspect recorded events, open the authenticated **Logs** page (`/logs`), or query the authenticated API (`GET /api/logs`, `GET /api/logs/errors`, `GET /api/logs/summary`, `GET /api/logs/sources`, `DELETE /api/logs`). The `app_data` volume must be included in backups; it now holds both the authentication database and the log database.

## Persistent state

The Compose deployment persists the SQLite database and ASP.NET Core data-protection keys in separate named volumes:

- `app_data` stores the authentication database.
- `data_protection_keys` stores keys used to protect authentication cookies and other protected data.

Back up both volumes together. Losing the data-protection volume invalidates existing authentication cookies; losing the database loses users and application state.

## Health and logs

`GET /health` is a lightweight liveness endpoint. It does not prove that remote SSH access or every persistence operation is available. Use application logs and targeted module checks for deeper diagnostics.

Configured game servers are managed through `/api/servers`. Lifecycle operations invoke fixed RemoteOperations definitions and then refresh the target's authoritative status. The backend does not infer game readiness from log messages. The log endpoint is an authenticated Server-Sent Events stream backed by the configured long-running log operation; disconnecting the browser cancels the local process or SSH command. Metrics are snapshots from the configured metrics operation.

The server page loads profiles from the API, remembers only the selected profile ID in browser local storage, refreshes the selected server's Docker-derived status every five seconds, reconnects the SSE stream after transport disconnects, and polls metrics every five seconds only while the selected server is online and the page is visible. Status polling pauses during lifecycle requests so it cannot overwrite the temporary starting/stopping state. Clearing the console affects only the browser's bounded local view and never truncates the target log file.

Lifecycle coordination is in-process and prevents overlapping start/stop/restart calls for one server within one application replica. A multi-replica deployment requires external coordination before lifecycle requests can be safely distributed across replicas.

When investigating a failure, identify the module from the request or log context, then inspect the endpoint, service, repository, and persistence boundary in that order. Error responses should remain safe for users; detailed diagnostics belong in secured logs.

## Common problems

- **Missing secret:** confirm the required file exists and is mounted at the path configured for the container. Never solve this by committing a secret.
- **Unexpected logouts after a restart:** confirm that the data-protection volume is present and retained.
- **The login screen appears after a reload:** inspect `GET /auth/session`, the authentication cookie, and the data-protection volume. The frontend restores its state from the backend session rather than local browser storage.
- **Protected API calls return `401`:** the cookie may have expired or the account may have been invalidated. Log in again; inspect the session and refresh endpoints before changing cookie settings.
- **Remote operation failure:** verify the remote host, port, username, key, host fingerprint, and command allowlist. Check the remote account's permissions and connectivity separately.
- **Missing or empty application logs:** confirm the `app_data` volume is writable and mounted, that `LogStore` is configured, and that the persisted minimum level is not above the events being examined. The log viewer shows persisted events; the console may show a superset.
- **Log entries dropped:** the bounded capture buffer reported drops because writes outran the background writer. Increase `LogStore:BufferCapacity` or lower `LogStore:MinimumLevel`.
- **Build or restore failure:** confirm Docker registry and NuGet access, then rerun the build with the relevant command output preserved for diagnosis.
