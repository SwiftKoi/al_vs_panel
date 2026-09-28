#!/usr/bin/env python3
"""Target-side file helper for the panel's FileManager module.

Usage: file-manager.py <root_id> <root_path> <command> [args...]

Exit codes are part of the contract with RemoteFileRepository, which maps them
to domain errors:
  1 usage, 2 path outside root / not permitted, 3 not found, 4 wrong entry type,
  5 I/O error, 17 already exists, 18 limit exceeded.
"""
import json
import os
import shutil
import sys
import time
import zipfile
from datetime import datetime, timezone

EXIT_USAGE = 1
EXIT_NOT_PERMITTED = 2
EXIT_NOT_FOUND = 3
EXIT_WRONG_TYPE = 4
EXIT_IO = 5
EXIT_EXISTS = 17
EXIT_LIMIT = 18


def fail(code, message):
    sys.stderr.write(message + "\n")
    sys.exit(code)


def inside(root, path):
    return path == root or path.startswith(root + os.sep)


def resolve(root, relative):
    """Resolves a path following every symlink. Used where the *content* is meant
    (listing, reading, writing a file's bytes, archive sources)."""
    path = os.path.realpath(os.path.join(root, relative.lstrip("/")))
    if not inside(root, path):
        fail(EXIT_NOT_PERMITTED, "Error: Path is outside the root.")
    return path


def resolve_entry(root, relative):
    """Resolves the directory entry itself: the parent is resolved, the last
    component is kept as-is. Used where the *entry* is meant (delete, rename,
    move, mkdir) so that acting on a symlink affects the link, not its target."""
    relative = relative.strip("/")
    if is_trash_path(relative):
        fail(EXIT_NOT_PERMITTED, "Error: The trash can only be changed through the trash commands.")
    name = os.path.basename(relative)
    if not relative or name in ("", ".", ".."):
        fail(EXIT_NOT_PERMITTED, "Error: The root itself cannot be changed.")
    parent = os.path.realpath(os.path.join(root, os.path.dirname(relative)))
    if not inside(root, parent):
        fail(EXIT_NOT_PERMITTED, "Error: Path is outside the root.")
    return os.path.join(parent, name)


class Progress:
    """Reports progress as JSON lines on stdout, at most twice a second plus a final line.
    The panel parses these to show files and bytes processed."""

    def __init__(self, total_items, total_bytes):
        self.total_items = total_items
        self.total_bytes = total_bytes
        self.items = 0
        self.bytes = 0
        self.last = 0.0

    def add(self, items, size):
        self.items += items
        self.bytes += size
        now = time.monotonic()
        if now - self.last >= 0.5:
            self.emit()

    def emit(self):
        self.last = time.monotonic()
        sys.stdout.write(json.dumps({
            "items": self.items, "totalItems": self.total_items,
            "bytes": self.bytes, "totalBytes": self.total_bytes,
        }) + "\n")
        sys.stdout.flush()


TRASH_DIR = ".trash"
# Scratch space for archives built for download; files are removed after download or when stale.
TMP_DIR = ".panel-tmp"
HIDDEN_DIRS = (TRASH_DIR, TMP_DIR)


def is_trash_path(relative):
    return relative.strip("/").split("/")[0] in HIDDEN_DIRS


def is_temp_file(name):
    # Upload and archive temp files: ".<name>.<pid>.part"
    return name.startswith(".") and name.endswith(".part")


def temp_path_for(path):
    return os.path.join(os.path.dirname(path), f".{os.path.basename(path)}.{os.getpid()}.part")


def describe(path, name):
    is_dir = os.path.isdir(path)
    return {
        "name": name,
        "isFolder": is_dir,
        "size": 0 if is_dir else os.path.getsize(path),
        "modified": datetime.fromtimestamp(os.path.getmtime(path), tz=timezone.utc).isoformat(),
    }


