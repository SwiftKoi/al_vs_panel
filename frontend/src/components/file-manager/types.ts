export interface FileItem {
  name: string;
  isFolder: boolean;
  size: string;
  modified: string;
}

export type ActiveModal = "upload" | "rename" | "move" | "pack" | "unpack" | "delete" | "mkdir" | null;
export type FileSortField = "name" | "size" | "modified";
export type SortDirection = "asc" | "desc";
