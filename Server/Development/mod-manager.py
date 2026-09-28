#!/usr/bin/env python3
"""Target-side mod helper for the panel's ModManager module.

Usage: mod-manager.py <data_root> <command> [args...]

All paths live under <data_root> (the game server's Data directory):
  Mods/          installed mods
  ModBackups/    the files replaced by the last update batch (one batch is kept)
  .mod-staging/  downloaded files waiting to be applied

Commands:
  scan                              JSON list of installed mods with raw modinfo.json
  game-info                         JSON {gameVersion, loadedMods, logModifiedUtc}
  stage <batch> <filename>          write stdin into .mod-staging/<batch>/<filename>
  apply <batch> (<old|-> <new>)...  swap staged files into Mods, back up the replaced ones
  rollback                          undo the last applied batch
  backup                            JSON journal of the last applied batch (or null)
  discard <batch>                   remove .mod-staging/<batch>

Exit codes are part of the contract with RemoteModRepository:
  1 usage, 2 not permitted, 3 not found, 5 I/O error, 17 already exists, 18 limit exceeded.
JSON goes to stdout; errors go to stderr as "Error: <message>".
"""
import base64
import json
import os
import re
import shutil
import sys
import zipfile
from datetime import datetime, timezone

EXIT_USAGE = 1
EXIT_NOT_PERMITTED = 2
EXIT_NOT_FOUND = 3
EXIT_IO = 5
EXIT_EXISTS = 17
EXIT_LIMIT = 18

MAX_MODINFO_BYTES = 256 * 1024
MAX_STAGE_BYTES = 512 * 1024 * 1024
MAX_ZIP_ENTRIES = 50000
LOG_TAIL_BYTES = 4 * 1024 * 1024
NAME_PATTERN = re.compile(r"^[^/\\\x00]{1,200}$")
BATCH_PATTERN = re.compile(r"^[A-Za-z0-9_-]{1,64}$")
MOD_EXTENSIONS = (".zip", ".cs", ".dll")


def fail(code, message):
    sys.stderr.write("Error: " + message + "\n")
    sys.exit(code)


def emit(value):
    json.dump(value, sys.stdout, ensure_ascii=False)
    sys.stdout.write("\n")


def utc(timestamp):
    return datetime.fromtimestamp(timestamp, timezone.utc).isoformat().replace("+00:00", "Z")


def safe_name(name):
    if not NAME_PATTERN.match(name) or name in (".", "..") or name.startswith("."):
        fail(EXIT_NOT_PERMITTED, f"Invalid file name '{name}'.")
    return name


def safe_batch(batch):
    if not BATCH_PATTERN.match(batch):
        fail(EXIT_NOT_PERMITTED, f"Invalid batch id '{batch}'.")
    return batch


def is_plain_file(path):
    return os.path.isfile(path) and not os.path.islink(path)


def read_zip_modinfo(path):
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len(entries) > MAX_ZIP_ENTRIES:
            raise ValueError("too many entries")
        # The game reads modinfo.json from the archive root only.
        entry = next((e for e in entries if e.filename.lower() == "modinfo.json"), None)
        if entry is None:
            return None
        if entry.file_size > MAX_MODINFO_BYTES:
            raise ValueError("modinfo.json is too large")
        return archive.read(entry)


def scan_entry(mods_dir, name):
    path = os.path.join(mods_dir, name)
    stat = os.lstat(path)
    item = {"fileName": name, "sizeBytes": stat.st_size, "modifiedUtc": utc(stat.st_mtime), "kind": None, "modInfo": None, "error": None}
    try:
        if os.path.islink(path):
            item["kind"] = "link"
            item["error"] = "Symbolic links are not managed."
        elif os.path.isdir(path):
            item["kind"] = "directory"
            info_path = os.path.join(path, "modinfo.json")
            if is_plain_file(info_path) and os.path.getsize(info_path) <= MAX_MODINFO_BYTES:
                with open(info_path, "rb") as handle:
                    item["modInfo"] = base64.b64encode(handle.read()).decode("ascii")
            else:
                return None
        elif name.lower().endswith(".zip"):
            item["kind"] = "zip"
            raw = read_zip_modinfo(path)
            if raw is not None:
                item["modInfo"] = base64.b64encode(raw).decode("ascii")
        elif name.lower().endswith((".cs", ".dll")):
            item["kind"] = "code"
        else:
            return None
    except (OSError, zipfile.BadZipFile, ValueError) as exception:
        item["error"] = str(exception)
    return item


