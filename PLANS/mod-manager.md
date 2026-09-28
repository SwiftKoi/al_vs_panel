# Mod Manager — implementation plan

Status: ☐ planned · ⏳ in progress · ✅ done

## Decisions (2026-09-28)

- **No automatic updates or restarts.** Updates only write to `Mods/` and take effect on the next restart; the page shows "restart required" and a restart button. Announcement countdowns and restart-hook scheduling (§5.4, §8) are dropped.
- **Pre-releases are offered.** One-click targets stable releases, except for mods already on a pre-release, which stay on that track. Newer pre-releases are shown and installable from the version history.
- **Backup retention: 1** (only the last update can be rolled back).
- Some `alegacy*` mods are on ModDB (`alegacyvsquest`, `albase`, `arcanumlib`) and are handled like any other mod. The local copies may be *ahead* of ModDB, which is its own status. Server-only private mods are "Private".
- Test setup only, local target. SSH is out of scope for now (the helper design still allows it).

## Implementation status

- ✅ Phase 1: helper `scan`/`game-info`, ModDB client and cache, list/detail endpoints, Mods page with version history and changelogs.
- ✅ Phase 2: download → verify → stage → atomic apply, rollback, job progress, Update all / per-mod update / install any version, pins.
- ✅ Restart-required detection plus a restart button (instead of the orchestrated restart).
- ☐ Private-mod upload-replace from the Mods page (File Manager works meanwhile), nav badge and Dashboard entries, background checks, Automation API endpoints, browsing ModDB for new mods.

A new **Mods** page in the panel that shows every installed mod, where it stands against the
official Vintage Story ModDB (version history, changelogs, compatibility), and lets an admin update
one mod or all of them in one click, safely and reversibly.

---

## 1. Goals and non-goals

**Goals**

- See every mod on a server: name, mod id, installed version, side, source (ModDB or private), status.
- For each ModDB mod, show the full **version history** (all releases, dates, game-version tags,
  downloads, retracted releases) and **changelogs**.
- Before updating, show the changelogs **between the installed and the target version**, not just the latest.
- **One-click "Update all"** that is safe to press on a live server. It stages, backs up, swaps,
  restarts with player warnings, verifies, and rolls back if the server fails to load the mods.
- One-click **rollback** of any past update batch.
- Private/custom mods (most mods on our server today are `alegacy*`) are first-class. They are
  shown, versioned, backed up and replaceable by upload, but never "updated" from ModDB by mistake.

**Non-goals (v1)**

- Browsing or searching ModDB to install brand-new mods. This is planned for v2 (§11); the
  install pipeline built here is reused for it.
- Client-side modpack distribution.
- Editing mod configs. That already works through File Manager (`ModConfig/`).

---

## 2. What I learned from the reference projects

I read `Laerinok/VS_ModsUpdater_v2` (Python CLI) and `aaymont/vintage-mod-updater` (.NET/Avalonia)
to learn how the ModDB works. **No code is taken from them.** Both are licence-incompatible and
built for a desktop, not for a remote server behind a panel. I also checked the API live from this
server on 2026-09-28.

### ModDB API (`https://mods.vintagestory.at`)

| Call | Use | Notes (verified live) |
|---|---|---|
| `GET /api/mod/{modid}` | Full mod record | `statuscode` is a **string** (`"200"`). `mod` has `assetid`, `name`, `side`, `urlalias`, `logofile`, `lastreleased`, `homepageurl`, `sourcecodeurl`, … and `releases[]` (carryon has 88). |
| `releases[]` item | Version history | `releaseid`, `modversion`, `created`, `downloads`, `filename`, `fileid`, `mainfile` (CDN URL), `tags` (= **game versions**, e.g. `1.22.0…1.22.6`), `changelog` (**HTML**). |
| `GET /api/v2/mods/install-information?ids=a@1.0,b@2.0&gv=1.22.7` | Batch "what should I upgrade to" | Returns `data.{modid}.recommendedUpgrade`, `fileName`, `fileUrl` (relative `/download/...`), or `errorCode` (4031/4041 not found, 4101/4102 retracted, 4001 parse error). Batch up to ~40 ids. |
| `GET /api/v2/game-versions` | Game version list | **Currently returns `{}`**, so fall back to `GET /api/gameversions`. |
| Downloads | | `mainfile` points to `moddbcdn.vintagestory.at`. `/download/...` on `mods.vintagestory.at` redirects there. These are the only two trusted hosts. |

### Useful ideas worth keeping