def list_directory(root, relative, limit):
    target = resolve(root, relative)
    if not os.path.exists(target):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    if not os.path.isdir(target):
        fail(EXIT_WRONG_TYPE, f"Error: Path '{relative}' is not a directory.")
    try:
        names = os.listdir(target)
    except OSError as e:
        fail(EXIT_IO, f"Error reading directory: {e}")

    entries = []
    skipped = 0
    for name in names:
        if is_temp_file(name) or (target == root and name in HIDDEN_DIRS):
            continue
        resolved = os.path.realpath(os.path.join(target, name))
        if not inside(root, resolved):
            skipped += 1  # symlink pointing outside the root
            continue
        try:
            entries.append(describe(resolved, name))
        except OSError:
            skipped += 1  # dangling link, permission denied, removed meanwhile

    entries.sort(key=lambda x: (not x["isFolder"], x["name"].lower()))
    truncated = limit is not None and len(entries) > limit
    if truncated:
        entries = entries[:limit]
    sys.stdout.write(json.dumps({"entries": entries, "truncated": truncated, "skipped": skipped}))


def stat_entry(root, relative):
    target = resolve(root, relative)
    if not os.path.exists(target):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    try:
        sys.stdout.write(json.dumps(describe(target, os.path.basename(target))))
    except OSError as e:
        fail(EXIT_IO, f"Error reading metadata: {e}")


def find_entries(root, relative, query, recursive, limit):
    """Case-insensitive name search. Stops at `limit` results or after a time budget,
    reporting `truncated` so the UI can say the list is incomplete."""
    start = resolve(root, relative)
    if not os.path.isdir(start):
        fail(EXIT_NOT_FOUND, f"Error: Folder '{relative}' does not exist.")
    needle = query.lower()
    if not needle:
        fail(EXIT_USAGE, "Error: Search text is required.")

    deadline = time.monotonic() + 15
    results = []
    truncated = False

    def visit(directory, names):
        nonlocal truncated
        for name in names:
            if is_temp_file(name) or needle not in name.lower():
                continue
            path = os.path.join(directory, name)
            resolved = os.path.realpath(path)
            if not inside(root, resolved):
                continue
            try:
                entry = describe(resolved, name)
            except OSError:
                continue
            entry["path"] = os.path.relpath(path, root)
            results.append(entry)
            if len(results) >= limit:
                truncated = True
                return False
        if time.monotonic() > deadline:
            truncated = True
            return False
        return True

    if recursive:
        # os.walk does not follow directory symlinks, so a link loop cannot trap it.
        for directory, dirs, files in os.walk(start):
            if directory == root:
                dirs[:] = [d for d in dirs if d not in HIDDEN_DIRS]
            dirs.sort(key=str.lower)
            if not visit(directory, sorted(dirs + files, key=str.lower)):
                break
    else:
        try:
            names = [n for n in os.listdir(start) if not (start == root and n in HIDDEN_DIRS)]
            visit(start, sorted(names, key=str.lower))
        except OSError as e:
            fail(EXIT_IO, f"Error reading directory: {e}")

    sys.stdout.write(json.dumps({"results": results, "truncated": truncated}))


def measure(root, relative):
    """Total size of a folder (or file): bytes, files and folders; stops after 20 s."""
    start = resolve(root, relative)
    if not os.path.exists(start):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    deadline = time.monotonic() + 20
    total = files = folders = 0
    complete = True
    if os.path.isdir(start):
        for directory, dirs, names in os.walk(start):
            if directory == root:
                dirs[:] = [d for d in dirs if d not in HIDDEN_DIRS]
            folders += len(dirs)
            for name in names:
                try:
                    total += os.lstat(os.path.join(directory, name)).st_size
                    files += 1
                except OSError:
                    pass
            if time.monotonic() > deadline:
                complete = False
                break
    else:
        total, files = os.path.getsize(start), 1
    sys.stdout.write(json.dumps({"bytes": total, "files": files, "folders": folders, "complete": complete}))


def read_file(root, relative):
    target = resolve(root, relative)
    if not os.path.exists(target):
        fail(EXIT_NOT_FOUND, f"Error: File '{relative}' does not exist.")
    if os.path.isdir(target):
        fail(EXIT_WRONG_TYPE, f"Error: Path '{relative}' is a directory.")
    try:
        with open(target, "rb") as f:
            shutil.copyfileobj(f, sys.stdout.buffer, 64 * 1024)
        sys.stdout.buffer.flush()
    except OSError as e:
        fail(EXIT_IO, f"Error reading file: {e}")