def cmd_scan(root):
    mods_dir = os.path.join(root, "Mods")
    if not os.path.isdir(mods_dir):
        fail(EXIT_NOT_FOUND, "Mods directory does not exist.")
    items = []
    for name in sorted(os.listdir(mods_dir), key=str.lower):
        if name.startswith("."):
            continue
        entry = scan_entry(mods_dir, name)
        if entry is not None:
            items.append(entry)
    emit(items)


def cmd_game_info(root):
    log_path = os.path.join(root, "Logs", "server-main.log")
    result = {"gameVersion": None, "loadedMods": None, "logModifiedUtc": None}
    if is_plain_file(log_path):
        result["logModifiedUtc"] = utc(os.path.getmtime(log_path))
        with open(log_path, "rb") as handle:
            size = handle.seek(0, os.SEEK_END)
            handle.seek(max(0, size - LOG_TAIL_BYTES))
            text = handle.read().decode("utf-8", errors="replace")
        versions = re.findall(r"Game Version: v?([0-9][^\s]*)", text)
        if versions:
            result["gameVersion"] = versions[-1]
        loaded = re.findall(r"Mods, sorted by dependency: ([^\r\n]*)", text)
        if loaded:
            result["loadedMods"] = [m.strip() for m in loaded[-1].split(",") if m.strip()]
    emit(result)


def cmd_stage(root, batch, name):
    batch, name = safe_batch(batch), safe_name(name)
    if not name.lower().endswith(MOD_EXTENSIONS):
        fail(EXIT_NOT_PERMITTED, "Only .zip, .cs and .dll mod files can be staged.")
    directory = os.path.join(root, ".mod-staging", batch)
    os.makedirs(directory, exist_ok=True)
    target = os.path.join(directory, name)
    temp = target + ".part"
    written = 0
    try:
        with open(temp, "wb") as handle:
            while True:
                chunk = sys.stdin.buffer.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > MAX_STAGE_BYTES:
                    handle.close()
                    os.remove(temp)
                    fail(EXIT_LIMIT, "Staged file is too large.")
                handle.write(chunk)
        os.replace(temp, target)
    except OSError as exception:
        fail(EXIT_IO, str(exception))
    emit({"fileName": name, "sizeBytes": written})


def backup_dir(root):
    return os.path.join(root, "ModBackups")


