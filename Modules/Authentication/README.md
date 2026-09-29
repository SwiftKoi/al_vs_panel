# Authentication module

Signs panel users in and out, keeps their session alive, and protects accounts with an optional
second factor. It owns the **session cookie**, the **login history** and the **trusted-IP list**.
Accounts themselves (creation, roles, deletion) belong to the [Users](../Users/) module, and the
Identity tables are shared through Users' `AppDbContext`.

Routes live under `/auth`. The frontend uses them from `SessionContext` and `LoginPage`.

## Features

| Feature | What it does |
|---|---|
| **Password login** | `POST /auth/login` checks the username and password and issues the session cookie. Failed attempts answer `401` with a generic message, so a wrong username and a wrong password look the same. |
| **Two-factor login (TOTP)** | If the account has 2FA on and the request comes from an IP that is not trusted, login returns `requiresTwoFactor: true` and parks the user in a temporary "pending" cookie (ASP.NET Identity's two-factor cookie). `POST /auth/login/2fa` then accepts the 6-digit authenticator code and completes the sign-in. |
| **Trusted IPs** | After a successful sign-in, the client IP is remembered for the user for `TrustedIpTtlDays` (30 by default). Logging in again from that IP skips the 2FA prompt. Disabling 2FA clears the user's trusted IPs. |
| **2FA enrolment** | `POST /auth/2fa/setup` returns the authenticator key and an `otpauth://` URI for a QR code. `POST /auth/2fa/enable` turns 2FA on once the user proves it with a valid code. `POST /auth/2fa/disable` turns it off and resets the key. `GET /auth/2fa/status` says which state the account is in. |
| **Password change** | `POST /auth/password` changes the caller's own password (current + new). It rotates the security stamp, which signs out the user's other sessions. The current session's cookie is reissued so the caller stays signed in. |
| **Session endpoints** | `GET /auth/session` returns the current user and role. `POST /auth/refresh` reissues the cookie so the frontend can pick up a role change without a re-login. `POST /auth/logout` ends the session. |
| **CSRF token** | `GET /auth/csrf` returns an anti-forgery token. Every state-changing route in the panel expects it in the `X-CSRF-TOKEN` header. |
| **Login history** | Every login attempt (success or failure, including failed 2FA codes) is written with username, IP and time. `GET /auth/login-logs?page=&pageSize=` pages through it, newest first (page size capped at 50). Entries older than `LoginLog.MaxRetainedDays` (90) are deleted when a new one is written. |
| **Role refresh** | The security stamp is revalidated every minute, so a role change or a deleted account reaches an existing cookie within about a minute. |

## Access

| Route | Who can call it |
|---|---|
| `/auth/csrf`, `/auth/login`, `/auth/login/2fa` | anyone (login routes require an anti-forgery token) |
| `/auth/session`, `/auth/refresh`, `/auth/logout` | any signed-in cookie, even one without a role yet (`PanelPolicies.SignedIn`) |
| `/auth/password`, `/auth/2fa/*` | admins and moderators (`PanelPolicies.Staff`), each for their own account |
| `/auth/login-logs` | admins only |

## Cookie

| Property | Value |
|---|---|
| Name | `Authentication:CookieName`, default `alegacy.auth` |
| Lifetime | `CookieLifetimeHours`, default 8, sliding by default (`SlidingExpiration`) |
| Flags | `HttpOnly`, `SameSite=Strict`, `Secure` always outside Development |
| Unauthenticated / forbidden | `401` / `403` (no redirects) |

## Configuration

Section `Authentication` in `appsettings*.json`:

| Key | Default | Meaning |
|---|---|---|
| `CookieName` | `alegacy.auth` | Session cookie name |
| `CookieLifetimeHours` | `8` | Session length. Must be positive |
| `SlidingExpiration` | `true` | Extend the session on activity |
| `TrustedIpTtlDays` | `30` | How long an IP stays trusted after a sign-in |
| `LoginLog:MaxRetainedDays` | `90` | Login history retention |
| `Admin:Username`, `Admin:PasswordFile` | `admin`, `/run/secrets/admin_password` | The first admin account. Read by the [Users](../Users/) seeder, not by this module |

Password rules and the lockout policy (12+ characters with upper, lower, digit and symbol; 5 failed
attempts lock the account for 15 minutes) are defined in `Users/Infrastructure/UsersModule.cs`.

## Layout

```text
Endpoints/       AuthenticationRoutes (route + policy map), AuthenticationEndpoints (handlers)
Services/        AuthenticationService (credentials, password change)
                 TwoFactorService (TOTP, trusted IPs)
                 LoginLogService (history, paging)
Infrastructure/  IdentityAuthenticationSession (cookie sign-in/out/refresh), AuthenticationModule (DI, cookie setup)
Persistence/     Identity-, EF-backed repositories for users, trusted IPs and login events
```

## Notes

- The client IP is `RemoteIpAddress`, rewritten from `X-Forwarded-For` by `Program.cs`. That code clears
  `KnownProxies`/`KnownNetworks`, so it trusts the header from **any** sender and relies on the app port not
  being reachable except through the gateway. Anyone who can reach the app directly (on this host it is
  published on `127.0.0.1:3000`) can spoof their IP, which matters for the trusted-IP 2FA bypass and the login log.
- Trusting an IP happens on **every** successful password login when 2FA is off as well, so
  enabling 2FA later starts from an already populated list. It is cleared only when 2FA is disabled.
- Sessions are cookie-based only. There is no token login. Machine access uses the
  [AutomationApi](../AutomationApi/) key instead.
