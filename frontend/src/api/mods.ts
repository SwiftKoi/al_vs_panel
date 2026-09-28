import { apiRequest } from "@/api/client";

export type ModStatus =
  | "UpdateAvailable"
  | "UpToDate"
  | "Ahead"
  | "NoCompatibleRelease"
  | "NotOnModDb"
  | "CheckFailed"
  | "Unidentified";

export type ModUpdateJobState = "Running" | "Succeeded" | "Failed";
export type ModUpdateItemState = "Queued" | "Downloading" | "Verifying" | "Staged" | "Installed" | "Failed";

export interface InstalledMod {
  fileName: string;
  modId: string | null;
  name: string;
  version: string | null;
  side: string | null;
  description: string | null;
  authors: string[];
  sizeBytes: number;
  modifiedUtc: string;
  status: ModStatus;
  statusDetail: string | null;
  isPrerelease: boolean;
  updateVersion: string | null;
  prereleaseVersion: string | null;
  latestVersion: string | null;
  newerReleaseCount: number;
  isPinned: boolean;
  isLoaded: boolean | null;
  modDbUrl: string | null;
  logoUrl: string | null;
}

export interface ModRelease {
  version: string;
  createdUtc: string | null;
  downloads: number;
  fileName: string | null;
  gameVersions: string[];
  isPrerelease: boolean;
  isCompatible: boolean;
  changelogHtml: string | null;
}

export interface ModBackup {
  batchId: string;
  appliedUtc: string;
  items: { oldFileName: string | null; newFileName: string }[];
}

export interface ModUpdateJob {
  jobId: string;
  serverId: string;
  state: ModUpdateJobState;
  startedUtc: string;
  finishedUtc: string | null;
  error: string | null;
  items: {
    modId: string;
    name: string;
    fromVersion: string | null;
    toVersion: string;
    state: ModUpdateItemState;
    error: string | null;
  }[];
}

export interface ModOverview {
  serverId: string;
  gameVersion: string | null;
  gameVersionFromLog: boolean;
  checkedUtc: string;
  restartRequired: boolean;
  serverStartedUtc: string | null;
  lastUpdate: ModBackup | null;
  currentJob: ModUpdateJob | null;
  mods: InstalledMod[];
}

export interface ModDetail {
  mod: InstalledMod;
  author: string | null;
  homepageUrl: string | null;
  sourceUrl: string | null;
  issueTrackerUrl: string | null;
  totalDownloads: number | null;
  releases: ModRelease[];
}

export interface ModUpdateItem {
  modId: string;
  version: string;
}

const base = (serverId: string) => `/api/servers/${encodeURIComponent(serverId)}/mods`;

export const modsApi = {
  overview: (serverId: string, refresh = false) =>
    apiRequest<ModOverview>(`${base(serverId)}${refresh ? "?refresh=true" : ""}`),
  detail: (serverId: string, modId: string) =>
    apiRequest<ModDetail>(`${base(serverId)}/${encodeURIComponent(modId)}`),
  setPinned: (serverId: string, modId: string, pinned: boolean) =>
    apiRequest<void>(`${base(serverId)}/${encodeURIComponent(modId)}/pin`, { method: "PUT", body: JSON.stringify({ pinned }) }),
  update: (serverId: string, items: ModUpdateItem[]) =>
    apiRequest<ModUpdateJob>(`${base(serverId)}/update`, { method: "POST", body: JSON.stringify({ items }) }),
  currentJob: (serverId: string) => apiRequest<ModUpdateJob | undefined>(`${base(serverId)}/update`),
  rollback: (serverId: string) => apiRequest<ModBackup>(`${base(serverId)}/rollback`, { method: "POST" })
};
