# Vintage Story dedicated server container

This container runs the standard `VintagestoryServer.dll` directly through the
startup entrypoint on the .NET 10 runtime. The server installation and all
runtime data remain on the host and are bind-mounted as follows:

| Host path | Container path | Purpose |
| --- | --- | --- |
| `Server/` | `/server` | Main server files |
| `Data/` | `/data` | Configuration, saves, mods, logs, and caches |

On startup, the entrypoint runs `Server/install.sh` when `Server/INSTALLED` is
absent, then starts the server. The installer creates `INSTALLED` only after a
successful archive extraction.

The existing server configuration uses TCP port `42420`, which Compose
publishes on the same host port.

## Initial permissions

The container user must have the same numeric UID/GID as the owner of the
bind-mounted `Server/` and `Data/` directories, or it will not be able to
install the server or write runtime data. Configure both values in one place:

```sh
cp .env.example .env
# Set SERVER_UID and SERVER_GID in .env to the values from:
id -u
id -g
```

Compose reads `.env` automatically. The installer uses `umask 0002` and
normalizes the extracted server files so they remain writable by the owner and
group.

`VINTAGESTORY_ARCHIVE_URL` in the same file is required. It is the URL of the
ZIP archive that `Server/install.sh` downloads on first start, and the
container refuses to start without it. `manage.py setup` preserves any value
you set.

Give the configured identity ownership of both bind-mounted directories before
the first start if they are currently owned by another account.

## Operation

Build and start in the background:

```sh
docker compose up --build -d
```

Follow logs:

```sh
./server-control.sh logs
```

The control helper follows `Data/Logs/server-main.log`, which is the game
server log used by the web panel.

Send one console command without attaching to stdin:

```sh
./server-command.sh /stats
```

The helper writes to a private FIFO inside the container. It is not published
over the network. For remote administration, run the helper over SSH.

Manage the existing server container:

```sh
./server-control.sh start
./server-control.sh status
./server-control.sh restart
./server-control.sh stop
```

Docker sends `SIGINT` directly to the foreground .NET process and allows up to
45 seconds for a clean save before forcing termination. Stop and restart also
send a shutdown announcement through the command pipe before controlling the
Compose service. Normal lifecycle operations do not use `docker compose down`.

Read one metrics snapshot as JSON:

```sh
./server-metrics.sh
```

CPU, memory, and cumulative block read/write values come from
`docker stats --no-stream`. Data usage comes from `du` for the bind-mounted
`Data/` directory; filesystem capacity and free space come from `df` because
Docker Block I/O is activity, not storage capacity.

Additional server arguments can be appended with Compose `command`, for
example:

```yaml
    command: ["--some-option", "some-value"]
```

Do not move `Data/` into the image or Docker build context. Server upgrades can
be performed directly in `Server/` while the container is stopped; preserve
the Docker-aware `server.sh` when replacing the vendor server distribution.

## Scheduled midnight restart

The `ops/` directory contains a systemd service and timer for a daily restart
at `00:00` in the host's local timezone. The timer begins at `23:50`, sends
Russian announcements at 10, 5, and 1 minute before midnight through the same
private command pipe, and then performs a graceful Compose restart.

The timer deliberately uses `Persistent=false`: if the host is powered off at
23:50, systemd will not run a late countdown and restart at the wrong time
after boot. Install and enable the timer from the repository root:

```sh
python3 manage.py install-restart-timer
```

The installer generates the service with the current repository path and host
user/group, installs both units under `/etc/systemd/system`, reloads systemd,
and enables and starts the timer. It uses `sudo` when not run as root. Run the
command again after moving the repository or changing the account that owns and
runs the production game server.
