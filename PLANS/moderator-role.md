# Moderator accounts — implementation plan

Status (2026-09-29): **implemented**. Decisions on the open questions:
1. Player IP addresses are blanked in `/connections` for non-admins.
2. Moderators may mute problem signatures (server logs hold nothing sensitive).
3. Moderators get a Settings page with their own password change and 2FA.
4. The Overview's log-problems item keeps its link to Server logs for moderators.

Goal: add a second account type, **Moderator**, next to the existing admins.
Moderators can:
- see the Overview (dashboard) page, read-only. They can't click tiles or
  "Needs attention" items through to detail pages.
- use the Server logs page (`/server-logs`).

Everything else is admin-only: the other menu items, Quick actions
(restart, open folders), the console, files, mods, analytics, the panel log,
settings and user management.

The server enforces this. Hiding things in the UI is only for convenience.

---

## 1. Current state

- Every browser route uses a plain `.RequireAuthorization()`, so any signed-in
  user can do everything. There are no roles and no policies.
- `AppDbContext` is `IdentityDbContext<IdentityUser>`, so the `AspNetRoles` and
  `AspNetUserRoles` tables already exist but aren't used. `AddIdentityCore`
  doesn't call `.AddRoles<IdentityRole>()`.
- The cookie principal is created by `SignInManager`. It carries role claims only
  when roles are registered.
- `/auth/session` returns `AuthenticatedUser(Id, Username)`, which has no role.
- The frontend has no notion of permissions. `App.tsx` mounts every route, and
  `NavigationMenu` and `QuickActions` show every item.
- The AutomationApi (`/api/v1`) uses its own API-key policy. It isn't affected
  by this change.

## 2. Design

### 2.1 Roles and policies

