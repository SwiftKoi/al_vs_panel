#!/usr/bin/env python3
import os
import sys
import json
import shutil
import zipfile
from datetime import datetime, timezone

def print_err(msg):
    sys.stderr.write(msg + "\n")

def list_directory(root_path, relative_path):
    root_path = os.path.realpath(root_path)
    
    # Secure target path resolution
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    # Path confinement check
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    if not os.path.exists(target_path):
        print_err(f"Error: Path '{relative_path}' does not exist.")
        sys.exit(3)
        
    if not os.path.isdir(target_path):
        print_err(f"Error: Path '{relative_path}' is not a directory.")
        sys.exit(4)
        
    try:
        names = os.listdir(target_path)
    except Exception as e:
        print_err(f"Error reading directory: {e}")
        sys.exit(5)
        
    entries = []
    for name in names:
        resolved_entry = os.path.realpath(os.path.join(target_path, name))
        
        # Confinement check for symlinks/entries
        if resolved_entry != root_path and not resolved_entry.startswith(root_path + os.sep):
            continue  # Skip entries that point outside the root boundary
            
        try:
            is_dir = os.path.isdir(resolved_entry)
            size = os.path.getsize(resolved_entry) if not is_dir else 0
            mtime = os.path.getmtime(resolved_entry)
            dt = datetime.fromtimestamp(mtime, tz=timezone.utc)
            
            entries.append({
                "name": name,
                "isFolder": is_dir,
                "size": size,
                "modified": dt.isoformat()
            })
        except Exception:
            continue  # Skip entries that generate errors (e.g. permission issues on individual files)
            
    # Deterministic sorting: folders first, then files, both alphabetically case-insensitive
    entries.sort(key=lambda x: (not x["isFolder"], x["name"].lower()))
    
    sys.stdout.write(json.dumps(entries))

def read_file(root_path, relative_path):
    root_path = os.path.realpath(root_path)
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    if not os.path.exists(target_path):
        print_err(f"Error: File '{relative_path}' does not exist.")
        sys.exit(3)
        
    if os.path.isdir(target_path):
        print_err(f"Error: Path '{relative_path}' is a directory.")
        sys.exit(4)
        
    try:
        with open(target_path, 'rb') as f:
            while True:
                chunk = f.read(64 * 1024)
                if not chunk:
                    break
                sys.stdout.buffer.write(chunk)
        sys.stdout.buffer.flush()
    except Exception as e:
        print_err(f"Error reading file: {e}")
        sys.exit(5)

def write_file(root_path, relative_path):
    root_path = os.path.realpath(root_path)
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    parent_dir = os.path.dirname(target_path)
    if not os.path.exists(parent_dir) or not os.path.isdir(parent_dir):
        print_err("Error: Parent directory does not exist.")
        sys.exit(6)
        
    try:
        with open(target_path, 'wb') as f:
            while True:
                chunk = sys.stdin.buffer.read(64 * 1024)
                if not chunk:
                    break
                f.write(chunk)
    except Exception as e:
        print_err(f"Error writing file: {e}")
        sys.exit(7)

def mkdir_directory(root_path, relative_path):
    root_path = os.path.realpath(root_path)
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    try:
        os.makedirs(target_path, exist_ok=True)
    except Exception as e:
        print_err(f"Error creating directory: {e}")
        sys.exit(8)

def rename_item(root_path, relative_path, new_name):
    root_path = os.path.realpath(root_path)
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    if not os.path.exists(target_path):
        print_err(f"Error: Path '{relative_path}' does not exist.")
        sys.exit(3)
        
    if "/" in new_name or "\\" in new_name or new_name in (".", ".."):
        print_err("Error: Invalid name for rename.")
        sys.exit(9)
        
    parent_dir = os.path.dirname(target_path)
    new_path = os.path.realpath(os.path.join(parent_dir, new_name))
    
    if new_path != root_path and not new_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on destination.")
        sys.exit(2)
        
    try:
        os.rename(target_path, new_path)
    except Exception as e:
        print_err(f"Error renaming: {e}")
        sys.exit(10)

def move_item(root_path, source_relative, dest_relative):
    root_path = os.path.realpath(root_path)
    source_path = os.path.realpath(os.path.join(root_path, source_relative.lstrip("/")))
    dest_path = os.path.realpath(os.path.join(root_path, dest_relative.lstrip("/")))
    
    if source_path != root_path and not source_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on source.")
        sys.exit(2)
        
    if dest_path != root_path and not dest_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on destination.")
        sys.exit(2)
        
    if not os.path.exists(source_path):
        print_err(f"Error: Source path '{source_relative}' does not exist.")
        sys.exit(3)
        
    dest_parent = os.path.dirname(dest_path)
    if not os.path.exists(dest_parent) or not os.path.isdir(dest_parent):
        print_err("Error: Destination parent directory does not exist.")
        sys.exit(11)
        
    try:
        shutil.move(source_path, dest_path)
    except Exception as e:
        print_err(f"Error moving: {e}")
        sys.exit(12)