- The **compatibility rule** used by VS_ModsUpdater. A release is compatible if one of its tags has
  the same `major.minor` as the server and is ≤ the server version. Prefer the highest matching
  tag, then the highest mod version, then the newest `created`.
- **Aggregating changelogs** of every release in `(installed, target]` so admins see everything
  they are skipping.
- **Trust policy** from vintage-mod-updater: HTTPS only, allowlisted hosts checked **after
  redirects**, response size caps, and validating that the downloaded zip's `modinfo.json` `modid`
  matches what we asked for.
- `install-information` exists and does the compatibility maths server-side. Use it as the primary
  signal and use our own tag logic to explain it (for example, "why is there no update?").

### Bugs and weak spots to avoid

- VS_ModsUpdater **deletes the old mod before the new one is in place**. A failed extract/move
  leaves the server without the mod. We stage, verify and then swap, and keep the old file as a backup.
- Both parse `modinfo.json` with strict JSON plus regex fixes. Real modinfo files have comments,
  trailing commas, single quotes and unquoted keys, because the game uses Newtonsoft (lenient).
  We parse on the panel side with **Newtonsoft.Json**, the same leniency as the game.
- vintage-mod-updater's version normalisation cuts everything after `-`/`+`, so `1.2.0-rc.1` equals
  `1.2.0`. We need a real comparator where pre-release < release, numeric parts compare
  numerically, and there is an opt-in for pre-releases.
- Neither tool restarts the server or checks whether the new mod actually **loaded**. On a server
  that is the most important step.
- Neither handles mods that are not on ModDB other than "error". We model them explicitly.

---

## 3. How it fits our architecture

### Where the mods live

- `Data/Mods/` on the game-server target. Today there are 6 zips; 5 of them are our private
  `alegacy*` mods. It is already a **protected path** in File Manager.
- The game version is the line `Game Version: v1.22.7 (Stable)` in `Data/Logs/server-main.log`.
- Load result: `Mods, sorted by dependency: game, alegacydblib, …` in the same log, plus mod
  exceptions and warnings.
- The panel may manage an **SSH target**, not only a local one. So, like File Manager, the panel
  **never touches the mods folder directly**. Everything on the target goes through
  `IRemoteOperationsService` and a target-side helper.

### New module: `Modules/ModManager`

Standard layout per `Docs/Module-Development.md`:

```
Modules/ModManager/
├── AlegacyWebPanel.ModManager.csproj   (+ Newtonsoft.Json for lenient modinfo parsing)
├── module.json
├── README.md
├── Contracts/      ModDtos, UpdatePlanDto, UpdateBatchDto, ReleaseDto, …
├── Endpoints/      ModManagerRoutes.cs, ModManagerEndpoints.cs
├── Exceptions/     ModNotFound, ModDbUnavailable, UpdateInProgress, ValidationFailed, …
├── Infrastructure/ ModManagerModule.cs (DI), ModDbHttpClient, UpdateWorker (BackgroundService)
├── Persistence/    ModManagerDbContext (SQLite /var/lib/alegacy/data/mods.db), repositories
├── Services/       IModManagerService, ModCatalogService, UpdatePlanner, UpdateExecutor,
│                   ChangelogSanitizer, VersionComparer, CompatibilityResolver, LoadVerifier
└── Tests/          Unit (fixtures of recorded ModDB JSON), Feature (endpoints)
```

Dependencies (all via public interfaces only):

- `IRemoteOperationsService` runs the target helper.
- `IServerManagementService` provides status, player count, console (`/announce`), restart and logs.
- Its own SQLite database (same pattern as Analytics and Logging), so ModDB cache writes never
  contend with auth.

### Target-side helper: `Server/Development/mod-manager.py`

This follows the same contract style as `file-manager.py` (exit codes, JSON on stdout, root
containment, no symlink following). The allowlisted operation is
`demo-local-mods → python3 mod-manager.py <mods_dir> <backup_dir> <staging_dir>`.

| Command | Does |
|---|---|
| `scan` | For each entry in Mods (`*.zip`, dirs with `modinfo.json`, `*.cs`, `*.dll`): type, size, mtime, sha256 and the **raw bytes of modinfo.json** (base64, capped at 256 KB). No JSON parsing on the target. Also returns the `modicon.png` (capped) for the UI. |
| `stage <batch> <filename>` (stdin) | Writes an upload into `staging/<batch>/`. Size cap, atomic temp+rename. |
| `verify <batch>` | Zip sanity: entry cap, no absolute or `..` paths, exactly one modinfo.json, returns its raw bytes. |
| `apply <batch> <manifest.json>` | For each item, moves the old file to `backups/<batch>/` and moves the staged file into Mods. It writes a journal first, so an interrupted apply can be completed or undone. It is all-or-nothing: on any error it reverses the moves already done. |
| `rollback <batch>` | Uses the journal to restore the backed-up files and remove the ones this batch added. |
| `prune-backups <keep>` | Retention. |
| `game-info` | Game version and the last "Mods, sorted by dependency" line and mod errors since a given log offset (used by the load verifier). |

