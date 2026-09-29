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

The browser frontend supports login, session restoration, logout, session refresh, health checks, configured server selection, lifecycle operations, console commands, metrics polling, player connection diagnostics, SSE log streaming, and the application log viewer. Server HTTP and SSE transport is isolated in `frontend/src/api/servers.ts`; the log viewer API is isolated in `frontend/src/api/logs.ts`. Pages and contexts consume typed methods rather than calling `fetch` directly. Data tables that can grow beyond a handful of rows use the shared `frontend/src/components/ui/DataTable.tsx`, which provides text search, click-to-sort columns (missing values always last), pagination with a page-size selector, and an optional mobile card layout; define columns with a `sortValue` so every column users would compare is sortable. The Overview page (`pages/DashboardPage.tsx`) shows server status and uptime, players online with a 24 h sparkline, CPU/memory with last-hour lag spikes, disk usage, who is online, and a "Needs attention" list; it only polls while the tab is visible. The Server logs page (`pages/ServerLogsPage.tsx`, components in `components/server-logs/`) searches the game server's logs. Its search box is the single source of truth: facet and in-line clicks add or remove `key:value` tokens (`lib/logQuery.ts`), and the box, time range and tab are kept in the URL. `LogSearchBox` draws the input's text in a highlighted overlay (the real input text is transparent), so the input and overlay must keep identical font, padding and horizontal scroll. Its other tabs are Problems, Players (per-player audit breakdown and "who was here" location lookup) and Startups (one row per server start with mod changes). The Overview page's "Needs attention" list links to Problems when new error or warning types appeared since the last server start. The panel login history lives in Settings → Security (`components/settings/LoginHistory.tsx`). CodeMirror editors must receive a stable `onChange` (`useCallback`): `@uiw/react-codemirror` reconfigures the editor whenever that prop changes, which closes the search panel on every parent re-render. Theme colours are remapped in `index.css`: `slate-500` is secondary text and must stay at WCAG AA contrast (≥4.5:1 on panels); use `slate-600` and darker only for decoration. The `button, input, select, textarea { font: inherit; }` rule lives in `@layer base`, so Tailwind text-size classes on those elements apply; headings (`h1`–`h6`) are still forced to the serif font by an unlayered rule, so use a `div` with `role="heading"` for small sans-serif section labels. `.scroll-fade-x` fades the right edge of horizontally scrolling rows on phones. On the Server logs page, results carry a separator row for each day (times alone are ambiguous), the facet panel collapses below `lg`, and Problems shows 30 signatures at a time. The signed-in user and role come from `context/SessionContext.tsx` (`useSession()`, filled from `/auth/session` in `App.tsx`). Moderators get only the Overview, Server logs, Actions and Settings routes. The Actions page (`pages/ActionsPage.tsx`, for admins and moderators) lists command cards; each opens a dialog (game mode, teleport, warn, kick, ban, hard ban, unban, land claims, class re-select; player pickers are typeable dropdowns from `components/ui/Select.tsx`, and ban and hard ban also accept offline names) that calls a dedicated endpoint, never the raw console, and closes with a success toast; `NavigationMenu` marks admin-only items with `adminOnly`, `AppShell` hides Quick actions, the Overview renders tiles and "Needs attention" items without links (except the log-problems item, which opens Server logs), and Settings shows only the user's own 2FA and password change. These checks shape the UI only; the backend enforces access (see [Security](Security.md#authorization)). Tauri IPC and desktop file dialogs are not used by this frontend. New browser features should add HTTP contracts and backend module endpoints rather than importing desktop-specific code.

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
