# Security

## Authentication

The Authentication module uses ASP.NET Core Identity with SQLite. Store password hashes only; never store plaintext passwords. Password policy and lockout behavior belong to the Authentication module.

Authentication cookies are HTTP-only and use strict SameSite behavior. Production cookies must be secure and the application must be served behind HTTPS.

State-changing cookie-authenticated requests require an ASP.NET antiforgery token in the `X-CSRF-TOKEN` header. The frontend obtains a token from `GET /auth/csrf` and sends it with login, refresh, logout, and remote-operation requests. Authentication failures from API endpoints return `401` or `403` responses rather than redirects.

## Authorization

Every browser account has exactly one role, `Admin` or `Moderator`. Roles are Identity roles in `alegacy.db`; the role names and policies are shared primitives in `Core/Authorization`.

- **Fail closed.** `PanelPolicies.Configure` makes the `Admin` policy the default, so a plain `RequireAuthorization()` is admin-only. A route is opened to moderators only by `RequireAuthorization(PanelPolicies.Staff)` (Admin or Moderator). `PanelPolicies.SignedIn` (any signed-in cookie) is used only for session, refresh and logout.
- **Group policies are combined with endpoint policies** (all must pass). A moderator route therefore needs its own `Staff` group; adding `Staff` to an endpoint inside an admin group does not open it. The ServerManagement and Analytics feature tests assert the exact set of moderator routes.
- **Moderator routes:** `GET /api/servers`, `GET /api/servers/{id}/status|metrics|connections`, `POST /api/servers/{id}/actions/gamemode|teleport|warn|kick|ban|hardban|unban|landclaim|allowcharselonce`, `GET /api/analytics/{id}/health|disconnects`, every `/api/servers/{id}/server-logs/*` route (including mute and saved searches), and the own-account routes `/auth/password`, `/auth/2fa/status|setup|enable|disable`. Everything else — lifecycle, console, files, mods, remote operations, other analytics, panel logs, users, login history — is admin-only.
- **Actions:** moderators never get the raw console. Each action has its own endpoint handled by `IServerActionsService` (ServerManagement module), which builds the command from validated parts and sends it through `IServerManagementService.SendCommandAsync`: player names must match letters, digits and `_ . -` (1–32, no spaces, so they can't add arguments); game mode is 0–2; teleport coordinates are finite numbers with a server-chosen prefix (none, `=` or `~`); extra land-claim areas are 0–9999 and extra allowance any non-negative int; the warn reason is required; kick/ban reasons are optional, at most 200 characters, with no control characters. Examples: `/gamemode Flajakay 1`, `/tp Flajakay =100 120 =-50`, `/ban Flajakay griefing`.
- **Player addresses** in `/connections` are blanked for non-admins; moderators see names and connection quality.
- **Role changes** rotate the user's security stamp. The cookie is revalidated against the database every minute (`SecurityStampValidatorOptions.ValidationInterval`), so a role change or deletion reaches an open session within about a minute. The frontend also refreshes the cookie on load so its role claims are current.
- Admins cannot change their own role, and the last admin cannot be demoted or deleted.
- The frontend hides admin pages, Quick actions and dashboard drill-down links from moderators. That is convenience only; the backend is the boundary.

## Audit trail

Privileged and state-changing routes are recorded in the audit trail (`Modules/Audit`): who (username, role, client IP), what (category, action, server, target, details), when, and whether it succeeded. Failed and refused attempts are recorded with the reason. Sign-in attempts stay in the [login log](#login-log).

- **Every new mutating route must call `.Audited(...)`** (see [Module development](Module-Development.md)). Reads are not recorded.
- **Secrets are not stored.** Request fields whose name contains `pass`, `secret`, `token`, `key`, `code`, `otp`, `cookie` or `content` are dropped before storage, values are cut at 200 characters, and console commands that mention a password, token, secret or API key keep only the command name. Unlike the application log, the audit trail does store console command text and file paths, because that is the point of it.
- **Admin-only and append-only.** `GET /api/audit` and `/api/audit/facets` use the default admin policy. No route creates, edits or deletes an entry. Entries leave only through retention pruning (`AuditTrail:RetentionDays`).
- **Separate store.** `audit.db` is its own SQLite file in `app_data`, so log clean-ups and authentication data cannot affect it.
- **Fail open for the action.** If the store is unavailable the action still runs and the error goes to the application log. An outage therefore leaves a gap in the trail.
- Automation API calls are recorded as `automation-api`. The client IP is taken from forwarded headers (see the note in the Authentication README), so it is only as trustworthy as that setting.

## Login log

The Authentication module records every login attempt — successful and failed — into the `LoginEvents` table in the authentication database (`alegacy.db`). Each entry stores only the attempted username, the client IP address, the outcome, and a UTC timestamp. Passwords, 2FA codes, and session tokens are never stored or logged. Entries older than `Authentication:LoginLog:MaxRetainedDays` (default 90) are pruned on write, bounding audit-table growth.

The read endpoint `GET /auth/login-logs` requires authentication and returns a paginated, newest-first list. The Settings → Security tab lists the history, and the Overview page counts recent failed attempts. The endpoint is admin-only.

## Secrets

Never commit passwords, SSH private keys or passphrases, API tokens, credential-bearing connection strings, Data Protection key material, or local secret files. Development and Compose secrets are supplied as files under `secrets/`, which is ignored by Git. Production should use the platform’s secret-management facility where available.

## SSH

RemoteOperations must use a dedicated, least-privileged remote account and key-based authentication. The SSH host-key SHA-256 fingerprint must be configured; unknown or changed host keys must fail closed.

Each allowlisted operation references a named execution target. SSH host, user, key paths, and fingerprint are resolved exclusively from that target's trusted backend configuration. Operations on different SSH targets must not reuse another target's connection settings implicitly.

Only commands present in the configured allowlist may be executed. Dynamic values are carried as structured arguments: local execution uses `ProcessStartInfo.ArgumentList`, and SSH execution POSIX-quotes every command token. Do not concatenate request data into shell commands. Configured commands that accept dynamic arguments must identify one executable or wrapper, with trusted fixed arguments configured separately.

Do not expose a general-purpose terminal through the web panel. Remote output and logs must not disclose credentials or private key material.

All ServerManagement endpoints require authentication, and lifecycle and console-command requests also require antiforgery validation. ServerManagement accepts only configured server IDs. Console commands have a bounded length and reject control characters; they are forwarded as one argument to the configured console operation. Log stderr and transport failures are converted into stable events that do not include remote paths or credentials.

## File Manager

File operations must validate writable permissions on the requested root configuration, blocking any mutations (mkdir, rename, move, delete, upload, compress, extract) on read-only roots.

Security controls:
- **Path Traversal Containment**: Enforces strict relative path checks in C# endpoints (blocking `..`, absolute paths, control characters, or NUL bytes) and matches absolute paths (`os.path.realpath`) against the allowed root boundaries in python.
- **Binary-Safe Validation**: Scans file streams (the first 8KB) for NUL (`\0`) bytes to reject binary text editor loads with `415 Unsupported Media Type`.
- **Bounded Stream Guard**: Restricts max size on text loads and multipart uploads using a counting stream wrapper (`BoundedStream`), throwing `FileTooLargeException` if limits are exceeded.
- **Zip-Slip Boundary Checks**: Iterates through archive member targets, verifying they resolve strictly within the target extraction directory boundary.
- **Feedback Loops Prevention**: Excludes the target archive itself from walking directory contents during compression to prevent infinite growing loops.
- **Hidden internal folders**: `.trash` and `.panel-tmp` are hidden from listings and search; the C# path validation rejects them as a first segment and the helper refuses them, so they are only reachable through the trash and download-archive endpoints.
- **Inline image previews**: `download?inline=true` is limited to raster types (png, jpg, jpeg, gif, webp, bmp, ico; anything else gets 415) and is served with its exact content type, `Content-Disposition: inline`, and `Content-Security-Policy: default-src 'none'; img-src 'self'; sandbox`. SVG is deliberately excluded because it can carry script; SVGs are only downloaded or opened as text.
- **Live-server guard**: changes under a root's `ProtectedPaths` (world saves, mods, configs) require an explicit acknowledgement in the UI while the game server is online. This is a UI safeguard against accidents, not an authorization boundary.

## Server logs

- The indexer reads game logs only through the read-only `server-logs.py` helper. It allowlists `server-{main,audit,debug,chat}.log` in `Logs/` and `Logs/Archive/` (one folder deep), and refuses `..`, symlinks, and paths that resolve outside `Logs/`. The panel never sends a path the helper did not list.
- Chat logs contain players' conversations and are indexed only when `IncludeChat` is enabled for a server.
- Search text reaches SQLite only as parameters. Free words reach FTS5 only as quoted terms, so query syntax cannot be injected, and no user-supplied regular expression runs on the server.
- CSV exports prefix cells starting with `=`, `+`, `-` or `@` so spreadsheet apps do not evaluate them. Exports are capped at `MaximumExportRows`.
- Saved searches are stored per signed-in user; the owner check happens in SQL on list and delete.
- Audit entries reveal player activity and coordinates. All endpoints require the `Staff` policy (admins and moderators), and muting a signature requires antiforgery validation.

## Automation API

The AutomationApi module exposes `/api/v1` routes for machine clients: server discovery and status, path-confined file listing, download, and upload, and server start/stop/restart. It authenticates every request with the `X-Api-Key` header.

Security controls:

- **Mounted secret**: The key is read from `AutomationApi:KeyFile` (default `/run/secrets/api_key`) per request. The file is never read from request data, and the application fails startup when the API is enabled without an existing secret file.
- **Timing-safe comparison**: The presented key and configured secret are hashed with SHA-256 and compared with `CryptographicOperations.FixedTimeEquals`, so the result neither leaks length nor depends on an early-exit character comparison. The key is never logged.
- **Disabled by default**: `AutomationApi:Enabled` defaults to `false`. When disabled, the routes are not mapped and return `404`. Compose enables the API only together with the mounted secret.
- **Scheme isolation**: The `/api/v1` group requires a policy that pins the `AutomationApiKey` authentication scheme. A browser cookie alone cannot authorize these routes, and the API key does not authorize browser routes.
- **No CSRF surface**: API-key requests are not cookie-authenticated, so the routes do not require antiforgery tokens. Existing cookie-authenticated routes keep their CSRF requirements.
- **Reused module rules**: File access stays subject to FileManager root configuration, path containment, writable-root checks, and size limits; lifecycle operations stay subject to ServerManagement operation allowlists and per-server lifecycle coordination. The API adds no bypass.

## Container

The development Compose service binds the application to localhost. The production Compose service publishes only the Caddy gateway on ports 80 and 443; the ASP.NET service is private to the application network. Both services use read-only root filesystems, drop Linux capabilities, enable `no-new-privileges`, and keep writable state in named volumes. Production services restart unless stopped.

The gateway forwards the original host and HTTPS scheme to ASP.NET. ASP.NET trusts forwarded headers only because the production web service is not published and is reachable through the private application network. Keep that network private if the deployment is extended.

The application network is `internal`, so the ASP.NET service has no direct internet access. Its only outbound path is the `egress-proxy` service (Squid, `egress-proxy/squid.conf`), reached through `HTTPS_PROXY`. The proxy allows only `CONNECT` on port 443 to `mods.vintagestory.at` and `moddbcdn.vintagestory.at`, and it doesn't cache. TLS stays end to end, so the ModManager's HTTPS and trusted-host checks still apply. To allow another outbound host, add it to the proxy's `allowed_hosts` ACL; don't attach the web service to a non-internal network.

These settings do not replace TLS, host hardening, least privilege, patching, monitoring, or network controls.

## Search engine indexing

The panel is a private administration tool and must not appear in search-engine indexes. The production gateway publishes the SPA on public ports, so three complementary guardrails apply to every response:

- `robots.txt` (in `frontend/public/`) declares `Disallow: /` for all crawlers.
- The SPA shell sets `<meta name="robots" content="noindex, nofollow">` in `frontend/index.html`.
- The Caddy gateway sends the `X-Robots-Tag: noindex, nofollow` response header on all routes.

Keep all three in place; dropping any of them re-exposes the panel to indexing.

## Error handling

Domain exceptions must not expose secrets or internal stack traces. Endpoint translation should return stable, useful problem details without leaking authentication state, filesystem paths, private key contents, or remote command credentials.

## Logging

The Logging module captures `ILogger<T>` output from every module into a dedicated SQLite database in the `app_data` volume, separate from the authentication database. Captured structured state is serialized with a size bound; any logged `SecretValue` is stored as its redacted form. Buffer overflow drops new entries rather than blocking requests, and retention pruning bounds storage growth.

Log messages must never contain credentials, tokens, secrets, or private-key material. Modules log identifiers and outcomes only: operation names (never arguments or remote paths), server and root IDs (never filesystem paths), and usernames. Remote output and console command text are never logged.

The viewer endpoints under `/api/logs` require authentication, and the clear action additionally requires antiforgery validation. Responses expose only the recorded events; storage failures surface as a stable `503` without internal error text.
