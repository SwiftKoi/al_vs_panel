# ServerManagement configuration

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
```

`status` prints `online` or `offline` based on Docker Compose service state. Logs remain running and write log lines to standard output. Metrics print the JSON shape produced by `server-metrics.sh`.

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
        "MetricsOperation": "main-metrics"
      }
    ]
  }
}
```

`Id` is the stable API identifier and must be unique. `Name`, `Host`, `Port`, and `Location` are display metadata only; they never choose an execution target. Every operation field references `Remote:Commands`.

The frontend calls explicit routes such as `/api/servers/main/status`; there is no mutable global backend “current server.” This keeps different browser tabs and concurrent users isolated. Local storage remembers only the preferred ID.

## Local execution and containers

Use a local target only when the web-panel process can execute its wrapper with the required permissions. The development Compose configuration supports local execution from inside the web-panel container by mounting the development server checkout at the same absolute path used by `appsettings.Development.json`, mounting `Data/` writable, and providing Docker CLI/Compose access to the existing local server container through the host Docker socket. The server container remains separate.

Configure `DOCKER_GID` in the root `.env` to the host Docker group ID. This Docker-socket integration is development-only; SSH targets remain the option for a server running on another host.

## Validation, secrets, and precedence

Startup validation rejects blank commands, unknown target references, invalid SSH ports, and incomplete SSH targets. Production defaults contain no targets, commands, or server instances.

ASP.NET loads [`appsettings.json`](../../appsettings.json), then the environment file, followed by environment variables and command-line settings. Later sources override earlier values. Keep private keys, passphrases, passwords, and real production credentials out of committed configuration; use mounted secret files or a secret manager.