def read_journal(root):
    path = os.path.join(backup_dir(root), "journal.json")
    if not is_plain_file(path):
        return None
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def cmd_apply(root, batch, pairs):
    batch = safe_batch(batch)
    if not pairs or len(pairs) % 2 != 0:
        fail(EXIT_USAGE, "apply expects <old|-> <new> pairs.")
    mods_dir = os.path.join(root, "Mods")
    staging = os.path.join(root, ".mod-staging", batch)
    items = []
    for index in range(0, len(pairs), 2):
        old = None if pairs[index] == "-" else safe_name(pairs[index])
        new = safe_name(pairs[index + 1])
        if old is not None and not os.path.lexists(os.path.join(mods_dir, old)):
            fail(EXIT_NOT_FOUND, f"Installed file '{old}' does not exist.")
        if not is_plain_file(os.path.join(staging, new)):
            fail(EXIT_NOT_FOUND, f"Staged file '{new}' does not exist.")
        items.append({"old": old, "new": new})

    removed = {item["old"] for item in items if item["old"]}
    for item in items:
        if os.path.lexists(os.path.join(mods_dir, item["new"])) and item["new"] not in removed:
            fail(EXIT_EXISTS, f"'{item['new']}' already exists in Mods.")

    # One batch of backups is kept: the previous one is dropped before the new swap.
    backups = backup_dir(root)
    shutil.rmtree(backups, ignore_errors=True)
    os.makedirs(os.path.join(backups, "files"))

    done = []  # (action, name) for reversal
    try:
        for item in items:
            if item["old"]:
                os.replace(os.path.join(mods_dir, item["old"]), os.path.join(backups, "files", item["old"]))
                done.append(("backed-up", item["old"]))
        for item in items:
            os.replace(os.path.join(staging, item["new"]), os.path.join(mods_dir, item["new"]))
            done.append(("installed", item["new"]))
    except OSError as exception:
        for action, name in reversed(done):
            try:
                if action == "installed":
                    os.replace(os.path.join(mods_dir, name), os.path.join(staging, name))
                else:
                    os.replace(os.path.join(backups, "files", name), os.path.join(mods_dir, name))
            except OSError:
                pass
        fail(EXIT_IO, f"Apply failed and was reverted: {exception}")

    journal = {"batchId": batch, "appliedUtc": utc(datetime.now(timezone.utc).timestamp()), "items": items}
    with open(os.path.join(backups, "journal.json"), "w", encoding="utf-8") as handle:
        json.dump(journal, handle)
    shutil.rmtree(staging, ignore_errors=True)
    emit(journal)


def cmd_rollback(root):
    journal = read_journal(root)
    if journal is None:
        fail(EXIT_NOT_FOUND, "There is no update to roll back.")
    mods_dir = os.path.join(root, "Mods")
    files = os.path.join(backup_dir(root), "files")
    for item in journal["items"]:
        if item["old"] and not is_plain_file(os.path.join(files, item["old"])) and not os.path.isdir(os.path.join(files, item["old"])):
            fail(EXIT_NOT_FOUND, f"Backup of '{item['old']}' is missing.")
    try:
        for item in journal["items"]:
            new_path = os.path.join(mods_dir, item["new"])
            if os.path.lexists(new_path) and item["new"] != item["old"]:
                os.remove(new_path)
        for item in journal["items"]:
            if item["old"]:
                os.replace(os.path.join(files, item["old"]), os.path.join(mods_dir, item["old"]))
    except OSError as exception:
        fail(EXIT_IO, str(exception))
    shutil.rmtree(backup_dir(root), ignore_errors=True)
    emit(journal)


def cmd_backup(root):
    emit(read_journal(root))


def cmd_discard(root, batch):
    shutil.rmtree(os.path.join(root, ".mod-staging", safe_batch(batch)), ignore_errors=True)
    emit({"discarded": batch})


def main(argv):
    if len(argv) < 3:
        fail(EXIT_USAGE, "Usage: mod-manager.py <data_root> <command> [args...]")
    root = os.path.realpath(argv[1])
    command, args = argv[2], argv[3:]
    if not os.path.isdir(root):
        fail(EXIT_NOT_FOUND, "Data root does not exist.")
    if command == "scan" and not args:
        cmd_scan(root)
    elif command == "game-info" and not args:
        cmd_game_info(root)
    elif command == "stage" and len(args) == 2:
        cmd_stage(root, *args)
    elif command == "apply" and len(args) >= 3:
        cmd_apply(root, args[0], args[1:])
    elif command == "rollback" and not args:
        cmd_rollback(root)
    elif command == "backup" and not args:
        cmd_backup(root)
    elif command == "discard" and len(args) == 1:
        cmd_discard(root, args[0])
    else:
        fail(EXIT_USAGE, f"Unknown command or wrong arguments: {command}")


if __name__ == "__main__":
    main(sys.argv)