def write_file(root, relative, create_parents=False):
    if is_trash_path(relative):
        fail(EXIT_NOT_PERMITTED, "Error: Uploads into internal folders are not allowed.")
    target = resolve(root, relative)
    parent = os.path.dirname(target)
    if create_parents and not os.path.lexists(parent):
        # Folder uploads: create the missing folders (confined to the root by resolve()).
        try:
            os.makedirs(parent, exist_ok=True)
        except OSError as e:
            fail(EXIT_IO, f"Error creating folders: {e}")
    if not os.path.isdir(parent):
        fail(EXIT_NOT_FOUND, "Error: Parent directory does not exist.")
    if os.path.isdir(target):
        fail(EXIT_WRONG_TYPE, f"Error: Path '{relative}' is a directory.")

    # Sweep leftovers from earlier aborted uploads of the same file: the helper is
    # killed on abort, so it cannot clean up after itself.
    prefix = f".{os.path.basename(target)}."
    try:
        for entry in os.listdir(parent):
            if entry.startswith(prefix) and entry.endswith(".part"):
                try:
                    os.unlink(os.path.join(parent, entry))
                except OSError:
                    pass
    except OSError:
        pass

    # Write to a sibling temp file and rename only once the whole stream has been
    # consumed. Otherwise an aborted upload (size limit hit, client disconnected)
    # would leave a truncated file behind — or replace a good one with it.
    tmp = temp_path_for(target)
    try:
        with open(tmp, "wb") as f:
            shutil.copyfileobj(sys.stdin.buffer, f, 64 * 1024)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, target)
    except BaseException as e:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        if isinstance(e, OSError):
            fail(EXIT_IO, f"Error writing file: {e}")
        raise


def mkdir_directory(root, relative):
    target = resolve_entry(root, relative)
    if os.path.lexists(target):
        fail(EXIT_EXISTS, f"Error: '{relative}' already exists.")
    if not os.path.isdir(os.path.dirname(target)):
        fail(EXIT_NOT_FOUND, "Error: Parent directory does not exist.")
    try:
        os.mkdir(target)
    except OSError as e:
        fail(EXIT_IO, f"Error creating directory: {e}")


def rename_item(root, relative, new_name):
    if not new_name or "/" in new_name or "\\" in new_name or "\0" in new_name or new_name in (".", ".."):
        fail(EXIT_NOT_PERMITTED, "Error: Invalid name for rename.")
    source = resolve_entry(root, relative)
    if not os.path.lexists(source):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    destination = os.path.join(os.path.dirname(source), new_name)
    if destination == source:
        return
    if os.path.lexists(destination):
        fail(EXIT_EXISTS, f"Error: '{new_name}' already exists.")
    try:
        os.rename(source, destination)
    except OSError as e:
        fail(EXIT_IO, f"Error renaming: {e}")


def move_item(root, source_relative, destination_relative):
    source = resolve_entry(root, source_relative)
    destination = resolve_entry(root, destination_relative)
    if not os.path.lexists(source):
        fail(EXIT_NOT_FOUND, f"Error: Source path '{source_relative}' does not exist.")
    if destination == source:
        return
    if os.path.lexists(destination):
        fail(EXIT_EXISTS, f"Error: '{destination_relative}' already exists.")
    if not os.path.isdir(os.path.dirname(destination)):
        fail(EXIT_NOT_FOUND, "Error: Destination directory does not exist.")
    if os.path.isdir(source) and not os.path.islink(source) and inside(source, os.path.dirname(destination)):
        fail(EXIT_NOT_PERMITTED, "Error: A folder cannot be moved into itself.")
    try:
        shutil.move(source, destination)
    except OSError as e:
        fail(EXIT_IO, f"Error moving: {e}")


def delete_item(root, relative):
    target = resolve_entry(root, relative)
    if not os.path.lexists(target):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    try:
        if os.path.isdir(target) and not os.path.islink(target):
            shutil.rmtree(target)
        else:
            os.unlink(target)  # files and symlinks: remove the entry, never the link target
    except OSError as e:
        fail(EXIT_IO, f"Error deleting: {e}")


def purge_tmp(root, max_age_hours=6):
    base = os.path.join(root, TMP_DIR)
    if not os.path.isdir(base):
        return
    cutoff = time.time() - max_age_hours * 3600
    for name in os.listdir(base):
        path = os.path.join(base, name)
        try:
            if os.path.getmtime(path) < cutoff:
                os.unlink(path)
        except OSError:
            pass


