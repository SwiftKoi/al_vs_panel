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

/** Short human duration: "45 s", "3 min 12 s", "1 h 5 min". */
export function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "—";
  const s = Math.round(seconds);
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min ${s % 60} s`;
  return `${Math.floor(m / 60)} h ${m % 60} min`;
}

export interface DroppedFile {
  file: File;
  /** Sub-folder inside the drop, e.g. "MyMod/assets" ("" for loose files). */
  relDir: string;
}

/**
 * Expands a drop into files, walking dropped folders (webkitGetAsEntry) so their
 * structure can be recreated. Falls back to the flat file list when entries are unavailable.
 */
export async function collectDroppedFiles(dataTransfer: DataTransfer): Promise<DroppedFile[]> {
  const entries = Array.from(dataTransfer.items ?? [])
    .map((item) => (item.kind === "file" ? item.webkitGetAsEntry?.() : null))
    .filter((entry): entry is FileSystemEntry => !!entry);
  if (entries.length === 0) {
    return Array.from(dataTransfer.files).map((file) => ({ file, relDir: "" }));
  }

  const result: DroppedFile[] = [];
  const walk = async (entry: FileSystemEntry, dir: string): Promise<void> => {
    if (entry.isFile) {
      const file = await new Promise<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject));
      result.push({ file, relDir: dir });
      return;
    }
    const reader = (entry as FileSystemDirectoryEntry).createReader();
    const childDir = dir ? `${dir}/${entry.name}` : entry.name;
    // readEntries returns batches (about 100 at a time) until it returns an empty one.
    for (;;) {
      const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject));
      if (batch.length === 0) break;
      for (const child of batch) await walk(child, childDir);
    }
  };
  for (const entry of entries) await walk(entry, "");
  return result;
}

/** Files picked with <input webkitdirectory> carry their path in webkitRelativePath. */
export function filesFromFolderInput(files: FileList): DroppedFile[] {
  return Array.from(files).map((file) => {
    const rel = file.webkitRelativePath || file.name;
    const slash = rel.lastIndexOf("/");
    return { file, relDir: slash === -1 ? "" : rel.slice(0, slash) };
  });
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["year", 365 * 86400], ["month", 30 * 86400], ["week", 7 * 86400],
  ["day", 86400], ["hour", 3600], ["minute", 60]
];

/** "5 minutes ago" style text in the given language; "just now" under a minute. */
export function formatRelativeTime(ms: number, locale: string): string {
  const seconds = (ms - Date.now()) / 1000;
  const format = new Intl.RelativeTimeFormat(locale, { numeric: "auto" });
  for (const [unit, size] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit);
  }
  return format.format(0, "second");
}

const PREVIEW_EXTENSIONS = new Set(["png", "jpg", "jpeg", "gif", "webp", "bmp", "ico"]);

/** Raster images the server will serve inline for preview (SVG is excluded on purpose). */
export function isPreviewableImage(name: string): boolean {
  const dot = name.lastIndexOf(".");
  return dot > 0 && PREVIEW_EXTENSIONS.has(name.slice(dot + 1).toLowerCase());
}
