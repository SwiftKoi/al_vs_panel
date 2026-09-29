import { apiRequest } from "@/api/client";

export type LogKind = "main" | "audit" | "debug" | "chat";

export interface LogEntry {
  id: number;
  timestamp: string;
  log: LogKind;
  level: string;
  source: string | null;
  player: string | null;
  action: string | null;
  item: string | null;
  x: number | null;
  y: number | null;
  z: number | null;
  message: string;
  extra: string | null;
  signatureId: number | null;
  /** The other party: victim of a kill, attacker on damage, killer on a death, receiver of a gift. */
  other: string | null;
}

export interface LogSearchResponse { entries: LogEntry[]; nextCursor: string | null; terms: string[] }
export interface FacetValue { value: string; count: number }
export interface LogFacets {
  total: number;
  logs: FacetValue[];
  levels: FacetValue[];
  sources: FacetValue[];
  players: FacetValue[];
  actions: FacetValue[];
}
export interface LogHistogram { from: string; to: string; bucketMilliseconds: number; counts: number[] }
export interface LogContext { focusId: number; entries: LogEntry[] }
export interface LogSignature {
  id: number;
  log: LogKind;
  level: string;
  source: string | null;
  template: string;
  count: number;
  firstSeen: string;
  lastSeen: string;
  isNew: boolean;
  muted: boolean;
  trend: number[];
  sampleMessage: string;
}
export interface LogSignatures { lastServerStart: string | null; signatures: LogSignature[] }
export interface LogIndexStatus {
  configured: boolean;
  entries: number;
  indexedBytes: number;
  totalBytes: number;
  files: number;
  databaseBytes: number;
  oldestEntry: string | null;
  newestEntry: string | null;
  lastIndexedAt: string | null;
  lastError: string | null;
}

export interface PlayerSummary {
  name: string;
  actions: number;
  commands: number;
  kills: number;
  deaths: number;
  rejectedPositions: number;
  joins: number;
  lastSeen: string;
}
export interface ItemTotal { item: string; quantity: number; events: number }
export interface Place { x: number; y: number | null; z: number; count: number; lastSeen: string }
export interface DayActivity { day: string; actions: number; rejectedPositions: number }
export interface PlayerActivity {
  name: string;
  firstSeen: string | null;
  lastSeen: string | null;
  actions: FacetValue[];
  taken: ItemTotal[];
  put: ItemTotal[];
  kills: FacetValue[];
  commands: LogEntry[];
  deaths: LogEntry[];
  sessions: LogEntry[];
  places: Place[];
  days: DayActivity[];
}
export interface LocationPlayer { name: string; count: number; actions: FacetValue[]; firstSeen: string; lastSeen: string }
export interface LocationReport {
  x: number;
  y: number | null;
  z: number;
  radius: number;
  total: number;
  players: LocationPlayer[];
  taken: ItemTotal[];
  put: ItemTotal[];
}
export interface ModChange { modId: string; change: "added" | "removed" | "updated"; from: string | null; to: string | null }
export interface Boot {
  startedAt: string;
  readyAt: string | null;
  startupSeconds: number | null;
  stoppedAt: string | null;
  state: "running" | "stopped" | "unclean";
  gameVersion: string | null;
  modCount: number | null;
  startupWarnings: number;
  startupErrors: number;
  modChanges: ModChange[];
}
export interface SavedSearch { id: number; name: string; query: string; range: string; createdAt: string }
export interface ProblemSummary { configured: boolean; lastServerStart: string | null; newErrors: number; newWarnings: number }

export interface LogSearchParams { q: string; from: string; to?: string; noise: boolean }

const base = (serverId: string) => `/api/servers/${encodeURIComponent(serverId)}/server-logs`;

function queryString(params: Record<string, string | number | boolean | undefined | null>) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") search.set(key, String(value));
  }
  return search.toString();
}

const searchQuery = (p: LogSearchParams) => ({ q: p.q, from: p.from, to: p.to, noise: p.noise || undefined });

export const serverLogsApi = {
  status: (serverId: string) => apiRequest<LogIndexStatus>(`${base(serverId)}/status`),
  search: (serverId: string, params: LogSearchParams, cursor?: string, limit = 200) =>
    apiRequest<LogSearchResponse>(`${base(serverId)}/search?${queryString({ ...searchQuery(params), cursor, limit })}`),
  facets: (serverId: string, params: LogSearchParams) =>
    apiRequest<LogFacets>(`${base(serverId)}/facets?${queryString(searchQuery(params))}`),
  histogram: (serverId: string, params: LogSearchParams, buckets = 60) =>
    apiRequest<LogHistogram>(`${base(serverId)}/histogram?${queryString({ ...searchQuery(params), buckets })}`),
  context: (serverId: string, entryId: number, lines: number, noise: boolean, logs?: string) =>
    apiRequest<LogContext>(`${base(serverId)}/entries/${entryId}/context?${queryString({ before: lines, after: lines, noise: noise || undefined, logs })}`),
  signatures: (serverId: string, from: string, to?: string) =>
    apiRequest<LogSignatures>(`${base(serverId)}/signatures?${queryString({ from, to })}`),
  setMuted: (serverId: string, signatureId: number, muted: boolean) =>
    apiRequest<void>(`${base(serverId)}/signatures/${signatureId}/mute`, { method: "PUT", body: JSON.stringify({ muted }) }),
  /** Values for an autocomplete key; `context` (the rest of the search box) narrows them. */
  suggest: (serverId: string, key: string, prefix: string, context: string, from: string, to?: string) =>
    apiRequest<{ key: string; values: FacetValue[] }>(`${base(serverId)}/suggest?${queryString({ key, prefix, q: context, from, to })}`),
  problemSummary: (serverId: string) => apiRequest<ProblemSummary>(`${base(serverId)}/problems/summary`),
  players: (serverId: string, from: string, to?: string) =>
    apiRequest<{ players: PlayerSummary[] }>(`${base(serverId)}/players?${queryString({ from, to })}`),
  playerActivity: (serverId: string, player: string, from: string, to?: string) =>
    apiRequest<PlayerActivity>(`${base(serverId)}/players/${encodeURIComponent(player)}/activity?${queryString({ from, to })}`),
  location: (serverId: string, place: { x: number; y?: number | null; z: number; radius: number }, from: string, to?: string) =>
    apiRequest<LocationReport>(`${base(serverId)}/location?${queryString({ x: place.x, y: place.y ?? undefined, z: place.z, radius: place.radius, from, to })}`),
  boots: (serverId: string, from: string, to?: string) =>
    apiRequest<{ boots: Boot[] }>(`${base(serverId)}/boots?${queryString({ from, to })}`),
  savedSearches: (serverId: string) => apiRequest<SavedSearch[]>(`${base(serverId)}/saved-searches`),
  saveSearch: (serverId: string, search: { name: string; query: string; range: string }) =>
    apiRequest<SavedSearch>(`${base(serverId)}/saved-searches`, { method: "POST", body: JSON.stringify(search) }),
  deleteSavedSearch: (serverId: string, id: number) =>
    apiRequest<void>(`${base(serverId)}/saved-searches/${id}`, { method: "DELETE" }),
  exportUrl: (serverId: string, params: LogSearchParams, format: "txt" | "csv") =>
    `${base(serverId)}/export?${queryString({ ...searchQuery(params), format })}`
};
