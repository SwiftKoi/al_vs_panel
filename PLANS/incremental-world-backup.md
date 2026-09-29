# Incremental world backups to Google Drive — feasibility and plan

Status: ☐ planned · ⏳ in progress · ✅ done

Written 2026-09-29. Verdict: **feasible.** Your idea (base world once, then only what changed) works, and a tool that does exactly this already exists (Litestream). The core was tested on the Development test server. The Google Drive leg was **not** tested (no credentials here); see §7.

---

## 1. Why this is possible: how a Vintage Story world is stored

Read from the source (`vsapi/Common/IO/SQLiteDBConnection.cs`, `Vintagestory.Common.Database/SQLiteDbConnectionv2.cs`, `ServerSystemLoadAndSaveGame.cs`, `ServerSystemAutoSaveGame.cs`) and checked on the live test world.

- The whole world is **one SQLite file**, `Data/Saves/default.vcdbs`. Tables: `chunk`, `mapchunk`, `mapregion` (keyed by position), `gamedata` (one row, ~700 KB), `playerdata`. There is no timestamp column.
- The server opens it with `PRAGMA journal_mode=WAL; synchronous=Normal`. So **changes are already appended to a log** (`default.vcdbs-wal`) before being merged into the main file. That is the "main file + accumulating changes" behaviour you described, and it is what lets an outside process read a consistent copy or follow the changes while the server runs.
- **Autosave** (default every 300 s, `MagicNum.ServerAutoSave`) writes only chunks flagged dirty, plus the `gamedata` row. On an idle server the log shows `Saved 0 chunks, 0 mapchunks, 4 mapregions`.
- Player-built changes therefore reach the file as a small number of chunk rows per autosave.

Consequence: everything that must be backed up for the world is inside that one file. Only small extras live outside it (§6).

## 2. Why the built-in backup does not fit

- `/genbackup` and friends call SQLite `BackupDatabase` into `Data/Backups/`. That is a **full copy each time**, so it needs free space equal to the world and keeps growing with retention.
- While it runs, `BackupInProgress` is set and **autosaves are skipped** (`ServerSystemAutoSaveGame.cs:37,47`). On a large world the copy takes long, so the server goes without saving for that whole time.
- `DieBelowDiskSpaceMb` (400 here) makes the server refuse to start on a full disk, so filling the disk with backup copies is dangerous.

## 3. What was tested (Development test server, 110 MB world, 23,121 rows)

| Test | Result |
|---|---|
| Row-level snapshot of the live DB while the server ran (read-only, streamed, no extra disk) | Full scan of ~95 MB in 0.5–1.4 s |
| What changes per autosave on an idle server | Exactly one row: `gamedata` (~700 KB). Everything else identical |
| Litestream 0.5.17 in a separate container on the live `Saves/` directory | Server kept autosaving normally (saves at 16:50, 16:55, 17:00, 17:05). CPU of the replicator ~0.1 % |
| Base snapshot size | 110 MB DB → **57 MB** compressed |
| Restore from the replica, then compare row by row with the live DB | **Identical**, `PRAGMA integrity_check` = ok |
| Write load on a **copy**: 8 commits of 250 random chunk rows (4.8 MB of incompressible payload, worst case) | Increments of 1.8–2.0 MB per commit, ~14.8 MB total, i.e. roughly **3× the payload** (page-level, random keys, no compression gain). Restore matched the source exactly, all 2,000 changed rows |
| Litestream → local `rclone serve s3` → local folder (stand-in for Drive), then restore | Works. Restore matched |

Not achieved: growing the **live** world. `/wgen pregen`, `/wgen regenrange` and `/autosavenow` issued from the console produced no visible effect (console output is not returned by `server-command.sh`, and the far coordinates I chose may simply not have generated). The write-load test on a copy replaces it. Real growth per day still needs measuring on the production server (§8).

## 3b. Production facts (measured 2026-09-29, from the user's server)

- `default.vcdbs` = **125,030,842,368 bytes (117 GiB)**, real data (`du` real = apparent; page size 4096, 30,525,108 pages, only 5,220 free pages = 21 MB). The test world is ~1,100× smaller, so every size in this plan scales accordingly.
- WAL file 63 MB. **The WAL file size is only a high-water mark** (SQLite reuses it and never shrinks it), so it is useless as a write-rate metric.
- The log line `World saved! Saved N chunks…` **undercounts**: chunks are also written when they unload and when worldgen creates them (`ServerSystemUnloadChunks.cs`). The logs provided cover only 3 days (7.8, 8.8, 29.9), so no rate can be derived from them.
- Write rate: **still unmeasured**. Use `wal-meter.py` (reads only the `-wal` file header and frame headers, never opens the database, tested on Development) for a few days.