def tmp_name_ok(name):
    return bool(name) and all(c.isalnum() or c in "-_" for c in name)


def zip_to_tmp(root, name, source_relatives):
    if not tmp_name_ok(name):
        fail(EXIT_NOT_PERMITTED, "Error: Invalid archive name.")
    purge_tmp(root)
    os.makedirs(os.path.join(root, TMP_DIR), exist_ok=True)
    zip_items(root, f"{TMP_DIR}/{name}.zip", source_relatives, internal=True)


def delete_tmp(root, name):
    if not tmp_name_ok(name):
        fail(EXIT_NOT_PERMITTED, "Error: Invalid archive name.")
    try:
        os.unlink(os.path.join(root, TMP_DIR, f"{name}.zip"))
    except FileNotFoundError:
        pass
    except OSError as e:
        fail(EXIT_IO, f"Error removing archive: {e}")


def zip_items(root, zip_relative, source_relatives, internal=False):
    destination = os.path.join(root, zip_relative) if internal else resolve_entry(root, zip_relative)
    if os.path.lexists(destination):
        fail(EXIT_EXISTS, f"Error: '{zip_relative}' already exists.")
    if not os.path.isdir(os.path.dirname(destination)):
        fail(EXIT_NOT_FOUND, "Error: Destination directory does not exist.")

    # Validate every source before creating anything, so a bad path cannot leave
    # a half-written archive behind.
    sources = []
    for relative in source_relatives:
        if not relative.strip("/"):
            continue
        source = resolve(root, relative)
        if not os.path.exists(source):
            fail(EXIT_NOT_FOUND, f"Error: Source path '{relative}' does not exist.")
        sources.append(source)
    if not sources:
        fail(EXIT_USAGE, "Error: Nothing to archive.")

    # Collect the work first so progress has a known total.
    work = []  # (path, arcname, size)
    for source in sources:
        if not os.path.isdir(source):
            work.append((source, os.path.basename(source), os.path.getsize(source)))
            continue
        base = os.path.dirname(source)
        for walk_root, walk_dirs, walk_files in os.walk(source):
            arc_dir = os.path.relpath(walk_root, base)
            if not walk_dirs and not walk_files:
                work.append((walk_root, arc_dir, 0))  # keep empty folders
            for name in walk_files:
                if is_temp_file(name):
                    continue
                file_path = os.path.join(walk_root, name)
                resolved = os.path.realpath(file_path)
                if not inside(root, resolved) or not os.path.isfile(resolved):
                    continue
                work.append((file_path, os.path.join(arc_dir, name), os.path.getsize(resolved)))

    progress = Progress(len(work), sum(size for _, _, size in work))
    progress.emit()
    tmp = temp_path_for(destination)
    try:
        with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zipf:
            for path, arcname, size in work:
                zipf.write(path, arcname)
                progress.add(1, size)
        os.replace(tmp, destination)
        progress.emit()
    except BaseException as e:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        if isinstance(e, OSError):
            fail(EXIT_IO, f"Error archiving: {e}")
        raise


