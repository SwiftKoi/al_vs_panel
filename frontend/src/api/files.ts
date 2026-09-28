import { apiRequest, uploadWithProgress, type UploadProgress } from "@/api/client";

export interface FileRootDto {
  id: string;
  displayName: string;
  isWritable: boolean;
  /** Root-relative paths the live server uses; changing them while it runs needs confirmation. */
  protectedPaths?: string[];
}

export interface TrashEntryDto {
  id: string;
  originalPath: string;
  name: string;
  isFolder: boolean;
  size: number;
  deletedAt: string;
}

export interface FileEntryDto {
  name: string;
  isFolder: boolean;
  size: number;
  modified: string;
}

export interface DirectoryListingDto {
  currentPath: string;
  roots: FileRootDto[];
  entries: FileEntryDto[];
  /** More entries exist than the server's listing limit; only the first ones are shown. */
  truncated: boolean;
  /** Entries hidden because they could not be read or point outside the root. */
  skipped: number;
  /** Largest file the server accepts for upload, in bytes. */
  maximumUploadBytes: number;
  /** Days deleted items stay restorable in the trash. */
  trashRetentionDays: number;
}

export interface SearchResultDto {
  /** Path relative to the root. */
  path: string;
  name: string;
  isFolder: boolean;
  size: number;
  modified: string;
}

export interface SearchResponseDto {
  results: SearchResultDto[];
  /** The result limit or time budget was hit; more matches may exist. */
  truncated: boolean;
}

export interface FileContentDto {
  content: string;
  modified: string;
}

export interface OperationProgress {
  items: number;
  totalItems: number;
  bytes: number;
  totalBytes: number;
}

export interface OperationTarget {
  serverId: string;
  rootId: string;
  folder: string;
}

export interface TrackedOperationDto {
  taskId: string;
  description: string;
  /** Running | Completed | Failed | Cancelled */
  status: string;
  errorMessage?: string;
  created: string;
  completed?: string;
  progress?: OperationProgress;
  target?: OperationTarget;
}

function filesPath(serverId: string, root: string, suffix?: string) {
  const base = `/api/servers/${encodeURIComponent(serverId)}/files/${encodeURIComponent(root)}`;
  return suffix ? `${base}/${suffix}` : base;
}

