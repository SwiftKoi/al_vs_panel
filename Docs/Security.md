# Security

## Authentication

The Authentication module uses ASP.NET Core Identity with SQLite. Store password hashes only; never store plaintext passwords. Password policy and lockout behavior belong to the Authentication module.

Authentication cookies are HTTP-only and use strict SameSite behavior. Production cookies must be secure and the application must be served behind HTTPS.

State-changing cookie-authenticated requests require an ASP.NET antiforgery token in the `X-CSRF-TOKEN` header. The frontend obtains a token from `GET /auth/csrf` and sends it with login, refresh, logout, and remote-operation requests. Authentication failures from API endpoints return `401` or `403` responses rather than redirects.

## Login log

The Authentication module records every login attempt — successful and failed — into the `LoginEvents` table in the authentication database (`alegacy.db`). Each entry stores only the attempted username, the client IP address, the outcome, and a UTC timestamp. Passwords, 2FA codes, and session tokens are never stored or logged. Entries older than `Authentication:LoginLog:MaxRetainedDays` (default 90) are pruned on write, bounding audit-table growth.

The read endpoint `GET /auth/login-logs` requires authentication and returns a paginated, newest-first list. The login page and dashboard use it to show recent activity; no role is required, matching the panel's current authorization model.

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
