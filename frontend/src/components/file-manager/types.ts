export interface FileItem {
  name: string;
  isFolder: boolean;
  size: string;
  modified: string;
  /** Raw values for sorting; the strings above are display-only. */
  sizeBytes: number;
  modifiedMs: number;
}

export type ActiveModal = "upload" | "rename" | "move" | "pack" | "unpack" | "delete" | "mkdir" | null;
export type FileSortField = "name" | "size" | "modified";
export type SortDirection = "asc" | "desc";
