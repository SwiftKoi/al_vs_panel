# Project documentation

This directory is the maintained technical documentation for AlegacyWebPanel.

## Documents

- [Architecture](Architecture.md) — system structure, dependencies, request flow, and current modules.
- [Module development](Module-Development.md) — module layout, manifests, boundaries, registration, and implementation rules.
- [Testing](Testing.md) — Unit and Feature test responsibilities, naming, and commands.
- [Security](Security.md) — authentication, secrets, SSH, container, and command-execution requirements.
- [Configuration](Configuration.md) — configuration sources, secret files, volumes, and environment behavior.
- [Development](Development.md) — local workflow, build commands, and change checklist.
- [Operations](Operations.md) — Docker Compose usage, deployment preparation, persistence, and troubleshooting.

## Module READMEs

Each module documents its own features, API, configuration and layout:

- [Authentication](../Modules/Authentication/README.md) — sign-in, sessions, two-factor, login history.
- [Users](../Modules/Users/README.md) — accounts, roles, first-admin seeding.
- [ServerManagement](../Modules/ServerManagement/README.md) — lifecycle, status, metrics, console, moderation actions, server profiles.
- [RemoteOperations](../Modules/RemoteOperations/README.md) — allowlisted local and SSH command execution.
- [FileManager](../Modules/FileManager/README.md) — file browsing, editing, transfers, trash, archives.
- [ModManager](../Modules/ModManager/README.md) — mod scan, ModDB updates, rollback.
- [Analytics](../Modules/Analytics/README.md) — player activity, disconnects, server health, connection quality.
- [ServerLogs](../Modules/ServerLogs/README.md) — search and analysis of the game server's logs.
- [Logging](../Modules/Logging/README.md) — the panel's own persistent application log.
- [Audit](../Modules/Audit/README.md) — append-only audit trail of who changed what, with an admin viewer.
- [AutomationApi](../Modules/AutomationApi/README.md) — API-key access for external automation.

Documentation must describe the repository as it should be maintained. Do not record conversational history or temporary implementation details here.
