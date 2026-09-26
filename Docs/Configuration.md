# Configuration

## Configuration sources

Use `appsettings.json` for non-secret defaults and structure. Use environment-specific configuration for safe environment differences. Use Docker secrets or an external secret manager for credentials and key material.

Environment variables use ASP.NET Core’s hierarchical `__` separator when needed, for example:

```text
Remote__Targets__remote-main__Host
Remote__Targets__remote-main__Username
Remote__Targets__remote-main__HostKeyFingerprintSha256
```

Do not put secret values in `appsettings.json`, `appsettings.Development.json`, `compose.yaml`, or `.env` files.

The sibling Vintage Story server container is configured through `Server/Development/.env` or `Server/Production/.env`. `SERVER_UID` and `SERVER_GID` set the container identity, and `VINTAGESTORY_ARCHIVE_URL` is the required URL of the server ZIP archive downloaded by `Server/install.sh` on first start; the container refuses to start without it. `python3 manage.py setup` regenerates these files and preserves an existing `VINTAGESTORY_ARCHIVE_URL`.

## Runtime state

- SQLite database: `/var/lib/alegacy/data/alegacy.db`
- Data Protection keys: `/var/lib/alegacy/keys`
- Initial admin password secret: `/run/secrets/admin_password`
- SSH private-key secret: `/run/secrets/ssh_private_key`

The first two paths are backed by named Docker volumes. Back them up together because losing Data Protection keys invalidates existing authentication cookies.

## Compose secrets

Create local secret files and set group ownership and permissions automatically using the command-line utility:

```sh
python3 manage.py setup
```

The script generates the secret files under `secrets/`, sets permissions to `640` (with directory permissions `700`), and runs `chgrp 1654` so the non-root container can read them.

For production, specify the domain via `--domain` during setup (e.g. `python3 manage.py setup --domain yourdomain.com`). This populates the `DOMAIN` environment variable in `.env` used by the production Caddy gateway.


## Authentication configuration

Authentication settings are under `Authentication`. The default cookie lifetime is eight hours with sliding expiration. The initial admin username and password-secret path are configured under `Authentication:Admin`; the password value remains only in the mounted secret file.

Login-attempt auditing is configured under `Authentication:LoginLog`. `MaxRetainedDays` (default 90) bounds the `LoginEvents` audit table; entries older than the window are pruned on the next recorded login.

## Automation API configuration

Automation API settings are under `AutomationApi` and are owned by the AutomationApi module:

```json
{
  "AutomationApi": {
    "Enabled": true,
    "KeyFile": "/run/secrets/api_key"
  }
}
```

- `Enabled` maps the `/api/v1` routes. It defaults to `false`; when disabled, the routes do not exist and the browser and Compose deployments behave as before.
- `KeyFile` is the mounted secret file that holds the pre-shared key. The default is `/run/secrets/api_key`. The file is read on every request, so replacing its contents rotates the key without a restart.

Startup validation fails when the API is enabled and `KeyFile` does not exist. The Compose files mount `secrets/api-key` as `api_key` and set `AutomationApi__Enabled=true`; `python3 manage.py setup` generates the local key file. See the [AutomationApi guide](../Modules/AutomationApi/README.md) for the endpoint reference and client examples.

## Module configuration

Module-specific configuration belongs under a module-specific configuration section. The module’s options type and configuration binding belong to that module. Do not make `Program.cs` interpret module settings.

Execution targets are configured under `Remote:Targets`. A target owns its `Mode` (`Local` or `Ssh`); SSH targets also own host, port, username, private-key/passphrase secret paths, and host-key fingerprint. Environment overrides include the target name, for example `Remote__Targets__remote-eu__Host`.

Remote operation definitions are configured under `Remote:Commands`. Each definition references a named `Target` and contains one executable `Command`, optional trusted fixed `Arguments`, and an optional local/remote `User`. Dynamic arguments are appended separately by trusted module code. Do not configure a shell pipeline or compound shell expression as `Command` when the operation accepts dynamic arguments.

Server profiles are configured under `Servers:Instances`. Each profile exposes safe metadata (`Id`, `Name`, `Host`, `Port`, and `Location`) and references allowlisted RemoteOperations names through `StartOperation`, `StopOperation`, `RestartOperation`, `StatusOperation`, `ConsoleOperation`, `LogsOperation`, and `MetricsOperation`. Browser input never supplies operation names, executable paths, SSH targets, Compose projects, or service names.

`Servers:MaximumCommandLength` limits game-console commands and defaults to 512 characters. Server IDs must be non-empty and unique. An empty instance list is valid and makes the list endpoint return no configured servers.

## Logging configuration

Logging settings are under `LogStore` and are owned by the Logging module. The persisted level is independent of the console level. Defaults live in `appsettings.json`; `appsettings.Development.json` overrides the persisted level to `Debug` and shortens retention.

```json
{
  "LogStore": {
    "DatabasePath": "/var/lib/alegacy/data/logs.db",
    "MinimumLevel": "Information",
    "MaximumRetainedDays": 30,
    "MaximumRetainedEntries": 500000,
    "BufferCapacity": 4096,
    "FlushBatchSize": 100,
    "PruneIntervalMinutes": 60,
    "StructuredStateMaxBytes": 4096
  }
}
```

- `DatabasePath` is the dedicated log database in the `app_data` volume.
- `MinimumLevel` is the lowest level persisted to the database (development: `Debug`, production: `Information`).
- `MaximumRetainedDays` and `MaximumRetainedEntries` bound log volume; the background worker prunes older/overflowing entries.
- `BufferCapacity` bounds the in-memory capture buffer; entries beyond it are dropped and reported instead of blocking requests.
- `FlushBatchSize` is the maximum batch size the background writer persists at once.
- `PruneIntervalMinutes` controls how often retention pruning runs.
- `StructuredStateMaxBytes` bounds the serialized structured state stored per entry.

The host `Logging:LogLevel` sets `Microsoft.EntityFrameworkCore` to `Warning`. This silences EF Core command/context logs at the source so the log store does not capture its own write activity; the Logging provider also hardens this by never persisting EF Core categories below `Warning`. EF Core errors and warnings are still captured.

See the [ServerManagement configuration guide](../Modules/ServerManagement/README.md) for complete local and SSH examples, operation contracts, configuration precedence, and container limitations. `appsettings.Development.json` includes a development-only local profile for the sibling AlegacyDocker checkout; production defaults remain empty.