The backups and staging directories are `Data/ModBackups/` and `Data/.mod-staging/`, on the same
filesystem as `Mods/`, so the swap is a `rename` rather than a copy.

### Why the panel downloads, not the target

The panel downloads from ModDB, validates the file (trusted host after redirects, size cap, zip
structure, `modinfo.modid` and `version` match the planned release), then streams it to the target
with `stage`. This gives one place that enforces the trust policy, works the same for SSH targets,
and the game host needs no outbound HTTP.

---

## 4. Data model (mods.db)

- **ModDbCache**: `modid`, fetched JSON (trimmed to the fields we use), `fetchedAtUtc`, `etag/hash`.
  TTL is 6 h. The **Check now** button bypasses it.
- **InstalledSnapshot** (per server): last scan result with mod id, version, filename, sha256, side
  and source (`moddb` | `private` | `unknown`).
- **ModSettings** (per server + modid):
  - `pinned` (never auto-update, optional pin reason)
  - `ignoredVersion` (skip this one release)
  - `allowPrerelease`
  - `moddbIdOverride` (link a mod whose modid differs from its ModDB entry, or mark it private)
  - a free-text note
- **UpdateBatch**: id, server, who, when, mode (now / at next restart / stage only), state machine
  (`planned → downloading → staged → waiting-restart → applying → restarting → verifying →
  succeeded | rolled-back | failed`), items (modid, from→to version, filenames, sha256),
  per-step log, and the verification result.
- **ServerSettings**: game version override, check schedule, auto-rollback on load failure (default
  **on**), backup retention (default the last 10 batches), announcement text and countdown.

---

## 5. Update pipeline (what "one click" actually does)

1. **Plan** (instant, no side effects)
   - Scan, resolve the game version, call `install-information` in batches of 40, then
     `/api/mod/{id}` for releases and changelogs of mods with updates.
   - Resolve the target per mod: the recommended upgrade, unless it is pinned, ignored, a
     pre-release when those are off, or would cross a game `major.minor`.
   - Dependency check from each new release's `modinfo.dependencies`, with a warning if a new
     version requires a game version or a mod version we don't have. The dependencies are known
     only after download, so this is re-checked at the verify step.
2. **Download and verify.** Parallel with a limit of 3. Each file is validated as in §3. Nothing
   on the server has changed yet, and any failure here just drops that mod from the batch.
3. **Stage.** Stream to `.mod-staging/<batch>/` on the target.
4. **Apply and restart**, according to the mode the admin chose:
   - **Now.** If players are online, send `/announce` at a configurable countdown (default 5 min
     → 1 min → 10 s, the same wording style as `ops/scheduled-restart.sh`), stop the server,
     `apply`, and start the server.
   - **At next scheduled restart.** The batch stays staged. The restart hook (§8) applies it while
     the server is stopped. This is the zero-disruption option.
   - **Stage only / apply without restart.** For admins who restart manually. The UI shows a
     persistent "restart needed" banner.
5. **Verify load.** Tail the log from the offset recorded before the restart, and wait (up to a
   configurable timeout) for the `Mods, sorted by dependency` line. Check that every updated modid
   is present, and that there are no mod crash, exception or "failed to load" lines for those mods,
   and that the server status reaches online.
6. **Auto-rollback** on failure: stop, `rollback <batch>`, start, verify again, and mark the batch
   `rolled-back` with the evidence (log excerpt) attached.
7. **Done.** Toast and a batch history entry. Backups are kept per retention.

Only one batch per server runs at a time (lock). The batch runs in a `BackgroundService`, so
closing the browser doesn't matter, and the UI reconnects to its progress.

---

## 6. Making it easy and convenient for admins

This is the part I care most about. The page should answer "is anything out of date, is it safe,
do it" in about 5 seconds, and it should never let a click break the server.

### 6.1 Page layout (`/mods`, new item in NavigationMenu)

- **Summary strip** at the top, which reads like a sentence:
  *"18 mods · 4 updates available · 1 incompatible · game 1.22.7 · last checked 3 min ago
  [Check now]"*.
  The main **Update all (4)** button sits next to it. When nothing is outdated it says
  "Everything is up to date ✓" and the button is hidden.
