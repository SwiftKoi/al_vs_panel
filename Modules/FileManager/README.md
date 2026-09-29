# FileManager module

Browse, edit, upload, download and archive files on a game server from the panel. Every operation
is confined to a configured **root** (for example the server's `Data` folder) and is executed by a
small allowlisted helper script (`Server/*/file-manager.py`) through the
[RemoteOperations](../RemoteOperations/) module. The module never touches the filesystem itself.
The frontend page is `/files` (`FileManagerPage.tsx`) and the text editor is `/editor`.

Routes live under `/api/servers/{serverId}/files` and are **admin-only**. Write routes also need the
anti-forgery token. Related notes: [`KNOWN_BUGS.md`](KNOWN_BUGS.md) (review findings and their status)
and [`INTERACTIVITY_PLAN.md`](INTERACTIVITY_PLAN.md) (UI feedback work).

## Features

| Feature | What it does |
|---|---|
| **Roots per server** | Each server instance exposes named roots (`server`, `data`, …) with a display name, a path and a writable flag. `GET …/files` lists them. A read-only root rejects every change. |
| **Directory listing** | `GET …/files/{root}?path=` returns folders and files with size and modified time. Listings are capped at `MaximumListingEntries`. The response says when it was truncated and how many entries were skipped. |
| **Search** | `GET …/search?path=&q=&recursive=` finds files and folders by name below a folder (query up to 200 characters, capped at `MaximumSearchResults`). |
| **Folder size** | `GET …/size?path=` measures a folder (bytes, files, folders) and reports whether the scan completed. |
| **Download** | `GET …/download?path=` streams a file. With `inline=true` it serves raster images (png, jpg, gif, webp, bmp, ico) for the in-page viewer and thumbnails. SVG is deliberately excluded because it would run script in the panel's origin. |
| **Multi-item download** | `POST …/download-archive` zips the chosen files and folders as a tracked background job, then `GET …/download-archive/{archiveId}` fetches the zip once. The temp archive lives in `.panel-tmp` and is removed after download (stale ones are swept after a few hours). |
| **Upload** | `POST …/upload?path=` takes a `multipart/form-data` file. Files over `MaximumFileSizeBytes` are refused with `413` from the `Content-Length` before any data is read. `createFolders=true` creates missing parent folders, which is how folder uploads keep their structure. The write is atomic (temp file, then rename), so a failed upload never leaves a truncated file. |
| **Text editor** | `GET/PUT …/content` reads and saves UTF-8 text up to `MaximumTextFileSizeBytes`. Binary files are refused. The save can carry `expectedModified`. If the file changed on disk since it was opened, the save is rejected instead of overwriting someone else's edit. |
| **Create folder, rename, move** | `mkdir`, `rename` and `move` operate on one path at a time. They fail rather than overwrite an existing item. The UI offers undo for rename and move. |
| **Delete with trash** | `DELETE …/files/{root}?path=` moves the item to `<root>/.trash` and returns a trash entry. `permanent=true` deletes for good. The root itself cannot be deleted. |
| **Trash management** | `GET …/trash` lists deleted items. `POST …/trash/{id}/restore` puts one back at its original path. `DELETE …/trash/{id}` purges one. `DELETE …/trash` empties the trash. Entries older than `TrashRetentionDays` (7) are purged automatically. |
| **Compress / extract** | `POST …/compress` zips selected items and `POST …/extract` unzips an archive into a folder. Both run as tracked background jobs. Extraction is limited by `MaximumArchiveSizeBytes` (2 GB uncompressed) and `MaximumArchiveEntries` (20 000) as a zip-bomb guard, and refuses paths that escape the destination. |
| **Background job tracking** | `GET /api/servers/operations` lists recent jobs (running and finished). `GET …/operations/{taskId}` returns status, progress (items and bytes) and the folder the result landed in. `DELETE …/operations/{taskId}` cancels it. At most `MaximumConcurrentOperations` (2) run at once, and a job is cancelled after `OperationTimeoutMinutes` (30). |
| **Protected paths** | A root can list `ProtectedPaths` (world saves, `Mods`, `serverconfig.json`, …). They are returned with the roots so the UI can ask for explicit confirmation before changing them while the server is online. The confirmation is a UI safeguard. The API itself does not block these paths. |

## Path safety

- Paths are always **relative to a root**. Absolute paths (`/…`, `~…`), `.`/`..` segments and control characters are
  rejected (`400 Invalid relative path`) by the service and again by the helper script, which
  resolves the real path and refuses anything that ends up outside the root.
- Delete and rename act on the item named, and archive extraction validates every entry.
  See `KNOWN_BUGS.md` for the review history of the symlink cases.
- Write operations resolve a *writable* root first, so a read-only root fails before any command
  is built.
- `.trash` and `.panel-tmp` are internal: they are hidden from the root listing, and any path that starts
  with them is rejected outside the trash endpoints. Upload and archive temp files (`.<name>.<pid>.part`) are hidden too.

## Configuration

Section `FileManager` (values below are the code defaults; Development raises
`MaximumFileSizeBytes` to 50 MB):

| Key | Default | Meaning |
|---|---|---|
| `MaximumFileSizeBytes` | 10 MB | Largest upload |
| `MaximumTextFileSizeBytes` | 1 MB | Largest file the text editor opens or saves |
| `MaximumArchiveSizeBytes` | 2 GB | Total uncompressed size allowed when extracting |
| `MaximumArchiveEntries` | 20 000 | Entry count allowed when extracting |
| `MaximumListingEntries` | 2 000 | Rows returned per directory listing |
| `MaximumSearchResults` | 500 | Rows returned per search |
| `MaximumConcurrentOperations` | 2 | Parallel background jobs |
| `OperationTimeoutMinutes` | 30 | Job time limit |
| `TrashRetentionDays` | 7 | How long deleted items can be restored |
| `Instances:<serverId>:Roots:<rootId>` | – | `DisplayName`, `Path`, `Operation`, `IsWritable`, `ProtectedPaths` |

`Operation` is the name of an allowlisted command in the `Remote:Commands` section that runs the
helper for this root (for example `demo-local-data-files`). Add a new root by defining the
command there and a matching entry under `Instances`. The `%WorkspacePath%` placeholder in paths is
replaced with the panel's workspace directory.

## Layout

```text
Endpoints/       FileManagerRoutes (route map), FileManagerEndpoints (multipart handling, streaming, error mapping)
Services/        FileManagerService (validation, limits, text/binary checks, orchestration)
                 BackgroundOperationTracker (job status, progress, cancel, concurrency limit)
                 BoundedStream (stops a stream once it exceeds the size limit)
Persistence/     IFileRepository, RemoteFileRepository (helper-script protocol via RemoteOperations)
Exceptions/      RootNotFound, InvalidRelativePath, FileTooLarge, FileChanged, PermissionDenied, TooManyOperations, …
Tests/Unit/      FileManagerServiceTests
```

## Notes

- Job status is kept **in memory**. It is lost when the panel restarts, and it is not scoped per
  user or per server.
- SSH-mode roots are not covered by the atomic-write and kill-on-abort logic. See `KNOWN_BUGS.md` #6.
- Run the unit tests with `dotnet test Modules/FileManager/Tests/Unit/AlegacyWebPanel.FileManager.UnitTests.csproj`.
