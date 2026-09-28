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
    name = os.path.basename(relative)
    if not relative or name in ("", ".", ".."):
        fail(EXIT_NOT_PERMITTED, "Error: The root itself cannot be changed.")
    parent = os.path.realpath(os.path.join(root, os.path.dirname(relative)))
    if not inside(root, parent):
        fail(EXIT_NOT_PERMITTED, "Error: Path is outside the root.")
    return os.path.join(parent, name)


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
        if is_temp_file(name):
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


def write_file(root, relative):
    target = resolve(root, relative)
    parent = os.path.dirname(target)
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


def zip_items(root, zip_relative, source_relatives):
    destination = resolve_entry(root, zip_relative)
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

    tmp = temp_path_for(destination)
    try:
        with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zipf:
            for source in sources:
                if not os.path.isdir(source):
                    zipf.write(source, os.path.basename(source))
                    continue
                base = os.path.dirname(source)
                for walk_root, walk_dirs, walk_files in os.walk(source):
                    arc_dir = os.path.relpath(walk_root, base)
                    if not walk_dirs and not walk_files:
                        zipf.write(walk_root, arc_dir)  # keep empty folders
                    for name in walk_files:
                        if is_temp_file(name):
                            continue
                        file_path = os.path.join(walk_root, name)
                        resolved = os.path.realpath(file_path)
                        if not inside(root, resolved) or not os.path.isfile(resolved):
                            continue
                        zipf.write(file_path, os.path.join(arc_dir, name))
        os.replace(tmp, destination)
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
            zipf.extractall(destination)
    except zipfile.BadZipFile as e:
        fail(EXIT_WRONG_TYPE, f"Error: Not a valid zip archive: {e}")
    except OSError as e:
        fail(EXIT_IO, f"Error extracting archive: {e}")


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
    elif command == "stat":
        stat_entry(root, arg(0))
    elif command == "read":
        read_file(root, arg(0))
    elif command == "write":
        write_file(root, arg(0))
    elif command == "mkdir":
        mkdir_directory(root, arg(0))
    elif command == "rename":
        rename_item(root, arg(0), arg(1))
    elif command == "move":
        move_item(root, arg(0), arg(1))
    elif command == "delete":
        delete_item(root, arg(0))
    elif command == "zip":
        # zip <destination.zip> <source> [<source>...]
        zip_items(root, arg(0), args[1:])
    elif command == "unzip":
        # unzip <archive.zip> <destination> [<max_bytes> <max_entries>]
        unzip_archive(root, arg(0), arg(1), optional_int(arg(2)), optional_int(arg(3)))
    else:
        fail(EXIT_USAGE, f"Error: Unknown command '{command}'")


if __name__ == "__main__":
    main()