def unzip_archive(root, zip_relative, destination_relative, max_bytes, max_entries):
    zip_path = resolve(root, zip_relative)
    destination = resolve(root, destination_relative)
    if not os.path.exists(zip_path):
        fail(EXIT_NOT_FOUND, f"Error: Zip file '{zip_relative}' does not exist.")
    if not os.path.isfile(zip_path):
        fail(EXIT_WRONG_TYPE, f"Error: '{zip_relative}' is not a file.")
    if os.path.exists(destination) and not os.path.isdir(destination):
        fail(EXIT_WRONG_TYPE, f"Error: '{destination_relative}' is not a directory.")

    try:
        with zipfile.ZipFile(zip_path, "r") as zipf:
            members = zipf.infolist()

            # Check everything before writing anything: limits, zip-slip, and
            # files that would be overwritten.
            if max_entries is not None and len(members) > max_entries:
                fail(EXIT_LIMIT, f"Error: Archive has {len(members)} entries; the limit is {max_entries}.")
            total = sum(m.file_size for m in members)
            if max_bytes is not None and total > max_bytes:
                fail(EXIT_LIMIT, f"Error: Archive expands to {total} bytes; the limit is {max_bytes}.")
            existing_parent = destination
            while not os.path.exists(existing_parent):
                existing_parent = os.path.dirname(existing_parent)
            free = shutil.disk_usage(existing_parent).free
            if total > free:
                fail(EXIT_LIMIT, f"Error: Archive expands to {total} bytes but only {free} bytes are free.")

            conflicts = []
            for member in members:
                target = os.path.realpath(os.path.join(destination, member.filename))
                if not inside(destination, target):
                    fail(EXIT_NOT_PERMITTED, f"Error: Archive entry '{member.filename}' points outside the destination.")
                if not member.is_dir() and os.path.lexists(target):
                    conflicts.append(member.filename)
            if conflicts:
                shown = ", ".join(conflicts[:5]) + (" …" if len(conflicts) > 5 else "")
                fail(EXIT_EXISTS, f"Error: {len(conflicts)} file(s) already exist in the destination: {shown}")

            os.makedirs(destination, exist_ok=True)
            progress = Progress(len(members), total)
            progress.emit()
            for member in members:
                zipf.extract(member, destination)
                progress.add(1, member.file_size)
            progress.emit()
    except zipfile.BadZipFile as e:
        fail(EXIT_WRONG_TYPE, f"Error: Not a valid zip archive: {e}")
    except OSError as e:
        fail(EXIT_IO, f"Error extracting archive: {e}")


def trash_root(root):
    return os.path.join(root, TRASH_DIR)


def entry_size(path):
    if os.path.islink(path) or not os.path.isdir(path):
        return os.lstat(path).st_size
    total = 0
    for walk_root, _, files in os.walk(path):
        for name in files:
            try:
                total += os.lstat(os.path.join(walk_root, name)).st_size
            except OSError:
                pass
    return total


def read_trash_entry(entry_dir):
    try:
        with open(os.path.join(entry_dir, "meta.json"), encoding="utf-8") as f:
            meta = json.load(f)
        if not os.path.lexists(os.path.join(entry_dir, "item")):
            return None
        return meta
    except (OSError, ValueError):
        return None


def purge_expired(root, retention_days):
    """Permanently removes trash entries older than the retention period."""
    if retention_days is None:
        return
    base = trash_root(root)
    if not os.path.isdir(base):
        return
    cutoff = time.time() - retention_days * 86400
    for entry in os.listdir(base):
        entry_dir = os.path.join(base, entry)
        meta = read_trash_entry(entry_dir)
        deleted = meta.get("deletedAtEpoch", 0) if meta else os.path.getmtime(entry_dir)
        if deleted < cutoff:
            shutil.rmtree(entry_dir, ignore_errors=True)


def trash_entry_dir(root, trash_id):
    # Ids are generated by trash_item; anything else is rejected, so an id can
    # never point outside the trash folder.
    if not trash_id or not all(c.isdigit() or c == "-" for c in trash_id):
        fail(EXIT_NOT_PERMITTED, "Error: Invalid trash id.")
    entry_dir = os.path.join(trash_root(root), trash_id)
    if not os.path.isdir(entry_dir):
        fail(EXIT_NOT_FOUND, "Error: That item is no longer in the trash.")
    return entry_dir


def trash_item(root, relative, retention_days):
    """Moves an entry into <root>/.trash/<id>/item with its original path in meta.json.
    A rename on the same filesystem: instant, nothing is copied or lost."""
    target = resolve_entry(root, relative)
    if not os.path.lexists(target):
        fail(EXIT_NOT_FOUND, f"Error: Path '{relative}' does not exist.")
    purge_expired(root, retention_days)

    trash_id = f"{int(time.time() * 1000)}-{os.getpid()}"
    entry_dir = os.path.join(trash_root(root), trash_id)
    try:
        os.makedirs(entry_dir)
        meta = {
            "id": trash_id,
            "originalPath": os.path.relpath(target, root),
            "name": os.path.basename(target),
            "isFolder": os.path.isdir(target) and not os.path.islink(target),
            "size": entry_size(target),
            "deletedAt": datetime.now(timezone.utc).isoformat(),
            "deletedAtEpoch": time.time(),
        }
        with open(os.path.join(entry_dir, "meta.json"), "w", encoding="utf-8") as f:
            json.dump(meta, f)
        os.rename(target, os.path.join(entry_dir, "item"))
    except OSError as e:
        shutil.rmtree(entry_dir, ignore_errors=True)
        fail(EXIT_IO, f"Error moving to trash: {e}")
    sys.stdout.write(json.dumps(meta))


