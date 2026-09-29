# RemoteOperations module

The panel's **only gateway for running commands** on a game-server host. Other modules
(ServerManagement, FileManager, ModManager, ServerLogs, Analytics) never start processes
themselves. They ask this module to run a named, pre-approved operation, either as a local process
or over SSH. Nothing else in the panel can execute code on the host.

It is mostly a library for other modules (`IRemoteOperationsService`), plus one HTTP route.

## Features

| Feature | What it does |
|---|---|
| **Allowlisted operations** | Operations are declared in configuration (`Remote:Commands`). An operation is a name plus one executable, optional fixed arguments, a target, and an optional user. A name that is not declared is refused (`RemoteOperationNotAllowedException`, `404` over HTTP). There is no way to submit an arbitrary command line. |
| **Named execution targets** | `Remote:Targets` defines where operations run: `Local` (a process inside the panel's container, typically reaching the game server through the mounted Docker socket) or `Ssh` (a remote host). Each operation is bound to exactly one target. |
| **Run and collect** | `ExecuteAsync` runs an operation to completion and returns `{ exitStatus, standardOutput, errorOutput }`. A non-zero exit is logged as a warning and returned to the caller rather than thrown. |
| **Stream output** | `StreamAsync` yields output as it arrives (`StandardOutput`, `StandardError`, then `Completed` with the exit status). It powers live consoles and log tails, and stops when the caller cancels. |
| **Binary streams** | `ExecuteBinaryAsync` pipes a caller-supplied stream to the command's stdin and/or its stdout into a caller-supplied stream. FileManager uses it for uploads and downloads without buffering whole files in memory. |
| **Safe argument passing** | Callers append dynamic arguments separately from the configured command. Local mode passes them as an argument list (no shell). SSH mode single-quotes every token before building the command line. Values are never interpreted as shell syntax. |
| **Run as another user** | A command may set `User`. It is then run through `su -s /bin/sh -c …` locally or remotely, with the command and arguments passed as positional parameters. |
| **SSH host-key pinning** | An SSH target must have `HostKeyFingerprintSha256`. The connection is trusted only if the server's key matches it exactly (compared in constant time). Otherwise the connection fails. Connect timeout is 15 s. |
| **Key-based SSH auth** | SSH uses a private key file (`/run/secrets/ssh_private_key` by default) with an optional passphrase file. Passwords are not supported. |
| **Cancellation** | Cancelling the request kills the local process tree, or stops the SSH command, so aborted uploads and closed console streams do not leave work running. |
| **Workspace placeholder** | `%WorkspacePath%` in a command or its arguments is replaced with the panel's configured workspace path at startup, so one configuration works across machines. |
| **Configuration validation** | The app refuses to start if a target or command is malformed: an SSH target lacking host, port (1–65535), username, key file or fingerprint, or a command that names an unknown target or has no executable. |

## HTTP route

`POST /api/remote/{operation}` runs an allowlisted operation with **no** extra arguments and returns
`{ exitStatus, standardOutput, errorOutput }`. It is **admin-only** and needs the anti-forgery token.
An unknown operation is `404`. A missing or invalid target configuration is `503`.

Dynamic arguments are never accepted from HTTP. Only server-side module code can add them.

## Configuration

```json
"Remote": {
  "Targets": {
    "local": { "Mode": "Local" },
    "remote-main": {
      "Mode": "Ssh",
      "Host": "203.0.113.10",
      "Port": 22,
      "Username": "panel",
      "PrivateKeyFile": "/run/secrets/ssh_private_key",
      "PrivateKeyPassphraseFile": null,
      "HostKeyFingerprintSha256": "SHA256:…"
    }
  },
  "Commands": {
    "demo-local-data-files": {
      "Target": "local",
      "Command": "python3",
      "Arguments": ["%WorkspacePath%/Server/Development/file-manager.py", "…"],
      "User": null
    }
  }
}
```

Environment overrides use the target or command name, for example `Remote__Targets__remote-main__Host`.
Do not configure a shell pipeline as `Command` for an operation that accepts dynamic arguments.
Full examples for the game-server lifecycle operations are in
[ServerManagement](../ServerManagement/README.md). See also
[Configuration](../../Docs/Configuration.md) and [Security](../../Docs/Security.md).

## Layout

```text
Endpoints/       RemoteOperationsRoutes, RemoteOperationsEndpoints (exception -> HTTP mapping)
Services/        RemoteOperationsService (lookup, dispatch, logging)
Persistence/     ConfigurationRemoteCommandRepository (Remote:Commands + Remote:Targets -> definitions)
Infrastructure/  IRemoteConnection, LocalRemoteConnection (process), SshRemoteConnection (SSH.NET),
                 RemoteConnectionFactory (picks by target mode), RemoteOperationsModule (DI, validation, placeholders)
Tests/           Unit (repository, argument quoting, local process, service) and Feature (endpoint)
```

## Notes

- The panel container mounts `/var/run/docker.sock`, so a `Local` operation can control Docker
  on the host. Treat `Remote:Commands` as root-equivalent configuration. Anyone who can edit it
  can run arbitrary code on the host.
