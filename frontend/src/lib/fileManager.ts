import type { FileEntryDto } from "@/api/files";
import type { FileItem, FileSortField, SortDirection } from "@/components/file-manager/types";

export function formatFileSize(size: number): string {
  if (size >= 1024 * 1024 * 1024) return `${(size / (1024 * 1024 * 1024)).toFixed(1)} GB`;
  if (size >= 1024 * 1024) return `${(size / (1024 * 1024)).toFixed(1)} MB`;
  if (size >= 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${size} B`;
}

export function formatModifiedDate(value: string): string {
  try {
    return new Date(value).toLocaleString("ru-RU");
  } catch {
    return value;
  }
}

export function mapFileEntry(entry: FileEntryDto): FileItem {
  return {
    name: entry.name,
    isFolder: entry.isFolder,
    size: entry.isFolder ? "—" : formatFileSize(entry.size),
    modified: formatModifiedDate(entry.modified),
    sizeBytes: entry.isFolder ? -1 : entry.size,
    modifiedMs: Date.parse(entry.modified) || 0
  };
}

export function sortFileItems(items: FileItem[], field: FileSortField, direction: SortDirection): FileItem[] {
  return [...items].sort((a, b) => {
    if (a.isFolder !== b.isFolder) return a.isFolder ? -1 : 1;
    const result = field === "name"
      ? a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: "base" })
      : field === "size"
        ? a.sizeBytes - b.sizeBytes
        : a.modifiedMs - b.modifiedMs;
    return direction === "asc" ? result : -result;
  });
}

/** Default archive name: a file loses only its last extension, a folder keeps its full name. */
export function defaultArchiveName(items: FileItem[]): string {
  if (items.length !== 1) return "archive.zip";
  const { name, isFolder } = items[0];
  if (name.toLowerCase().endsWith(".zip")) return name;
  const dot = name.lastIndexOf(".");
  const base = !isFolder && dot > 0 ? name.slice(0, dot) : name;
  return `${base}.zip`;
}

/** Normalises a user-typed folder path; "" and "/" both mean the root. */
export function normalizeFolderPath(value: string): string {
  return value.trim().split("/").filter(Boolean).join("/");
}