def list_trash(root, retention_days):
    purge_expired(root, retention_days)
    base = trash_root(root)
    entries = []
    if os.path.isdir(base):
        for entry in os.listdir(base):
            meta = read_trash_entry(os.path.join(base, entry))
            if meta:
                entries.append(meta)
    entries.sort(key=lambda m: m.get("deletedAtEpoch", 0), reverse=True)
    sys.stdout.write(json.dumps(entries))


def restore_trash(root, trash_id):
    entry_dir = trash_entry_dir(root, trash_id)
    meta = read_trash_entry(entry_dir)
    if not meta:
        fail(EXIT_NOT_FOUND, "Error: That trash entry is damaged or empty.")
    destination = resolve_entry(root, meta["originalPath"])
    if os.path.lexists(destination):
        fail(EXIT_EXISTS, f"Error: '{meta['originalPath']}' already exists; rename or move it first.")
    try:
        os.makedirs(os.path.dirname(destination), exist_ok=True)
        os.rename(os.path.join(entry_dir, "item"), destination)
        shutil.rmtree(entry_dir, ignore_errors=True)
    except OSError as e:
        fail(EXIT_IO, f"Error restoring: {e}")
    sys.stdout.write(json.dumps(meta))


def purge_trash(root, trash_id):
    if trash_id == "*":
        shutil.rmtree(trash_root(root), ignore_errors=True)
        return
    shutil.rmtree(trash_entry_dir(root, trash_id), ignore_errors=True)


def optional_int(value):
    return int(value) if value not in (None, "") else None


def main():
    if len(sys.argv) < 4:
        fail(EXIT_USAGE, "Usage: file-manager.py <root_id> <root_path> <command> [args...]")

    root = os.path.realpath(sys.argv[2])
    command = sys.argv[3]
    args = sys.argv[4:]

    def arg(index):
        return args[index] if len(args) > index else ""

    if command == "list":
        list_directory(root, arg(0), optional_int(arg(1)))
    elif command == "find":
        # find <folder> <text> <recursive 0|1> [<limit>]
        find_entries(root, arg(0), arg(1), arg(2) == "1", optional_int(arg(3)) or 500)
    elif command == "size":
        measure(root, arg(0))
    elif command == "stat":
        stat_entry(root, arg(0))
    elif command == "read":
        read_file(root, arg(0))
    elif command == "write":
        # write <path> [1 = create missing parent folders]
        write_file(root, arg(0), arg(1) == "1")
    elif command == "mkdir":
        mkdir_directory(root, arg(0))
    elif command == "rename":
        rename_item(root, arg(0), arg(1))
    elif command == "move":
        move_item(root, arg(0), arg(1))
    elif command == "delete":
        # Permanent. The panel normally uses "trash" instead.
        delete_item(root, arg(0))
    elif command == "trash":
        # trash <path> [<retention_days>]
        trash_item(root, arg(0), optional_int(arg(1)))
    elif command == "trash-list":
        list_trash(root, optional_int(arg(0)))
    elif command == "trash-restore":
        restore_trash(root, arg(0))
    elif command == "trash-purge":
        # trash-purge <id> | trash-purge "*"
        purge_trash(root, arg(0))
    elif command == "zip":
        # zip <destination.zip> <source> [<source>...]
        zip_items(root, arg(0), args[1:])
    elif command == "zip-tmp":
        # zip-tmp <name> <source> [<source>...]  -> <root>/.panel-tmp/<name>.zip
        zip_to_tmp(root, arg(0), args[1:])
    elif command == "tmp-delete":
        delete_tmp(root, arg(0))
    elif command == "unzip":
        # unzip <archive.zip> <destination> [<max_bytes> <max_entries>]
        unzip_archive(root, arg(0), arg(1), optional_int(arg(2)), optional_int(arg(3)))
    else:
        fail(EXIT_USAGE, f"Error: Unknown command '{command}'")


if __name__ == "__main__":
    main()
