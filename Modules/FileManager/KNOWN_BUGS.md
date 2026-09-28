# File manager — bug review

Review date: 2026-09-28.

## Fix status (2026-09-28)

Fixed: 1, 2, 3, 4, 5, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23
(file manager page), 24, 25, 26. Mostly fixed: 8 (the leak, the timeout and the
concurrency limit are fixed; task status is still not scoped per server/user, and
it is still lost on restart, although the UI now says so).

Not fixed: 6 (SSH; deferred on purpose) and the operational note (the panel still
gives no warning before changing live world files).

The original findings follow. Severity: 🔴 high (data
loss or security) · 🟠 medium (wrong behavior users will hit) · 🟡 low.

Files reviewed: `Modules/FileManager/**`, `Modules/RemoteOperations/Infrastructure/{Local,Ssh}RemoteConnection.cs`
(binary execution), `Server/Production/file-manager.py`, `Server/Development/file-manager.py`,
`frontend/src/pages/FileManagerPage.tsx`, `frontend/src/api/files.ts`, `frontend/src/lib/fileManager.ts`.

## 🔴 High

### 1. The atomic-upload fix never reached the production helper
Commit `4513015` ("atomic write") changed only `Server/Development/file-manager.py`.
`Server/Production/file-manager.py:106` still opens the target with `open(target_path, 'wb')`,
so it truncates the existing file immediately and then streams stdin into it.
- **Failure:** the user re-uploads `serverconfig.json`, but the upload exceeds the limit or
  the browser disconnects. `LocalRemoteConnection` kills the helper as designed. The
  original file has already been truncated, leaving it empty or half-written. The same
  applies to saves from the text editor.
- The kill-before-EOF logic in `LocalRemoteConnection.cs:190-211` assumes the helper writes
  to a temp file, which is not true in production.

### 2. Delete and rename act on a symlink's target, not the link
Every helper command resolves `os.path.realpath(...)` first
(`file-manager.py:190`, `:133`). For a symlink inside the root that points elsewhere inside
the root:
- `delete link` → `shutil.rmtree(<real directory>)`. This deletes the real data, not the link.
- `rename link x` → renames the real target, and the link breaks.
- `write` on a link overwrites the target. That might be intended; the other two are not.

### 3. Downloads report 200 before anything is validated
`FileManagerEndpoints.DownloadAsync` (`:39-60`) returns `Results.Stream(...)`. The service
call, and with it every validation, runs *inside* the stream callback, after the 200 status
and `Content-Disposition` headers have been sent. `TranslateAsync` never sees the
exceptions. The code comment ("Verify access synchronously first") describes what the code
is supposed to do, not what it does.
- **Failure:** downloading a missing file, a folder (helper exit 4), an unknown root, or a
  path containing `..` produces a 200 response and an empty or truncated file saved under
  the requested name, or a connection reset. It never produces a 404, 400 or 403.
- A helper failure partway through a large file also saves a silently truncated file.

### 4. Extracting archives has no size or entry limit (zip bomb)
`MaximumArchiveSizeBytes` is validated in configuration but never used
(grep: only `FileManagerOptions.cs` and `FileManagerModule.cs`). `unzip_archive`
(`file-manager.py:279-285`) extracts every member with no limit on total uncompressed size
or member count.
- **Failure:** a 50 MB upload that expands to hundreds of GB fills the disk the game server
  saves to.

### 5. Text editor silently corrupts non-UTF-8 files and strips the BOM
`FileManagerService.GetTextContentAsync` (`:150-152`) decodes with
`StreamReader(ms, Encoding.UTF8)`:
- Invalid UTF-8 bytes (Latin-1 or CP1251 configs, for example) become `U+FFFD`. On save,
  those `�` characters are written back and the original bytes are lost.
- The reader strips a UTF-8 BOM, and `SaveTextContentAsync` writes without one. Every edit
  of a BOM file removes its BOM.
- The binary check only looks at the first 8 KB for NUL bytes.

### 6. Upload over SSH swallows stream errors
`SshRemoteConnection.ExecuteBinaryAsync` (`:222-234`) catches *every* exception from the
stdin copy and ignores it. It then closes the input normally.
- **Failure:** on an SSH target, an oversized upload (`FileTooLargeException`) or a client
  abort gives the helper EOF. The helper saves the truncated file as complete, and the API
  returns 200. Only local targets have the kill-on-failure fix.

## 🟠 Medium

### 7. Configured limits are dead settings
`MaximumListingEntries` (2000) and `MaximumConcurrentOperations` (2) are never read.
- Listing a directory with many entries (for example `Logs/Archive`, or backups) returns
  everything in one JSON response. The whole output is buffered in memory
  (`RemoteFileRepository.ListAsync`).
- The number of zip and unzip jobs is unlimited. The frontend starts one extract job *per
  selected archive*, all at once.

### 8. Background operations: leaked, not cancellable, not scoped to a server
`BackgroundOperationTracker`:
- Finished tasks are never removed from `_tasks`, so the dictionary grows for the life of
  the process.
- Tasks run with `CancellationToken.None` and cannot be cancelled. A stuck `unzip` runs
  forever.
- `GET /api/servers/operations/{taskId}` isn't tied to a server or user. Any authenticated
  session can read any task's description, which includes paths.
- The state is only in memory. After a panel restart, polling returns 404, and the UI stops
  polling with no message (`FileManagerPage.tsx:168-171`).

