# Architecture

## Style

AlegacyWebPanel is a modular monolith. It is deployed as one ASP.NET Core application, while feature ownership is separated into modules. Each production module is a separate .NET project, but modules are still compiled and deployed together; a module is not currently a separately deployed process.

The dependency direction is:

```text
Core
  ↑
Modules
  ↑
Program.cs (composition root)
```

`Core` may be referenced by modules. Modules must not put feature-specific behavior into Core. `Program.cs` wires modules together and should not contain feature behavior.

## Request flow

```text
HTTP request
    -> endpoint coordinator
    -> service interface
    -> service implementation
    -> repository interface
    -> persistence or configuration adapter
```

Endpoints translate HTTP DTOs into application commands, invoke service interfaces, translate domain failures into HTTP exceptions, and map application results to HTTP responses.

Services own business rules. They may depend on repositories and other service interfaces, but never on HTTP requests, HTTP responses, endpoint types, or controllers.

Repositories expose data access. They do not contain business rules. Persistence models stay inside persistence adapters and are mapped to module DTOs or application models.

## Core

The Core project contains cross-module capabilities only:

- shared abstractions such as secret reading;
- redacted secret values;
- domain and HTTP exception primitives;
- shared exception handling;
- health endpoint infrastructure;
- Core tests.

Core is an independent project at `Core/AlegacyWebPanel.Core.csproj`.

Each feature module is also an independent project under its module directory. Module projects reference Core, and the application project references the module projects. This makes the dependency direction enforceable at compile time and keeps module-specific package dependencies out of the host project.

## Current modules

### Authentication

Owns the single-admin authentication flow, cookie session discovery and refresh, CSRF token issuance, cookie session handling, Identity persistence, password policy, account bootstrap, authentication DTOs, domain exceptions, and authentication tests. The browser uses the cookie session; it does not store access or refresh tokens.

The module also owns the login-log audit feature. Login endpoints record every attempt (username, IP address, outcome, UTC timestamp) into the `LoginEvents` table hosted by the Users module's `AppDbContext` — the same persistence split used for trusted 2FA IPs. The flow is `Login endpoint -> ILoginLogService -> ILoginEventRepository -> AppDbContext (LoginEvents)`, and the dashboard reads recent attempts through `GET /auth/login-logs`.

### RemoteOperations

Owns named local/SSH execution targets, allowlisted target-bound operations, safe structured argument transport, cancellable output streaming, per-target SSH host-key verification and private-key loading, remote-operation DTOs, domain exceptions, and remote-operation tests.

### ServerManagement

Owns configured game-server profiles, lifecycle/status orchestration, per-server lifecycle coordination, console-command validation, metrics mapping, safe log events, server HTTP contracts, domain exceptions, and server-management tests. It depends only on the public `IRemoteOperationsService` contract for local or SSH execution.

Server requests follow this module-to-module flow:

```text
HTTP request -> ServerManagement endpoint -> ServerManagement service
             -> IRemoteOperationsService -> local process or SSH transport
             -> configured target-side wrapper
```

### FileManager

Owns remote file management capabilities including directory listings, file downloading and uploading, text editor content loading and saving, basic mutations (mkdir, rename, move, delete), and long-running background tasks (zip compression and unzip extraction) with in-memory task status monitoring. It communicates using `IRemoteOperationsService` to execute the python-based `file-manager.py` target-side helper. It applies path containment validation, NUL-byte binary checking, and entry-level zip-slip filters for system security.

### Logging

Owns persistent application log capture and the authenticated log viewer. `AddLoggingModule` registers a global `ILoggerProvider` (`SqliteLogProvider`) so every `ILogger<T>` call in any module or in Core is captured. Logged entries are formatted by `SqliteLogWriter`, enqueued into a bounded in-memory channel, and written by the background `LogWriteWorker` in batches to a dedicated SQLite database (`/var/lib/alegacy/data/logs.db`). The worker also prunes retained entries and flushes remaining entries on graceful shutdown. The viewer exposes paged, filterable log queries plus level counts, source lists, an error feed, and a clear action.

The capture path is:

```text
ILogger<T> -> SqliteLogProvider -> SqliteLogWriter -> bounded channel
            -> LogWriteWorker -> ILogRepository -> LogDbContext (SQLite)
```

The viewer follows the standard flow: `HTTP request -> Logging endpoint -> ILoggingService -> ILogRepository -> LogDbContext`.

The log database is intentionally separate from the authentication database so log write contention or log storage failures cannot affect authentication. Structured state is serialized with size bounds, `SecretValue` values are redacted, and the buffer drops entries without blocking when full.

## Persistence and state

Authentication uses SQLite through the Authentication module’s persistence layer. The database is mounted at runtime through the `app_data` Docker volume.

ASP.NET Core Data Protection keys are persisted separately in the `data_protection_keys` Docker volume so authentication cookies survive container recreation.

## Composition root

`Program.cs` is responsible for creating the host, registering Core and module services, configuring Data Protection, running startup seeding, adding middleware, and mapping Core and module endpoints.

It must not implement module business rules or access module persistence directly.
