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
    modified: formatModifiedDate(entry.modified)
  };
}

export function parseSizeBytes(size: string): number {
  if (size === "—" || !size) return -1;
  const match = size.trim().match(/^([\d.]+)\s*(B|KB|MB|GB)?$/i);
  if (!match) return 0;
  const value = parseFloat(match[1]);
  const unit = (match[2] || "B").toUpperCase();
  return unit === "GB" ? value * 1024 ** 3 : unit === "MB" ? value * 1024 ** 2 : unit === "KB" ? value * 1024 : value;
}

export function parseDateMs(value: string): number {
  try {
    const [datePart, timePart] = value.split(",");
    if (!datePart || !timePart) return 0;
    const [day, month, year] = datePart.trim().split(".").map(Number);
    const [hours, minutes, seconds] = timePart.trim().split(":").map(Number);
    return new Date(year, month - 1, day, hours, minutes, seconds).getTime();
  } catch {
    return 0;
  }
}

export function sortFileItems(items: FileItem[], field: FileSortField, direction: SortDirection): FileItem[] {
  return [...items].sort((a, b) => {
    if (a.isFolder !== b.isFolder) return a.isFolder ? -1 : 1;
    const result = field === "name"
      ? a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: "base" })
      : field === "size"
        ? parseSizeBytes(a.size) - parseSizeBytes(b.size)
        : parseDateMs(a.modified) - parseDateMs(b.modified);
    return direction === "asc" ? result : -result;
  });
}
