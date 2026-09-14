# Development workflow

## Prerequisites

- .NET SDK matching the solution target framework.
- Docker Engine and Docker Compose for the supported runtime and test environment.
- Access to restore NuGet packages.

## Normal workflow

1. Read `AGENTS.md` and the relevant documents in `Docs/`.
2. Identify the module that owns the behavior. Shared, genuinely cross-module concerns belong in `Core`.
3. Add or update a service interface before implementing the service behavior.
4. Keep persistence behind a repository and keep endpoint code limited to transport coordination.
5. Add unit tests for service behavior and feature tests for endpoint behavior.
6. Update the module manifest and documentation when a module's public behavior or structure changes.
7. Build and test the affected projects, validate Compose configuration, and inspect the final diff.

Useful checks:

```sh
dotnet build
dotnet test Core/Tests/AlegacyWebPanel.Core.Tests.csproj
docker compose config --quiet
docker compose build
git diff --check
git status --short --untracked-files=all
```

The Dockerfile copies the host, Core, and production module project files before restore so Docker can cache package restore independently from source changes. When adding a production module, add its project file to that pre-restore copy section as well.

## Browser frontend

The browser frontend lives in `frontend/` and is intentionally separate from the ASP.NET Core modules. It communicates with the backend over same-origin HTTP requests and sends cookies with requests. During development, Vite runs on `http://localhost:5173` and proxies `/auth`, `/api`, and `/health` to the ASP.NET application. Use `npm run dev` for the local ASP.NET server on port `5053`, or `npm run dev:docker` for the Docker mapping on port `3000`.

Run the initial frontend slice with:

```sh
cd frontend
npm install
npm run dev
```

To use the Docker backend instead:

```sh
npm run dev:docker
```

The browser frontend supports login, session restoration, logout, session refresh, health checks, configured server selection, lifecycle operations, console commands, metrics polling, SSE log streaming, and the application log viewer. Server HTTP and SSE transport is isolated in `frontend/src/api/servers.ts`; the log viewer API is isolated in `frontend/src/api/logs.ts`. Pages and contexts consume typed methods rather than calling `fetch` directly. Tauri IPC and desktop file dialogs are not used by this frontend. New browser features should add HTTP contracts and backend module endpoints rather than importing desktop-specific code.

The frontend uses `i18next` and `react-i18next` for user-facing translations. English and Russian are supported. The language selector is available on the login page and in the authenticated shell; the choice is persisted in browser local storage, and Russian is the default for first-time visitors. New visible frontend copy must be added to both translation resources rather than embedded in components.

Run module test projects explicitly when changing a module. The full test suite runs with `docker compose --profile test run --rm --build tests`, using an isolated SDK image that does not write build artifacts into the workspace.

## Adding a module

Create a directory under `Modules/` with a `module.json` manifest and the layer directories described in [Module Development](Module-Development.md). Register its services, repositories, configuration, and endpoint mappings from the module registration entry point. Wire that entry point from `Program.cs`, because the application composition root owns module activation.

Create one production `.csproj` for the module. Reference Core from the module project, reference the module from `AlegacyWebPanel.csproj`, and keep module-specific package references in the module project. Do not add module source files directly to the host project.

The Logging module is an example of a module that registers cross-cutting infrastructure: its `AddLoggingModule` adds a global `ILoggerProvider` and a hosted background writer through DI while the host still only calls the single registration method. Keep `Program.cs` limited to that one call; it must not interpret log settings directly.

Every module must have separate `Tests/Unit` and `Tests/Feature` projects. Keep test data local to the module unless it is reusable Core test infrastructure.

## Database migrations

The primary application database (`AppDbContext`) uses Entity Framework Core migrations. To add new migrations when schema changes are introduced:

1. Ensure the `dotnet-ef` tool is installed:
   ```sh
   dotnet tool install --global dotnet-ef
   ```
2. Add a new migration from the repository root:
   ```sh
   dotnet ef migrations add <MigrationName> \
     --project Modules/Users/AlegacyWebPanel.Users.csproj \
     --startup-project AlegacyWebPanel.csproj \
     --context AppDbContext
   ```
3. Update the database: The application automatically applies migrations on startup via `db.Database.MigrateAsync()` during the seeder execution.

For unit and feature tests, `EnsureCreated()` is used to fast-path ephemeral in-memory or file-based database setups without running migrations.

## Repository hygiene

Do not commit `bin/`, `obj/`, database files, data-protection keys, SSH keys, secret files, or local environment files. Use the repository's ignore rules and inspect untracked files before pushing.