### 9. Every helper error becomes a 502
The helper signals "not found" (3), "not a directory" (4), "parent missing" (6, 11) and
"traversal" (2) through its exit code. `LocalRemoteConnection` turns every non-zero exit
into `RemoteOperationFailedException`, which becomes **502 Bad Gateway, "Remote operation
failed"** (`FileManagerEndpoints.cs:365`). A missing folder or a stale path therefore looks
like a server outage in the UI.

### 10. Move, rename and extract silently overwrite or nest
- `move`: `shutil.move(src, dest)` puts the source *inside* `dest` when `dest` is an
  existing folder. Moving folder `A` to a place where `A` already exists produces `A/A`.
  If `dest` is an existing file, it is overwritten without warning.
- `rename` (`os.rename`) replaces an existing file with the new name without asking.
- `unzip` overwrites existing files in the destination.
- `zip` replaces an existing archive with the same name. If archiving then fails, the
  `except` branch deletes `dest_path` (`:254-255`), so the user's earlier archive is gone
  as well.

### 11. A failed zip leaves a partial archive behind
Inside `zip_directory`, a traversal or missing-source problem on a later path calls
`sys.exit(...)` (`:233`, `:237`). `SystemExit` isn't an `Exception`, so the cleanup
`except` doesn't run and a half-written zip stays in place. `unzip` has the same problem: a
zip-slip member found partway through leaves the earlier members extracted.

### 12. Multi-select paths are joined with `;`
The frontend joins sources with `;` (`FileManagerPage.tsx:593`), and the backend and helper
split on `;`. A file or folder whose name contains `;` is split into two bogus paths, and
the compress fails. It could also archive the wrong item if a path fragment matches another
file.

### 13. The move and extract dialogs cannot target the root
Both dialogs prefill `modalInput` with `currentPath`. At the root this is `""`, and confirm
requires `modalInput.trim()` (`:562`, `:608`). Typing `/` is rejected by the backend
("absolute path"). There is no way to move or extract into the top-level folder through the
dialog; only drag-and-drop onto the "Up" row works.

### 14. Opening the text editor matches the file name case-insensitively
`GetTextContentAsync` (`:130`) lists the parent folder and finds the file with
`OrdinalIgnoreCase`. On Linux, `Config.json` and `config.json` are different files, so the
size check can use the wrong entry. It also lists the whole parent folder just to get one
file's size.

### 15. Stale listings when navigating quickly
`fetchDirectoryListing` (`FileManagerPage.tsx:93-113`) has no abort or request-id guard. If
you click into a folder and then quickly go up, the slower response wins, and the table can
show the wrong folder's entries under the current path. Actions then target paths that do
not exist there.

### 16. Hard-coded `"data"` root
`fetchDirectoryListing`, drop-upload and drag-move fall back to `selectedRootId || "data"`.
A server without a `data` root gets a 404 on first load, so `roots` stays empty and the
root selector has nothing in it. The page cannot recover.

### 17. Only the last extract task is tracked
Extracting several archives starts one background task each, but only `lastTaskId` is
polled (`:613-640`). Failures in the earlier tasks are never reported, and the listing may
refresh before they finish.

### 18. Upload/abort leaves `.part` files visible (dev helper)
The dev helper's temp files (`.<name>.<pid>.part`) appear in listings while an upload is
running, and after a crash until the next upload of the same name. Once finding 1 is fixed
by copying the dev helper, production has the same issue. Consider hiding `.part` files in
`list`.

## 🟡 Low

19. **Size sorting uses rounded strings.** `sortFileItems` parses back the formatted
    `"1.0 MB"` text (`lib/fileManager.ts:28-35`), so files of 1.01 MB and 1.04 MB compare
    as equal. Date sorting likewise parses the `ru-RU` display string. The raw `size` and
    `modified` should be kept on `FileItem`.
20. **Colons are rejected.** `ValidateRelativePath` rejects any `:`
    (`FileManagerService.cs:409`), so Linux files with a colon in the name (such as
    timestamped logs or crash dumps) cannot be opened, downloaded or deleted.
21. **`mkdir` of an existing folder succeeds.** `makedirs(..., exist_ok=True)` also
    silently creates missing intermediate folders, so the user gets no "already exists"
    feedback.
22. **Default archive name drops everything after the first dot.** `v1.2.3` becomes
    `v1.zip`, and `world.backup.vcdbs` becomes `world.zip` (`:344`).
23. **Errors go through `alert()`.** Some errors use `alert()`, others the inline error
    banner, which is inconsistent. Poll errors are swallowed entirely.
24. **Only the first file part of an upload is processed.** A multipart request with
    several files silently ignores the rest (`FileManagerEndpoints.cs:183`). The current UI
    sends one file per request, so this is latent.
25. **No conflict detection when saving.** The editor overwrites the file even if the game
    server rewrote it while the editor was open. This matters for configs the server
    rewrites on shutdown.
26. **Listing hides problems silently.** Entries that fail `stat` and symlinks pointing
    outside the root are skipped without any indication (`file-manager.py:42-58`).

## Operational note (not a code bug)
The `data` root is writable and contains the live world save (`Saves/*.vcdbs`), `Logs`
and configs. The panel doesn't warn before deleting, moving or overwriting these while the
game server is running, and doing so can corrupt the world.
