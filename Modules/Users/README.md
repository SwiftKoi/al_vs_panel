# Users module

Owns the panel's **accounts and roles** and the SQLite database (`AppDbContext`) that holds them.
Sign-in, sessions and 2FA are in the [Authentication](../Authentication/) module. That module reuses
this module's Identity stores and its `LoginEvents` and `UserTrustedIps` tables.

Routes live under `/api/users` and are **admin-only**. The frontend page is Settings → user management
(`frontend/src/components/settings`).

## Features

| Feature | What it does |
|---|---|
| **List users** | `GET /api/users` returns every account with `id`, `username`, `role` and whether 2FA is enabled. |
| **Create user** | `POST /api/users` with `{ username, password, role }`. Rejects blank fields, unknown roles (`400`) and duplicate usernames (`409`). The password must satisfy the password policy below. |
| **Change role** | `PUT /api/users/{id}/role` with `{ role }`. Rotates the user's security stamp, so their existing cookie picks up the new role at its next validation (about a minute). Setting the role they already have is a no-op. |
| **Delete user** | `DELETE /api/users/{id}`. Cascades to the user's trusted IPs. |
| **Roles** | `Admin` (full panel) and `Moderator` (read-only Overview and Server logs, plus their own password and 2FA). The roles are defined in `Core` (`PanelRoles`) and created on startup if missing. See [Security](../../Docs/Security.md#authorization) for which routes each role reaches. |
| **Lock-out guards** | You cannot delete your own account (`400`) or change your own role (`400`). The last remaining admin cannot be deleted or demoted (`409`). |
| **First-admin seeding** | On startup, `AdminSeeder` applies EF migrations, ensures the roles exist and, if no account with the configured username exists, creates it as an Admin with the password read from a secret file. It never overwrites an existing account. |
| **Password policy** | At least 12 characters with an upper-case letter, a lower-case letter, a digit and a symbol. Five failed sign-ins lock the account for 15 minutes. |

## Data

`AppDbContext` (EF Core, SQLite, migrations in `Migrations/`) extends ASP.NET Core Identity and adds:

| Table | Purpose | Used by |
|---|---|---|
| Identity tables (`AspNetUsers`, `AspNetRoles`, …) | accounts, hashed passwords, roles, 2FA keys | this module |
| `UserTrustedIps` | IPs allowed to skip the 2FA prompt, unique per user and IP | Authentication |
| `LoginEvents` | login history (success flag, IP, time) | Authentication |

The database file is set by `ConnectionStrings:DefaultConnection`, default
`Data Source=/var/lib/alegacy/data/alegacy.db` (the `app_data` volume).

## Configuration

| Setting | Meaning |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQLite path. The directory is created if missing |
| `Authentication:Admin:Username` | Seeded admin username, default `admin` |
| `Authentication:Admin:PasswordFile` | File with the seeded admin's password, default `/run/secrets/admin_password` (`secrets/admin-password` on the host). Startup fails if it is needed but missing |

The seed settings sit under `Authentication` because that section is shared with the Authentication
module. The seeded password is only used the first time. Changing the file later does not change an
existing account.

## Layout

```text
Endpoints/       UserRoutes (group, admin-only), UserEndpoints (exception -> HTTP status mapping)
Services/        UserService (rules and guards), AdminSeeder (migrate + roles + first admin)
Persistence/     AppDbContext, LoginEvent, UserTrustedIp
Migrations/      InitialCreate, AddPanelRoles
Exceptions/      UserAlreadyExists, UserNotFound, InvalidRole, SelfDeletion, SelfRoleChange, LastAdmin, …
Infrastructure/  UsersModule: Identity setup, password/lockout policy, DI
```

## Notes

- A new user has no session until they sign in. There is no invitation, e-mail or reset flow. An
  admin sets the initial password, and the user changes it in Settings.
- There is no "reset password" or "disable 2FA for another user" action yet. A locked-out user
  needs a database edit, or an admin can delete and recreate the account.
