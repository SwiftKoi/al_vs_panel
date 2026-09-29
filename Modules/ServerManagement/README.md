# ServerManagement module

Runs and watches the game servers the panel manages: start, stop and restart, live status and
resource metrics, the live console and log stream, the connected-players table and moderation actions.
It contains no game logic of its own. Each feature is an allowlisted operation from `Remote:Commands`,
run through [RemoteOperations](../RemoteOperations/README.md) against the target named in the server profile.
The frontend pages are Overview (`/`), Server (`/server`) and Quick actions (`/actions`).

## Features

| Feature | What it does |
|---|---|
| **Server list** | `GET /api/servers` returns the configured servers (id, name, host, port, location) for the server switcher. Which server a page shows is chosen per request, never as global backend state. |
| **Lifecycle control** | `POST /api/servers/{id}/start\|stop\|restart` runs the profile's operation and returns the resulting status. Only one lifecycle operation per server runs at a time. A second one gets `409`. Admin-only. |
| **Live status** | `GET …/status` reports `online`, `offline` or `unknown` from the status operation. |
| **Resource metrics** | `GET …/metrics` returns CPU %, memory use/limit/%, block I/O, disk used/total/available/% and the server's start time. Analytics also samples it for history. |
| **Live console log stream** | `GET …/logs` is a Server-Sent-Events stream (`line`, `error`, `end` events) of the game's output. Admin-only. |
| **Console commands** | `POST …/commands` with `{ command }` sends one game command to the console. It is validated (non-empty, at most `Servers:MaximumCommandLength` = 512 characters) and appended as **one** argument, so it cannot add extra shell arguments. Admin-only. |
| **Connected players** | `GET …/connections` lists live client sockets with player name, join count, RTT, jitter, retransmits, queues and bytes. Player IP addresses are blanked for moderators. Analytics samples this for history. |
| **Moderation actions** | `POST …/actions/{gamemode, teleport, warn, kick, ban, hardban, unban, landclaim, allowcharselonce}`. Each action has its own endpoint that builds the console command from validated parts. Available to moderators, who never get the raw console. |
| **Role split** | Admins can do everything. Moderators get the list, status, metrics, connections and the actions above. The exact route list is in [Security](../../Docs/Security.md#authorization). |

### Moderation actions

`IServerActionsService` builds the game command from validated fields. Player names must match
`letters, digits, _ . -` (1–32 characters, no spaces).

| Action | Game command | Input rules |
|---|---|---|
| `gamemode` | `/gamemode <player> <0-2>` | 0 guest, 1 survival, 2 creative |
| `teleport` | `/tp <player> <x> <y> <z>` | finite coordinates within ±100 000 000. Prefix chosen by the server: none (as in the coordinates box), `=` (absolute) or `~` (relative) |
| `warn` | `/warn <player> <reason>` | reason required |
| `kick` | `/kick <player> [reason]` | reason optional |
| `ban` | `/ban <player> [reason]` | reason optional |
| `hardban` | `/hardban <player>` | – |
| `unban` | `/unban <player>` | – |
| `landclaim` | `/player <player> landclaimallowance\|landclaimmaxareas <n>` | allowance: any non-negative integer. Extra areas: 0–9999 |
| `allowcharselonce` | `/player <player> allowcharselonce` | lets the player pick a class again |

Reasons are at most 200 characters with no control characters. Every action is written to the panel log.

## Configuration

Server profiles, allowlisted operations and execution targets are wired together in configuration:

ServerManagement connects browser-facing server IDs to trusted RemoteOperations. Configuration has three linked levels:

1. `Remote:Targets` defines where operations execute.
2. `Remote:Commands` defines allowlisted executables and references a target by name.
3. `Servers:Instances` defines servers shown in the panel and references commands by name.

The selected server ID is a browser preference, but execution context is backend-owned. Every API request includes a server ID; the backend resolves that ID to configured operations and targets. The browser cannot choose an executable, target, SSH host, Docker service, or shell fragment.

## Development example

[`appsettings.Development.json`](../../appsettings.Development.json) contains a local target and a `demo-local` server profile for the development server under [`Server/Development`](../../Server/Development/).

## Execution targets

Local and SSH targets are named entries under `Remote:Targets`:

```json
{
  "Remote": {
    "Targets": {
      "local-host": {
        "Mode": "Local"
      },
      "remote-eu": {
        "Mode": "Ssh",
        "Host": "game.example.internal",
        "Port": 22,
        "Username": "alegacy-panel",
        "PrivateKeyFile": "/run/secrets/game_eu_private_key",
        "HostKeyFingerprintSha256": "SHA256:replace-with-real-fingerprint"
      }
    }
  }
}
```

An SSH target owns its host, port, username, private-key secret path, optional `PrivateKeyPassphraseFile`, and required SHA-256 host-key fingerprint. Each SSH target is validated independently and fails closed. Use a dedicated least-privileged account and key for each host where practical.

Environment variables can override target properties. Dictionary keys become path segments:

```text
Remote__Targets__remote-eu__Host=game.example.internal
Remote__Targets__remote-eu__Port=22
Remote__Targets__remote-eu__Username=alegacy-panel
Remote__Targets__remote-eu__PrivateKeyFile=/run/secrets/game_eu_private_key
Remote__Targets__remote-eu__HostKeyFingerprintSha256=SHA256:...
```

## Allowlisted operations

Every command references exactly one target:

```json
{
  "Remote": {
    "Commands": {
      "main-console": {
        "Target": "remote-eu",
        "Command": "/opt/alegacy/server-control.sh",
        "Arguments": ["command", "--"]
      }
    }
  }
}
```

Fields:

- `Target` references a key from `Remote:Targets`.
- `Command` is one trusted executable path or executable name, not a shell command line.
- `Arguments` contains trusted fixed arguments supplied before dynamic arguments.
- `User` is optional and runs the executable as that operating-system user.

For console operations, ServerManagement appends the validated game command as one dynamic argument. Given `/stats`, the conceptual invocation above is:

```text
/opt/alegacy/server-control.sh command -- /stats
```

Local execution uses `ProcessStartInfo.ArgumentList`. SSH execution POSIX-quotes every token. Do not put pipelines, redirects, substitutions, or compound shell expressions in `Command`.

The included Docker wrappers expose this contract:

```text
server-control.sh start
server-control.sh stop
server-control.sh restart
server-control.sh status
server-control.sh command -- <game-command>
server-control.sh logs
server-metrics.sh
server-connections.sh (optional)
```

`status` prints `online` or `offline` based on Docker Compose service state. Logs remain running and write log lines to standard output. Metrics print the JSON shape produced by `server-metrics.sh`. The optional connections operation prints `{"connections":[...]}` as produced by `server-connections.sh`.

## Multiple local and remote servers

Different servers may use different targets, while multiple operations may share a target. For example:

```json
{
  "Remote": {
    "Targets": {
      "local-host": { "Mode": "Local" },
      "remote-eu": {
        "Mode": "Ssh",
        "Host": "eu.example.internal",
        "Port": 22,
        "Username": "alegacy-panel",
        "PrivateKeyFile": "/run/secrets/eu_key",
        "HostKeyFingerprintSha256": "SHA256:..."
      }
    },
    "Commands": {
      "survival-status": {
        "Target": "local-host",
        "Command": "/opt/servers/survival/server-control.sh",
        "Arguments": ["status"]
      },
      "creative-status": {
        "Target": "local-host",
        "Command": "/opt/servers/creative/server-control.sh",
        "Arguments": ["status"]
      },
      "eu-status": {
        "Target": "remote-eu",
        "Command": "/opt/alegacy/server-control.sh",
        "Arguments": ["status"]
      }
    }
  }
}
```

The two local wrappers can point to different Compose project directories or fixed service names. Those details remain inside trusted configuration/wrappers. A remote operation uses only the credentials and host-key fingerprint of its named SSH target.

## Server profiles

Profiles connect public server IDs to operation names:

```json
{
  "Servers": {
    "MaximumCommandLength": 512,
    "Instances": [
      {
        "Id": "main",
        "Name": "Main server",
        "Host": "game.example.internal",
        "Port": 42420,
        "Location": "Warsaw",
        "StartOperation": "main-start",
        "StopOperation": "main-stop",
        "RestartOperation": "main-restart",
        "StatusOperation": "main-status",
        "ConsoleOperation": "main-console",
        "LogsOperation": "main-logs",
        "MetricsOperation": "main-metrics",
        "ConnectionsOperation": "main-connections"
      }
    ]
  }
}
```

`ConnectionsOperation` is optional. Without it the connected-players table stays empty. `Id` is the stable API identifier and must be unique. `Name`, `Host`, `Port`, and `Location` are display metadata only; they never choose an execution target. Every operation field references `Remote:Commands`.

The frontend calls explicit routes such as `/api/servers/main/status`; there is no mutable global backend “current server.” This keeps different browser tabs and concurrent users isolated. Local storage remembers only the preferred ID.

## Local execution and containers

Use a local target only when the web-panel process can execute its wrapper with the required permissions. The development Compose configuration supports local execution from inside the web-panel container by mounting the development server checkout at the same absolute path used by `appsettings.Development.json`, mounting `Data/` writable, and providing Docker CLI/Compose access to the existing local server container through the host Docker socket. The server container remains separate.

Configure `DOCKER_GID` in the root `.env` to the host Docker group ID. The production compose file mounts the Docker socket the same way, which makes the panel root-equivalent on the host (see [Security](../../Docs/Security.md)). SSH targets remain the option for a server running on another host.

## Validation, secrets, and precedence

Startup validation rejects blank commands, unknown target references, invalid SSH ports, and incomplete SSH targets. Production defaults contain no targets, commands, or server instances.

ASP.NET loads [`appsettings.json`](../../appsettings.json), then the environment file, followed by environment variables and command-line settings. Later sources override earlier values. Keep private keys, passphrases, passwords, and real production credentials out of committed configuration; use mounted secret files or a secret manager.
