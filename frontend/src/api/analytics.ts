import { apiRequest } from "@/api/client";

export interface PlayerWindowStats {
  window: "day" | "week" | "month";
  uniquePlayers: number;
  proxyPlayers: number;
  newPlayers: number;
  joins: number;
}

export interface PlayerDailyStats {
  date: string;
  uniquePlayers: number;
  proxyPlayers: number;
  newPlayers: number;
  joins: number;
  peakConcurrent: number | null;
}

export interface PlayerSummaryResponse {
  serverId: string;
  timeZone: string;
  proxyConfigured: boolean;
  recordedSinceUtc: string | null;
  windows: PlayerWindowStats[];
  daily: PlayerDailyStats[];
}

export const analyticsApi = {
  players: (serverId: string, days: number) =>
    apiRequest<PlayerSummaryResponse>(
      `/api/analytics/${encodeURIComponent(serverId)}/players?days=${days}`
    )
};

export type SessionEndKind =
  | "left"
  | "lostConnection"
  | "clientCrash"
  | "serverShutdown"
  | "serverError"
  | "kicked"
  | "unknown"
  | "open";

export interface DisconnectSummary {
  sessions: number;
  drops: number;
  quickRejoins: number;
  dropsNearAutosave: number;
  groupDrops: number;
  dropsAfterOverload: number;
  medianSessionMinutes: number;
  proxySessions: number;
  proxyDrops: number;
  directSessions: number;
  directDrops: number;
  connectionFailures: number;
}

export interface DropEvent {
  occurredAtUtc: string;
  playerName: string;
  kind: SessionEndKind;
  reason: string | null;
  sessionMinutes: number;
  viaProxy: boolean;
  nearAutosave: boolean;
  slowestTickMs: number | null;
  simultaneousDrops: number;
  rttMs: number | null;
  retransmitPercent: number | null;
  lastReceiveMs: number | null;
}

export interface PlayerDisconnectStats {
  playerName: string;
  sessions: number;
  drops: number;
  quickRejoins: number;
  averageSessionMinutes: number;
  usesProxy: boolean;
  lastDropUtc: string | null;
}

export interface DisconnectReportResponse {
  serverId: string;
  timeZone: string;
  days: number;
  summary: DisconnectSummary;
  endReasons: { kind: SessionEndKind; count: number }[];
  failureReasons: { reason: string; count: number }[];
  daily: { date: string; sessions: number; drops: number; quickRejoins: number }[];
  players: PlayerDisconnectStats[];
  recentDrops: DropEvent[];
}

export const disconnectsApi = {
  report: (serverId: string, days: number) =>
    apiRequest<DisconnectReportResponse>(
      `/api/analytics/${encodeURIComponent(serverId)}/disconnects?days=${days}`
    )
};

export interface ConnectionQualityStats {
  samples: number;
  medianRttMs: number | null;
  p95RttMs: number | null;
  averageJitterMs: number | null;
  lossPercent: number | null;
  stalls: number;
}

export interface PlayerConnectionQuality {
  playerName: string;
  usesProxy: boolean;
  lastSeenUtc: string;
  stats: ConnectionQualityStats;
}

export interface ConnectionQualityResponse {
  serverId: string;
  hours: number;
  proxyConfigured: boolean;
  all: ConnectionQualityStats;
  proxy: ConnectionQualityStats;
  direct: ConnectionQualityStats;
  players: PlayerConnectionQuality[];
}

export interface PlayerConnectionPoint {
  sampledAtUtc: string;
  rttMs: number;
  jitterMs: number;
  lossPercent: number | null;
  lastReceiveMs: number;
  sendQueueBytes: number;
}

export interface PlayerConnectionHistoryResponse {
  serverId: string;
  playerName: string;
  hours: number;
  usesProxy: boolean;
  stats: ConnectionQualityStats;
  points: PlayerConnectionPoint[];
  sessions: { startedAtUtc: string; endedAtUtc: string | null; endKind: SessionEndKind }[];
}

export const connectionQualityApi = {
  overview: (serverId: string, hours: number) =>
    apiRequest<ConnectionQualityResponse>(
      `/api/analytics/${encodeURIComponent(serverId)}/connection-quality?hours=${hours}`
    ),
  player: (serverId: string, playerName: string, hours: number) =>
    apiRequest<PlayerConnectionHistoryResponse>(
      `/api/analytics/${encodeURIComponent(serverId)}/connection-quality/${encodeURIComponent(playerName)}?hours=${hours}`
    )
};

export interface ServerHealthPoint {
  sampledAtUtc: string;
  cpuPercent: number | null;
  memoryPercent: number | null;
  memoryBytes: number | null;
  players: number;
  bytesOutPerSecond: number;
  bytesInPerSecond: number;
  longestPauseSeconds: number | null;
  slowestTickMs: number | null;
}

export interface ServerHealthResponse {
  serverId: string;
  hours: number;
  summary: {
    averageCpuPercent: number | null;
    peakCpuPercent: number | null;
    peakMemoryPercent: number | null;
    peakPlayers: number;
    pauses: number;
    longestPauseSeconds: number | null;
    pausesOverOneSecond: number;
    peakBytesOutPerSecond: number;
    overloads: number;
    overloadsOverTwoSeconds: number;
    slowestTickMs: number | null;
    medianOverloadTickMs: number | null;
  };
  points: ServerHealthPoint[];
  longestPauses: { startedAtUtc: string; seconds: number }[];
  slowestTicks: { occurredAtUtc: string; tickMs: number }[];
}

export const serverHealthApi = {
  get: (serverId: string, hours: number) =>
    apiRequest<ServerHealthResponse>(`/api/analytics/${encodeURIComponent(serverId)}/health?hours=${hours}`)
};

export interface ActivityHeatmapResponse {
  serverId: string;
  timeZone: string;
  days: number;
  cells: { weekday: number; hour: number; averagePlayers: number; peakPlayers: number }[];
}

export interface PlayerPlaytime {
  playerName: string;
  sessions: number;
  totalMinutes: number;
  averageSessionMinutes: number;
  drops: number;
  quickRejoins: number;
  usesProxy: boolean;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

export interface PlayerSessionRow {
  startedAtUtc: string;
  endedAtUtc: string | null;
  minutes: number;
  endKind: SessionEndKind;
  endReason: string | null;
  viaProxy: boolean;
  quickRejoin: boolean;
}

export interface PlayerProfileResponse {
  serverId: string;
  playerName: string;
  days: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  proxySessionPercent: number;
  sessions: number;
  totalMinutes: number;
  averageSessionMinutes: number;
  drops: number;
  quickRejoins: number;
  endReasons: { kind: SessionEndKind; count: number }[];
  quality: ConnectionQualityStats;
  recentSessions: PlayerSessionRow[];
}

export const playersApi = {
  heatmap: (serverId: string, days: number) =>
    apiRequest<ActivityHeatmapResponse>(`/api/analytics/${encodeURIComponent(serverId)}/players/heatmap?days=${days}`),
  list: (serverId: string, days: number) =>
    apiRequest<{ serverId: string; days: number; players: PlayerPlaytime[] }>(
      `/api/analytics/${encodeURIComponent(serverId)}/players/list?days=${days}`
    ),
  profile: (serverId: string, playerName: string, days: number) =>
    apiRequest<PlayerProfileResponse>(
      `/api/analytics/${encodeURIComponent(serverId)}/players/list/${encodeURIComponent(playerName)}?days=${days}`
    )
};