Consequences already visible:

- First snapshot: 117 GiB read, roughly 60 GB uploaded if it compresses like the test world (52 %). At 10–20 MB/s that is 1–2 hours; the bridge must not stage it locally (§7.2 becomes the top risk).
- **Snapshot cadence must be weekly or monthly, not daily** (daily × 14 days retention ≈ 0.9 TB and 60 GB/day upload for no benefit). Increments between snapshots carry the history.
- **Restore needs ≥ 125 GB of free disk** on the target and downloads ~60 GB. The production VPS has no such space, so restores go to another machine or a larger volume. Plan this before relying on it.
- Restore time grows with the number of increments since the last snapshot; Litestream's compaction levels keep that bounded.

## 3c. Experimental implementation on Development (2026-09-29)

Running as `/srv/stacks/test-vs-backup/` (see its README): Litestream + `rclone serve s3` bridge over a local folder standing in for Drive, `status.sh`, `restore.sh`, `backup-files.sh` (nightly cron). No change to the game container or the panel.

Failure-mode drills, all against the live test world:

| Drill | Result |
|---|---|
| Game container restarted | Replication continued, new increments appeared, nothing to do |
| Drive/bridge down for ~6 min (two autosaves), then back | Litestream buffered locally, then uploaded txids 8–b in order. **No new snapshot.** A Drive outage costs only the buffered increments in local disk |
| Replicator stopped, game autosaves, `wal_checkpoint(TRUNCATE)` (what the game does on its own when the WAL passes 1000 pages), replicator started | **Full new snapshot** (57 MB here; ~60 GB in production). Chain stays restorable, but it is a full re-upload. Keep the replicator running and set `restart: unless-stopped` |
| `restore.sh` latest | integrity ok; **row-for-row identical** to the live world |
| `restore.sh` point-in-time (17:36:00Z, before the outage) | integrity ok |
| Scratch game server (no network, separate copy of Data, restored PIT file) | Loaded the world and reached `RunGame` |
| Restore guards | refuse targets under `/srv/sites`, refuse to overwrite, refuse if free space < 110 % of the live file |

## 4. Options compared

### A. Litestream (recommended)
Follows the WAL, uploads a base snapshot once, then a small incremental file every few seconds or minutes, and compacts them in the background.

- Restore = latest snapshot + the incremental files, to **any point in time**. One command.
- Base + increments is exactly your model; the tool handles ordering, compaction and pruning.
- Recovery point can be seconds, not hours.
- No stop, no pause, no local copy of the world.
- Costs: page-level, so about 2–3× the dirty data in the worst case (still small); needs a bridge to reach Drive (§5); the WAL must be reachable on the same host as the server.

### B. Custom row-level delta tool (fallback)
Every N minutes, read a snapshot, hash each row, upload only rows whose hash changed (plus a list of deleted keys).

- Measured: a full scan is cheap for this world. Delta size ≈ 1× the changed data, and the format is ours.
- Costs: **the whole file is read every run** (tens of GB on a big production world); we must write and maintain the restore tool; a consistency point across several transactions of one autosave is on us.
- Use it only if Litestream's Drive bridge fails on large snapshots (§7).

### C. Periodic full backup to Drive, streamed
`sqlite3 .backup`-style copies need a local file, so this is what the built-in backup does and the reason you are asking. Only viable if the full copy is written straight to Drive without local staging, which SQLite does not support. Rejected.

## 5. Recommended design

```
VS server ──writes──▶ default.vcdbs + default.vcdbs-wal      (untouched)
                              │  read-only follower, same host
                        [ litestream container ]
                              │  S3 API, localhost only
                        [ rclone serve s3 ]  ──▶  Google Drive
```

Both extra containers run on the **same host** as the game server and mount the same `Saves/` directory. Same host is required: SQLite coordinates through a shared-memory file, which does not work across a network filesystem or across machines.

**Layout on Drive**

- `vs-backup/<server>/world/` — Litestream replica: a snapshot at each daily level plus incrementals.
- `vs-backup/<server>/files/` — the small non-DB files (§6), versioned.
- A separate Drive folder per server (Development, Production), never shared.

**Litestream settings to start with**

- `sync-interval`: **30–60 s** (not 1 s). Every sync with changes is one small Drive object, and Drive is slow and rate-limited for many tiny files.
- Daily snapshot, retention ≥ 14 days, tuned to how many restore points you want. Note: in the test the first snapshot was stored under three levels (L0/L1/L9) at once, roughly 3× the world size until retention pruned it. With 5 TB this is fine, but set the retention explicitly and check the real steady-state size after a week.
- Run the containers as the same uid as the game server (**1654:1654** on Development, from README §19), read-write on `Saves/` (the `-shm` file needs write access).

