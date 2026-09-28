import { apiRequest } from "@/api/client";

export interface FileRootDto {
  id: string;
  displayName: string;
  isWritable: boolean;
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
}

export interface FileContentDto {
  content: string;
  modified: string;
}

export interface TrackedOperationDto {
  taskId: string;
  description: string;
  status: string;
  errorMessage?: string;
  created: string;
  completed?: string;
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

  delete: (serverId: string, root: string, path: string) =>
    apiRequest<void>(
      `${filesPath(serverId, root)}?path=${encodeURIComponent(path)}`,
      {
        method: "DELETE"
      }
    ),

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

  getOperationStatus: (taskId: string) =>
    apiRequest<TrackedOperationDto>(`/api/servers/operations/${encodeURIComponent(taskId)}`),

  getDownloadUrl: (serverId: string, root: string, path: string) =>
    `${filesPath(serverId, root, "download")}?path=${encodeURIComponent(path)}`
};