def delete_item(root_path, relative_path):
    root_path = os.path.realpath(root_path)
    target_path = os.path.realpath(os.path.join(root_path, relative_path.lstrip("/")))
    
    if target_path != root_path and not target_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected.")
        sys.exit(2)
        
    if not os.path.exists(target_path):
        print_err(f"Error: Path '{relative_path}' does not exist.")
        sys.exit(3)
        
    try:
        if os.path.isdir(target_path):
            shutil.rmtree(target_path)
        else:
            os.remove(target_path)
    except Exception as e:
        print_err(f"Error deleting: {e}")
        sys.exit(13)

def zip_directory(root_path, relative_path, zip_relative_path):
    root_path = os.path.realpath(root_path)
    dest_path = os.path.realpath(os.path.join(root_path, zip_relative_path.lstrip("/")))
    
    if dest_path != root_path and not dest_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on destination.")
        sys.exit(2)
        
    dest_parent = os.path.dirname(dest_path)
    if not os.path.exists(dest_parent) or not os.path.isdir(dest_parent):
        print_err("Error: Destination parent directory does not exist.")
        sys.exit(11)
        
    relative_paths = relative_path.split(";")
    
    try:
        with zipfile.ZipFile(dest_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for rel_path in relative_paths:
                if not rel_path.strip():
                    continue
                source_path = os.path.realpath(os.path.join(root_path, rel_path.lstrip("/")))
                
                if source_path != root_path and not source_path.startswith(root_path + os.sep):
                    print_err("Error: Path traversal attempt detected on source.")
                    sys.exit(2)
                    
                if not os.path.exists(source_path):
                    print_err(f"Error: Source path '{rel_path}' does not exist.")
                    sys.exit(3)
                    
                if os.path.isdir(source_path):
                    for walk_root, walk_dirs, walk_files in os.walk(source_path):
                        for file in walk_files:
                            file_path = os.path.join(walk_root, file)
                            resolved_file = os.path.realpath(file_path)
                            if resolved_file == dest_path:
                                continue
                            if resolved_file != root_path and not resolved_file.startswith(root_path + os.sep):
                                continue
                            arcname = os.path.relpath(file_path, os.path.dirname(source_path))
                            zipf.write(file_path, arcname)
                else:
                    zipf.write(source_path, os.path.basename(source_path))
    except Exception as e:
        print_err(f"Error archiving: {e}")
        if os.path.exists(dest_path):
            os.remove(dest_path)
        sys.exit(14)

def unzip_archive(root_path, zip_relative_path, dest_relative_path):
    root_path = os.path.realpath(root_path)
    zip_path = os.path.realpath(os.path.join(root_path, zip_relative_path.lstrip("/")))
    dest_path = os.path.realpath(os.path.join(root_path, dest_relative_path.lstrip("/")))
    
    if zip_path != root_path and not zip_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on zip file.")
        sys.exit(2)
        
    if dest_path != root_path and not dest_path.startswith(root_path + os.sep):
        print_err("Error: Path traversal attempt detected on destination.")
        sys.exit(2)
        
    if not os.path.exists(zip_path):
        print_err(f"Error: Zip file '{zip_relative_path}' does not exist.")
        sys.exit(3)
        
    try:
        dest_dir = os.path.realpath(dest_path)
        os.makedirs(dest_dir, exist_ok=True)
        
        with zipfile.ZipFile(zip_path, 'r') as zipf:
            for member in zipf.infolist():
                target_member_path = os.path.realpath(os.path.join(dest_dir, member.filename))
                if target_member_path != dest_dir and not target_member_path.startswith(dest_dir + os.sep):
                    print_err(f"Error: Zip-slip path traversal attempt blocked for member '{member.filename}'")
                    sys.exit(15)
                zipf.extract(member, dest_dir)
    except Exception as e:
        print_err(f"Error extracting archive: {e}")
        sys.exit(16)

def main():
    if len(sys.argv) < 4:
        print_err("Usage: file-manager.py <root_id> <root_path> <command> [args...]")
        sys.exit(1)
        
    root_id = sys.argv[1]
    root_path = sys.argv[2]
    command = sys.argv[3]
    
    if command == "list":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        list_directory(root_path, relative_path)
    elif command == "read":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        read_file(root_path, relative_path)
    elif command == "write":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        write_file(root_path, relative_path)
    elif command == "mkdir":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        mkdir_directory(root_path, relative_path)
    elif command == "rename":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        new_name = sys.argv[5] if len(sys.argv) > 5 else ""
        rename_item(root_path, relative_path, new_name)
    elif command == "move":
        source_relative = sys.argv[4] if len(sys.argv) > 4 else ""
        dest_relative = sys.argv[5] if len(sys.argv) > 5 else ""
        move_item(root_path, source_relative, dest_relative)
    elif command == "delete":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        delete_item(root_path, relative_path)
    elif command == "zip":
        relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        zip_relative_path = sys.argv[5] if len(sys.argv) > 5 else ""
        zip_directory(root_path, relative_path, zip_relative_path)
    elif command == "unzip":
        zip_relative_path = sys.argv[4] if len(sys.argv) > 4 else ""
        dest_relative_path = sys.argv[5] if len(sys.argv) > 5 else ""
        unzip_archive(root_path, zip_relative_path, dest_relative_path)
    else:
        print_err(f"Error: Unknown command '{command}'")
        sys.exit(1)

if __name__ == "__main__":
    main()