**Restore procedure (documented and tested)**

```sh
# stop the game server first, put the restored file where the world lives
litestream restore -o /data/Saves/default.vcdbs  s3://…            # latest
litestream restore -timestamp 2026-09-29T12:00:00Z -o … s3://…      # point in time
# then remove stale default.vcdbs-wal / -shm before starting the server
```

Always restore to a **new file** first, run `PRAGMA integrity_check`, then swap.

## 6. What is not in the world file

Back these up separately (they are small): `serverconfig.json`, `servermagicnumbers.json`, `Mods/`, `ModConfig/`, `ModData/`, `Playerdata/` if present, `Macros/`, `WorldEdit/`, and the panel's `secrets/` and SQLite volume if the panel itself matters. Use `rclone copy --backup-dir` (or a nightly tar of ~100 MB) so Drive keeps old versions. Mods change rarely, so this is negligible traffic.

## 7. Known risks and what is unverified

1. **Google Drive leg is untested.** Needs an rclone remote with **your own OAuth client** and your account token (a service account has no storage quota on personal Drive). I only ran the bridge against a local folder.
2. **Large snapshots through `rclone serve s3`.** The first snapshot of a big world is one huge upload. Whether `serve s3` handles multi-GB multipart uploads to Drive without buffering them all locally (which would defeat the purpose) is **unverified**. Test this early with a 5–20 GB dummy file before relying on it. If it fails, either use option B or replace the bridge (Litestream's WebDAV/SFTP replica types pointing at `rclone serve webdav`/`sftp`).
3. **Drive limits.** About 750 GB/day upload per account and API rate limits. Not a problem for the sizes here, but many tiny objects are (hence the long sync interval).
4. **If replication stops** (Drive outage, container down) the WAL is not lost, but if the server checkpoints past what Litestream has read, Litestream must take a **new full snapshot** on recovery. That is a full re-upload. Monitor lag.
5. **Disk pressure from the WAL.** While the follower lags, the WAL cannot be recycled and grows. Alert on `default.vcdbs-wal` size and on free space (the server itself refuses to start below `DieBelowDiskSpaceMb`).
6. **Whole-file rewrites cost a full-size delta.** SQL `VACUUM` (also run by repair mode) rewrites the file, so the next replication is as large as the world. Rare and manual, but do not schedule it casually.
7. **Consistency point.** An autosave is several separate transactions (chunks, map chunks, `gamedata`). A restore to an arbitrary second can land between them, the same state a crash would leave. Prefer restoring to a time just after a logged `Offthread save of savegame done.`
8. **Litestream drops a hidden directory** (`.default.vcdbs-litestream`) next to the world file. It is harmless, but the panel's File Manager will show it. It was removed after the test.
9. **Untested here:** the Production server's real size, write rate and host. It runs on a different VPS; the design applies there unchanged, but everything in §3 must be re-measured on it.

## 8. Implementation phases

1. ☐ **Measure production.** Read-only: DB size, WAL size, rows changed per autosave over a day (the manifest script from this investigation, or just watch the WAL). Decide retention from real numbers.
2. ☐ **Drive bridge test.** Configure an rclone Drive remote, run `serve s3`, push a 5–20 GB dummy object, watch local memory and disk. This decides Litestream vs option B.
3. ☐ **Development end to end.** Litestream + bridge on the test server, a week of running, then a full restore drill into a scratch directory and start a scratch server on it.
4. ☐ **Panel integration (optional).** A status card: last replicated time, lag, snapshot age, Drive usage; a "restore to point in time" action that restores to a scratch file and validates it. Should not restore over the live world without an explicit confirmation.
5. ☐ **Production rollout.** Same containers on the production host, separate Drive folder, monitoring on WAL size and lag.
6. ☐ **Small-files backup** (§6) on a nightly schedule.

## 9. Notes on the investigation itself

- The first source drop (`VintagestoryLib.tar.gz`) turned out to be a **different, modified engine** (it had "Stratum" systems). Everything in this document was re-checked against `CorrectVSLib.tar.gz` plus the public `vsapi` / `vssurvivalmod` / `vsessentialsmod` / `vscreativemod` repos. The measurements in §3 come from the actual test server, not from the source, so they were not affected.
- While the wrong lib was assumed, I sent `/stratum …` commands to the test server. They are not commands on this server; they were only logged as chat lines and changed nothing.
- Working files (scripts, test replicas, cloned repos) are in `/srv/research/`. They can be deleted once the plan is accepted.