- Two Identity roles: `Admin` and `Moderator`. Each user has exactly one.
- Put a small shared primitive in Core (`Core/Authorization/PanelRoles.cs`,
  `PanelPolicies.cs`): role names plus two policy names. It's cross-module
  (every module's routes use it) and contains no feature logic.
  - `PanelPolicies.Admin`: authenticated **and** in role `Admin`.
  - `PanelPolicies.Staff`: authenticated **and** in role `Admin` or `Moderator`.
- **Fail closed:** in `AddAuthenticationModule`, set
  `AuthorizationOptions.DefaultPolicy` to the Admin policy. Every existing
  `.RequireAuthorization()` then becomes admin-only without further changes.
  Only the endpoints moderators need are opened explicitly with
  `.RequireAuthorization(PanelPolicies.Staff)`. A route added later is
  admin-only unless someone deliberately opens it.
- In `AddUsersModule`, add `.AddRoles<IdentityRole>()` so the claims factory
  puts role claims into the auth cookie.

### 2.2 Endpoints opened to moderators (`Staff`)

| Route | Why |
|---|---|
| `GET /auth/session`, `POST /auth/refresh`, `POST /auth/logout` | Session basics |
| `POST /auth/2fa/setup`, `/enable`, `/disable` | Moderators can protect their own account (see open question 3) |
| `GET /api/servers`, `GET /api/servers/{id}/status` | Server selector and status (`ServerContext`) |
| `GET /api/servers/{id}/metrics` | Overview CPU, memory, disk and uptime |
| `GET /api/servers/{id}/connections` | Overview "who is online" (see open question 1) |
| `GET /api/analytics/{id}/health` | Overview sparkline and lag spikes |
| `GET /api/analytics/{id}/disconnects` | Overview "drops" attention item |
| `GET /api/servers/{id}/server-logs/*` (all GETs) | Server logs page and Overview problem summary |
| `POST`/`DELETE .../server-logs/saved-searches` | Saved searches are already per user |

These stay admin-only through the default policy:
- lifecycle start/stop/restart, console commands, and the console SSE stream
  `/logs`
- files, mods and remote operations
- all other analytics routes
- panel logs (`/api/logs`), users, and `/auth/login-logs`
- `PUT .../signatures/{id}/mute`. Muting changes the shared Problems view for
  everyone (see open question 2).

Nothing changes on anonymous routes (`/auth/csrf`, `/auth/login`,
`/auth/login/2fa`, `/health`).

### 2.3 Role in the session and the user API

- `AuthenticatedUser` gets a `Role` (`"Admin"` or `"Moderator"`).
  `IdentityAuthenticationSession.GetCurrentUserAsync` reads it with
  `UserManager.GetRolesAsync`. Login, 2FA login, session and refresh then all
  return it.
- Users module:
  - `CreateUserRequest(Username, Password, Role)`, with the role validated
    against `PanelRoles`. A bad role throws the domain exception
    `InvalidRoleException`, which the endpoint turns into a 400.
  - `UserResponse` gets `Role`.
  - New `PUT /api/users/{id}/role` (Admin, antiforgery) changes a user's role.
  - Business rules in `UserService`:
    - You can't demote yourself.
    - You can't demote or delete the **last admin** (`LastAdminException` → 409).
  - Changing a role calls `UpdateSecurityStampAsync`. With the
    security-stamp validator set to a short interval (for example 1 minute),
    the user's existing cookie picks up the new role, or is rejected, soon
    after the change.
- `AdminSeeder`: create both roles if they're missing, and give the configured
  seed admin the `Admin` role when it's created.

### 2.4 Migrating existing accounts

- Add an EF migration `AddPanelRoles`. It makes no schema change; the tables
  already exist. It inserts the two roles and assigns `Admin` to **every
  existing user**, which matches what they can do today. Because it's a
  one-time migration, a later bug can't silently promote users. A seeder
  rule like "user without a role becomes admin" could.
- Existing cookies have no role claim, so after the deploy existing sessions
  get 403 until the user refreshes or signs in again. To avoid that, the
  frontend treats a 403 from `/auth/session` or `/auth/refresh` as "sign in
  again". Short security-stamp validation also rebuilds the principal
  automatically.

### 2.5 Frontend

- **Session context.** `App.tsx` already fetches the session. Keep the
  returned user and expose it through a new `context/SessionContext.tsx` with
  `useSession()`, which returns `{ user, isAdmin }`. Update the user on login,
  2FA login and refresh.
- **Routes.** Moderators get only `/` and `/server-logs`. Any other path
  redirects to `/`. Admin routes stay as they are.
- **`NavigationMenu`.** Define the items as data with an `adminOnly` flag and
  filter them by role. Moderators see Overview and Server logs.
- **`AppShell`.** Don't render `QuickActions` or its divider for moderators.
  Also skip `UploadProvider` and `UploadQueuePanel` for moderators; they can't
  upload.
- **`DashboardPage`:**
  - `Tile` gets an `interactive` flag. For moderators it renders a plain
    `<div>`: no `Link`, no chevron, no hover.
  - "Needs attention" items render without a link for moderators. The one
    exception is `logProblems` → `/server-logs?tab=problems`, which is a page
    moderators can use (open question 4).
  - Skip the `loginsApi.recent` poll and the failed-logins item for
    moderators. The endpoint is admin-only and panel login history isn't their
    business.
- **Server logs page.** Hide the mute button for moderators if mute stays
  admin-only. Everything else works unchanged.
- **Settings → Users** (admin only):
  - a Role column
  - a role picker in the create form (default: Moderator)
  - a role change action; server errors show as toasts
- **i18n.** Add every new string to both `en.ts` and `ru.ts`: role names,
  column header, picker, and the last-admin and self-demotion errors.

### 2.6 Tests

- **Users unit tests:**
  - create with each role
  - invalid role rejected
  - role change updates the security stamp
  - can't demote yourself
  - can't demote or delete the last admin
  - listing includes roles
- **Users feature tests:**
  - role DTO mapping
  - `InvalidRoleException` → 400, `LastAdminException` → 409
  - `PUT /role` result mapping
- **Authentication tests:** session and login responses include the role.
- **Authorization wiring** (new Core test or host-level test):
  - build the endpoint data source and assert that every mapped browser route
    has either the default (Admin) policy or an explicit `Staff` policy, and
    that `Staff` appears only on the allowlisted routes in §2.2. This protects
    against a route being opened by accident.
  - check that the policies accept or reject principals with the right roles.

### 2.7 Docs to update in the same change

- `Docs/Security.md`: new "Authorization" section covering roles, policies,
  fail-closed default, moderator allowlist, last-admin rule, and
  security-stamp revalidation. Also fix the "no role is required" sentence in
  the Login log section.
- `Docs/Architecture.md`: Authentication and Users module descriptions; the
  role primitive in Core.
- `Docs/Module-Development.md`: new routes are admin-only by default; opening
  one to moderators needs `PanelPolicies.Staff` and an update to the
  allowlist test.
- `Docs/Development.md`: frontend session context, role-based navigation,
  non-interactive dashboard for moderators.
- `Docs/Operations.md`: after the deploy, existing sessions may need to sign
  in again; how to create a moderator.

## 3. Rollout

1. Backend: Core primitives, roles, policies, migration, session role, Users
   API, and the Staff allowlist on routes. Add tests. Run `dotnet build` and
   `dotnet test`.
2. Frontend: session context, routes, menu, shell, dashboard, users UI, i18n.
   Run `npm run build`.
3. Docs.
4. Deploy: rebuild and restart **only the web panel container**. The game
   server (Vintage Story) container is **never restarted**; players are
   online. The migration runs on web container startup and doesn't touch the
   game server.
5. Verify as admin: everything works as before. Create a moderator and sign in
   as them. Check that the menu and dashboard are reduced, the server logs
   page works, and direct calls to admin routes (for example
   `POST /api/servers/{id}/restart` and `GET /api/servers/{id}/files`) return
   403.

## 4. Open questions

1. **Player IP addresses.** `/connections` returns each client's
   `RemoteAddress`. The Overview only shows names, but a moderator could read
   the IPs from the API response. Options: strip addresses for non-admins in
   the endpoint (proposed), or accept it.
2. **Muting problem signatures.** Admin-only (proposed), or allowed for
   moderators?
3. **Moderator account self-service.** Moderators have no Settings page. Should
   they get a minimal "My account" dialog for 2FA and password change, or do
   admins manage their accounts?
4. **"Needs attention" → Problems link.** Keep that single link for
   moderators because it points to a page they can access (proposed), or
   remove every link?
