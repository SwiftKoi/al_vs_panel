# File manager — interactivity plan

Status: ✅ done · ⏳ in progress · ☐ planned

## 1. Upload feedback
- ✅ Upload with `XMLHttpRequest` for real byte progress (fetch cannot report upload progress).
- ✅ Upload queue panel (bottom-right, collapsible): per file name, size, progress bar, %, speed, ETA, status
  (queued → uploading → saving on server → done / failed).
- ✅ Separate "saving on server" state after 100% of bytes are sent.
- ✅ Per-file cancel and retry, "cancel all"; clear finished.
- ✅ Limited concurrency (2 uploads at a time).
- ✅ Pre-flight: reject files over the size limit before sending (limit exposed by the API).
- ✅ Name collision prompt (replace / keep both / skip).
- ✅ Folder upload preserving structure (drag a folder in, or "Upload folder"); missing folders are created (`write <path> 1`).
- ✅ `beforeunload` warning while uploads are running.
- ✅ Highlight newly uploaded files for a few seconds.

## 2. Background jobs (zip/unzip)
- ✅ Real progress (files and bytes processed) from the helper via a progress file.
- ✅ Cancel button (endpoint that triggers the job's cancellation token).
- ✅ Completion toast with "open folder".
- ✅ Job history drawer with results and errors.

## 3. Feedback for every action
- ✅ Toast notifications for success/failure of each action (errors sticky).
- ✅ Undo for move and rename.
- ✅ Instant update for rename (reverted on failure). ☐ Move and delete still wait for the server.
- ✅ Per-row spinner for rename and delete.

## 4. Keyboard
- ✅ ↑/↓ (+Shift) move/extend selection · Enter open · Backspace / Alt+↑ up · F2 rename ·
  Delete delete · Ctrl+A select all · Ctrl+U upload · Ctrl+Shift+N new folder ·
  Ctrl+F or `/` search · F5 / Ctrl+R refresh · Esc clear selection · `?` shortcut sheet.
- ✅ Ctrl+X / Ctrl+V cut & paste move.
- ✅ Type-ahead: typing letters jumps to the first matching name.

## 5. Navigation and finding things
- ✅ Search button (replaced the filter box): server-side case-insensitive name search in the current folder, optionally including subfolders; up to 500 results with a 15 s time budget. Clicking a hit opens its folder and selects it.
- ✅ Root and path in the URL (browser back/forward, bookmarkable folders).
- ✅ Folder picker in Move/Extract dialogs.

## 6. Mouse and touch
- ✅ Right-click context menu (rows and empty space).
- ✅ Long-press context menu on touch.
- ✅ Selection checkboxes on touch. ☐ Drag-select rectangle.
- ✅ Drag onto breadcrumb segments (including `/`) to move to any parent.
- ✅ Multi-item / folder download as zip (built in `.panel-tmp` as a job, downloaded automatically, then deleted; stale archives purged after 6 h).
- ✅ Inline rename (F2 / menu; Enter saves, Esc cancels; extension not preselected).

## 7. Context and safety
- ✅ Live-server warning: with the server online, changing any configured `ProtectedPaths` entry needs a ticked acknowledgement. This covers delete, move, rename, paste, drag, upload, extract, new folder, editor save and restore. The warning shows a fresh player count, and a banner appears inside protected folders.
- ✅ Trash: delete moves items to `<root>/.trash` (an instant rename; nothing is copied) for `TrashRetentionDays` days. The Trash dialog restores items or deletes them forever, and the delete notification has Undo. Permanent delete is an explicit checkbox.
- ✅ Hide write actions on read-only roots.

## 7b. Image preview
- ✅ Image viewer: double-click, Enter or "Preview" opens PNG/JPEG/GIF/WebP/BMP/ICO. ←/→ step through the folder's images, Space toggles fit/actual size, small images render pixelated, and the details panel shows a thumbnail. The images are served by `download?inline=true` with the correct type, `Content-Disposition: inline` and a sandboxing CSP. SVG is excluded on purpose, because opening one directly could run script in the panel's origin.

## 8. Polish
- ✅ Relative dates with exact time on hover; total size of the selection.
- ✅ Folder size on demand (Calculate in the details panel; 20 s budget).
- ✅ File-type icons (zip, log, image, code, `.vcdbs`).
- ✅ Remember sort order (the folder is already in the URL).

Done so far (2026-09-28): the upload queue lives in `UploadContext` above the routes, so
uploads keep running when you leave the file manager. Toasts come from `ToastContext`,
and the panel is `UploadQueuePanel`. Keyboard handling, the filter and type-ahead are in
`FileManagerPage`.

Jobs (2026-09-28): the helper prints JSON progress lines on stdout while zipping or
unzipping. The API exposes `GET /api/servers/operations` for recent jobs with progress and
target, and `DELETE /api/servers/operations/{id}` to cancel one. A cancelled unzip may
leave files it had already extracted; a cancelled zip leaves nothing, because its temp
file is never renamed.

Dropped (2026-09-28, per decision): folder tree, path tab-completion, recent/pinned folders, preview pane, log tailing.

Order of work: 1 → 3 (toasts) → 4 → 5 (filter) → 2 → the rest.