- **Mod table** (existing `DataTable`), one row per mod:
  - icon, name, mod id (muted)
  - installed version → available version, with a coloured badge: `Update` / `Up to date` /
    `Pinned` / `Private` / `Retracted!` / `Incompatible` / `Not on ModDB`
  - side (Server / Client / Universal), last released date
  - a checkbox for partial selection ("Update selected")
  - a row menu: Update, Pin, Skip this version, Open on ModDB, Show in File Manager, Download
    installed file
- **Filters as chips**: *Updates available* (default when there are any), *All*, *Private*,
  *Problems*, plus a search box. The selected filter is remembered in the URL.
- **Detail drawer** (click a row; `BottomSheet` on mobile):
  - **What changes if I update.** The combined changelog of every skipped release, newest first,
    each with version, date and game-version tags.
  - **Version history.** A timeline of all releases with the installed one marked "installed" and
    the target marked "will install". Retracted and incompatible releases are greyed out with the
    reason. Each release has **Install this version**, so downgrade or pin-to-version is one click.
  - Links: ModDB page, source, issue tracker.
  - Pin toggle, pre-release toggle, note field.

### 6.2 One click that respects a live server

Clicking **Update all** opens a single confirmation dialog, not a wizard:

- The list of what will change (`carryon 1.13.0 → 1.14.3`), each item expandable to its changelog.
- Warnings grouped at the top in plain words, for example "2 players online", "`xlib` 2.0 is a
  major version jump" or "`foo` requires `bar ≥ 1.4`".
- **When to apply** as a radio group with a smart default. If nobody is online the default is
  *Now*. If players are online it defaults to *With 5-minute warning*, and offers
  *At the next scheduled restart (00:00)*.
- One button: **Update 4 mods**.

After that there is a live progress panel (reusing the BackgroundTaskBar pattern): download →
verify → stage → warn players → restart → verify load, with each step ticking green. Admins can
leave the page; a toast reports the result.

### 6.3 Safe by default, so admins don't have to be careful

- Nothing on the server changes until every file is downloaded and verified.
- Every update is a **batch with a backup**. The *History* tab lists batches with a **Roll back**
  button, which restarts using the same warning flow.
- Auto-rollback if mods fail to load. The admin sees *"Update rolled back: `foo` threw
  NullReferenceException during load"* with the log excerpt, instead of a dead server at 3 am.