export const filesApi = {
  roots: (serverId: string) =>
    apiRequest<FileRootDto[]>(`/api/servers/${encodeURIComponent(serverId)}/files`),

  list: (serverId: string, root: string, path: string) =>
    apiRequest<DirectoryListingDto>(
      `${filesPath(serverId, root)}?path=${encodeURIComponent(path)}`
    ),

  search: (serverId: string, root: string, path: string, query: string, recursive: boolean) =>
    apiRequest<SearchResponseDto>(
      `${filesPath(serverId, root, "search")}?path=${encodeURIComponent(path)}&q=${encodeURIComponent(query)}&recursive=${recursive}`
    ),

  getContent: (serverId: string, root: string, path: string) =>
    apiRequest<FileContentDto>(
      `${filesPath(serverId, root, "content")}?path=${encodeURIComponent(path)}`
    ),

  /** Pass the `modified` value from getContent so the save is refused if the file changed meanwhile. */
  saveContent: (serverId: string, root: string, path: string, content: string, expectedModified?: string) =>
    apiRequest<{ modified: string }>(
      filesPath(serverId, root, "content"),
      {
        method: "PUT",
        body: JSON.stringify({ path, content, expectedModified })
      }
    ),

  upload: (serverId: string, root: string, path: string, file: File) => {
    const formData = new FormData();
    formData.append("file", file);
    return apiRequest<void>(
      `${filesPath(serverId, root, "upload")}?path=${encodeURIComponent(path)}`,
      {
        method: "POST",
        body: formData
      }
    );
  },

  uploadWithProgress: (
    serverId: string,
    root: string,
    path: string,
    file: File,
    options: { onProgress?: (progress: UploadProgress) => void; onUploaded?: () => void; signal?: AbortSignal },
    createFolders = false
  ) => {
    const formData = new FormData();
    formData.append("file", file);
    return uploadWithProgress<void>(
      `${filesPath(serverId, root, "upload")}?path=${encodeURIComponent(path)}&createFolders=${createFolders}`,
      formData,
      options
    );
  },

  createDirectory: (serverId: string, root: string, path: string) =>
    apiRequest<void>(
      filesPath(serverId, root, "mkdir"),
      {
        method: "POST",
        body: JSON.stringify({ path })
      }
    ),

  rename: (serverId: string, root: string, path: string, newName: string) =>
    apiRequest<void>(
      filesPath(serverId, root, "rename"),
      {
        method: "POST",
        body: JSON.stringify({ path, newName })
      }
    ),

  move: (serverId: string, root: string, sourcePath: string, destinationPath: string) =>
    apiRequest<void>(
      filesPath(serverId, root, "move"),
      {
        method: "POST",
        body: JSON.stringify({ sourcePath, destinationPath })
      }
    ),

  /** Moves to the trash (returns its entry) unless `permanent` is set. */
  delete: (serverId: string, root: string, path: string, permanent = false) =>
    apiRequest<TrashEntryDto | undefined>(
      `${filesPath(serverId, root)}?path=${encodeURIComponent(path)}&permanent=${permanent}`,
      {
        method: "DELETE"
      }
    ),

  /** Starts zipping several items for download; poll the task, then open downloadArchiveUrl. */
  prepareDownloadArchive: (serverId: string, root: string, paths: string[]) =>
    apiRequest<{ taskId: string; archiveId: string }>(
      filesPath(serverId, root, "download-archive"),
      { method: "POST", body: JSON.stringify({ paths }) }
    ),

  downloadArchiveUrl: (serverId: string, root: string, archiveId: string, name: string) =>
    `${filesPath(serverId, root, `download-archive/${encodeURIComponent(archiveId)}`)}?name=${encodeURIComponent(name)}`,

  size: (serverId: string, root: string, path: string) =>
    apiRequest<{ bytes: number; files: number; folders: number; complete: boolean }>(
      `${filesPath(serverId, root, "size")}?path=${encodeURIComponent(path)}`
    ),

  trash: (serverId: string, root: string) =>
    apiRequest<TrashEntryDto[]>(filesPath(serverId, root, "trash")),

  restoreTrash: (serverId: string, root: string, trashId: string) =>
    apiRequest<TrashEntryDto>(
      filesPath(serverId, root, `trash/${encodeURIComponent(trashId)}/restore`),
      { method: "POST" }
    ),

  purgeTrash: (serverId: string, root: string, trashId: string) =>
    apiRequest<void>(filesPath(serverId, root, `trash/${encodeURIComponent(trashId)}`), { method: "DELETE" }),

  emptyTrash: (serverId: string, root: string) =>
    apiRequest<void>(filesPath(serverId, root, "trash"), { method: "DELETE" }),

  compress: (serverId: string, root: string, sourcePaths: string[], destinationZipPath: string) =>
    apiRequest<{ taskId: string }>(
      filesPath(serverId, root, "compress"),
      {
        method: "POST",
        body: JSON.stringify({ sourcePaths, destinationZipPath })
      }
    ),

  extract: (serverId: string, root: string, zipPath: string, destinationDirectoryPath: string) =>
    apiRequest<{ taskId: string }>(
      filesPath(serverId, root, "extract"),
      {
        method: "POST",
        body: JSON.stringify({ zipPath, destinationDirectoryPath })
      }
    ),

  recentOperations: () => apiRequest<TrackedOperationDto[]>("/api/servers/operations"),

  cancelOperation: (taskId: string) =>
    apiRequest<void>(`/api/servers/operations/${encodeURIComponent(taskId)}`, { method: "DELETE" }),

  getOperationStatus: (taskId: string) =>
    apiRequest<TrackedOperationDto>(`/api/servers/operations/${encodeURIComponent(taskId)}`),

  /** Inline image URL for previews (raster formats only; see isPreviewableImage). */
  getPreviewUrl: (serverId: string, root: string, path: string, version?: number) =>
    `${filesPath(serverId, root, "download")}?path=${encodeURIComponent(path)}&inline=true${version ? `&v=${version}` : ""}`,

  getDownloadUrl: (serverId: string, root: string, path: string) =>
    `${filesPath(serverId, root, "download")}?path=${encodeURIComponent(path)}`
};
