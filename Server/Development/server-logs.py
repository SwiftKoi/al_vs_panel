#!/usr/bin/env python3
"""Target-side, read-only log reader for the panel's ServerLogs module.

Usage: server-logs.py <data_root> <command> [args...]

Only the game's own logs are reachable: server-{main,audit,debug,chat}.log in <data_root>/Logs,
in <data_root>/Logs/Archive and in <data_root>/Logs/Archive/<folder>. Files are opened read-only;
the game keeps appending while we read.

Commands:
  list                          JSON list of log files: {path, size, modifiedUtc, identity}
                                identity = sha1 of the file's first line (stable when the game
                                moves the file into Archive/); null until the first line is complete
  read <path> <offset> <max>    raw bytes from <offset>, ending at the last complete line
                                (or exactly <max> bytes when one line is longer than that)

Exit codes: 1 usage, 2 not permitted, 3 not found, 5 I/O error.
"""
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone

EXIT_USAGE = 1
EXIT_NOT_PERMITTED = 2
EXIT_NOT_FOUND = 3
EXIT_IO = 5

LOG_NAME = re.compile(r"^server-(main|audit|debug|chat)\.log$")
ARCHIVE_FOLDER = re.compile(r"^[A-Za-z0-9_.-]{1,64}$")
MAX_FIRST_LINE = 4096
MAX_READ = 16 * 1024 * 1024


def fail(code, message):
    sys.stderr.write("Error: " + message + "\n")
    sys.exit(code)


def utc(timestamp):
    return datetime.fromtimestamp(timestamp, timezone.utc).isoformat().replace("+00:00", "Z")


def is_plain_file(path):
    return os.path.isfile(path) and not os.path.islink(path)


def candidates(logs):
    """Yields (relative path, absolute path) for every allowed log file."""
    for name in sorted(os.listdir(logs)):
        if LOG_NAME.match(name):
            yield name, os.path.join(logs, name)
    archive = os.path.join(logs, "Archive")
    if not os.path.isdir(archive) or os.path.islink(archive):
        return
    for name in sorted(os.listdir(archive)):
        path = os.path.join(archive, name)
        if LOG_NAME.match(name):
            yield "Archive/" + name, path
        elif ARCHIVE_FOLDER.match(name) and os.path.isdir(path) and not os.path.islink(path):
            for inner in sorted(os.listdir(path)):
                if LOG_NAME.match(inner):
                    yield f"Archive/{name}/{inner}", os.path.join(path, inner)


def identity(path):
    with open(path, "rb") as handle:
        head = handle.read(MAX_FIRST_LINE)
    end = head.find(b"\n")
    if end < 0:
        return None
    return hashlib.sha1(head[:end]).hexdigest()


def cmd_list(logs):
    items = []
    for relative, path in candidates(logs):
        if not is_plain_file(path):
            continue
        try:
            stat = os.stat(path)
            items.append({"path": relative, "size": stat.st_size, "modifiedUtc": utc(stat.st_mtime), "identity": identity(path)})
        except OSError:
            continue  # rotated away between listing and reading
    json.dump(items, sys.stdout)
    sys.stdout.write("\n")


def resolve(logs, relative):
    parts = relative.split("/")
    valid = (
        (len(parts) == 1 and LOG_NAME.match(parts[0]))
        or (len(parts) == 2 and parts[0] == "Archive" and LOG_NAME.match(parts[1]))
        or (len(parts) == 3 and parts[0] == "Archive" and ARCHIVE_FOLDER.match(parts[1])
            and parts[1] not in (".", "..") and LOG_NAME.match(parts[2]))
    )
    if not valid:
        fail(EXIT_NOT_PERMITTED, f"Not an allowed log path: {relative}")
    path = os.path.join(logs, *parts)
    if not os.path.realpath(path).startswith(os.path.realpath(logs) + os.sep) or not is_plain_file(path):
        fail(EXIT_NOT_FOUND, f"Log file not found: {relative}")
    return path


def cmd_read(logs, relative, offset, maximum):
    try:
        offset, maximum = int(offset), int(maximum)
    except ValueError:
        fail(EXIT_USAGE, "Offset and size must be integers.")
    if offset < 0 or maximum <= 0 or maximum > MAX_READ:
        fail(EXIT_USAGE, "Offset or size out of range.")
    path = resolve(logs, relative)
    try:
        with open(path, "rb") as handle:
            handle.seek(offset)
            data = handle.read(maximum)
    except OSError as exception:
        fail(EXIT_IO, str(exception))
    end = data.rfind(b"\n")
    if end >= 0:
        data = data[: end + 1]
    elif len(data) < maximum:
        data = b""  # an incomplete last line: wait for the rest
    sys.stdout.buffer.write(data)


def main(argv):
    if len(argv) < 3:
        fail(EXIT_USAGE, "Usage: server-logs.py <data_root> <command> [args...]")
    logs = os.path.join(os.path.realpath(argv[1]), "Logs")
    command, args = argv[2], argv[3:]
    if not os.path.isdir(logs):
        fail(EXIT_NOT_FOUND, "Logs directory does not exist.")
    if command == "list" and not args:
        cmd_list(logs)
    elif command == "read" and len(args) == 3:
        cmd_read(logs, *args)
    else:
        fail(EXIT_USAGE, f"Unknown command or wrong arguments: {command}")


if __name__ == "__main__":
    main(sys.argv)