- Major game-version jumps and pre-releases are never picked automatically.
- Retracted installed releases are highlighted at the top as a problem ("the author pulled this
  version: reason …").

### 6.4 Private mods are not second-class

- Private mods get a clear `Private` badge, no scary errors, and are excluded from "Update all".
- **Replace with upload**: drag a new zip onto the row. The panel reads its modinfo, shows
  `1.3.0 → 1.4.0`, and runs it through the same batch, backup and restart pipeline, so
  private mods also get history and rollback.
- If a mod's modid is not found on ModDB but it is really there under another id, an admin can
  link it once (`moddbIdOverride`).

### 6.5 Staying informed without visiting the page

- A background check (default every 6 h) updates the counts. The badge on the **Mods** navigation
  item shows the number of available updates.
- The Dashboard "Needs attention" card gets entries for *updates available*, *retracted mod
  installed* and *last update rolled back*.
- Optional: a `/api/v1` Automation API endpoint (`GET mods`, `POST mods/update`) for scripts and
  Discord bots, behind the existing API key.

### 6.6 Small things that matter

- Changelogs are rendered as sanitised HTML (ModDB sends HTML), with lists and links preserved.
  Scripts, styles and images are stripped, and links open in a new tab with `rel=noopener`.
- Relative times ("released 2 days ago") with the exact date on hover.
- Fully translated (en/ru through the existing i18n), including the player announcement text.
- Keyboard: `/` to search, `u` to update the selected mod, `Esc` to close the drawer.
- Works on a phone: the table collapses to cards, and the drawer becomes a bottom sheet.
- Every action is written to the Logging module (who updated what and when) for auditing.

---

## 7. HTTP API (browser, cookie + CSRF, like other modules)

```
GET    /api/servers/{id}/mods                       list + status (from cache; ?refresh=true re-checks)
GET    /api/servers/{id}/mods/{modid}               detail: releases, changelogs, settings
PUT    /api/servers/{id}/mods/{modid}/settings      pin / ignore version / prerelease / override / note
POST   /api/servers/{id}/mods/plan                  body: {modids?|all, targetVersions?} → plan with warnings
POST   /api/servers/{id}/mods/batches               body: {planId, mode} → start batch
GET    /api/servers/{id}/mods/batches               history
GET    /api/servers/{id}/mods/batches/{batchId}     live progress / result
POST   /api/servers/{id}/mods/batches/{batchId}/rollback
DELETE /api/servers/{id}/mods/batches/{batchId}     cancel (only before apply)
POST   /api/servers/{id}/mods/upload                private mod replacement → plan
GET    /api/servers/{id}/mods/{modid}/icon          cached modicon.png
```

---

## 8. Configuration and ops changes

- `appsettings*.json`:
  - a new `ModManager` section per server instance: operation name, mods, backup and staging
    paths, check interval, restart countdown and announcement templates
  - a new `Remote.Commands.demo-local-mods` entry for the helper
- The **scheduled restart** (`ops/scheduled-restart.sh`) gains a hook. While the server is down it
  runs `mod-manager.py apply-pending`, so "apply at next restart" works without the panel being in
  the loop at midnight. The panel then verifies the load after startup.
- The panel container needs outbound HTTPS to `mods.vintagestory.at` and `moddbcdn.vintagestory.at`.
  This already works from this host.
- `Docs/Architecture.md`, `Docs/Configuration.md`, `Docs/Operations.md` and `Docs/Security.md` are
  updated in the same change (AGENTS.md rule). The platform README §19 gets a short note.

---

## 9. Security

- ModDB responses: HTTPS only, host allowlist re-checked on the final URI after redirects, a 2 MB
  cap on API JSON and a 200 MB cap on mod files (configurable), with timeouts.
- Zip validation: entry count cap, no absolute or `..` entries, a single `modinfo.json`, and a
  modid/version match against the plan. `.cs`/`.dll` single-file mods are accepted only via
  admin upload, never auto-downloaded.
- The helper is the only thing that writes to `Mods/`. It uses root containment and never
  follows symlinks for moves.
- Changelog HTML is sanitised server-side with an allowlist (`HtmlSanitizer` package). The raw
  HTML is never passed to `dangerouslySetInnerHTML`.
- All mutating endpoints require an authenticated session and CSRF. Batch actions are audited.
- Mods run arbitrary code on the game server. The UI states the source (ModDB author) and does not
  pretend a ModDB file is "safe". This is noted in `Docs/Security.md`.

---

## 10. Testing

- **Unit tests**:
  - version comparator (pre-release, 4-part, `v` prefix)
  - compatibility resolver against recorded real ModDB JSON fixtures (carryon and others)
  - changelog aggregation and sanitisation
  - lenient modinfo parsing (comments, trailing commas, unquoted keys, BOM)
  - planner warnings
  - batch state machine including every failure branch
- **Helper tests** (pytest in a temp dir): apply/rollback atomicity, including a simulated crash
  mid-apply followed by journal recovery; symlink and path escapes; size caps.
- **Feature tests**: endpoints with a fake ModDB `HttpMessageHandler` and a fake remote runner.
- **Manual end-to-end on the Development server**:
  1. Install an old CarryOn.
  2. Update all, using the warning flow while logged in with a client.
  3. Check the load was verified.
  4. Break a mod on purpose and check auto-rollback.
  5. Roll back manually from History.

---

## 11. Delivery phases

| Phase | Scope | Result |
|---|---|---|
| 1 | Helper `scan`/`game-info`, ModDB client + cache, list and detail endpoints, read-only Mods page with version history and changelogs | Admins can **see** everything (useful on its own) |
| 2 | Planner, download/verify/stage/apply/rollback, batches, History tab, single and "Update all" with the *Now* mode + warnings + load verification + auto-rollback | **One-click update** |
| 3 | Scheduled-restart hook ("apply at next restart"), private mod upload-replace, pins/ignore/pre-release settings, background checks, nav badge, Dashboard entries | Comfort and private mods |
| 4 (v2) | Browse/search ModDB and install new mods, dependency auto-install, Automation API endpoints, Discord/webhook notifications | Extras |

Each phase ships with docs, tests, i18n strings and a changelog line.

---

## 12. Open questions for you

1. **Default "when to apply"** when players are online: a 5-minute warning, or the next scheduled
   restart (00:00)?
2. Should **pre-release** mod versions ever be offered? My default is no, with a per-mod opt-in.
3. **Backup retention**: last 10 batches OK, or keep by age?
4. Are the `alegacy*` mods published anywhere (for example a GitHub release) that we could treat as
   an update source later, instead of manual upload?
5. Is the production server a **remote SSH target**? The design already assumes it could be. This
   question only affects test priority.
