import { apiRequest } from "@/api/client";

export type ServerRuntimeStatus = "online" | "offline" | "unknown";
export type ServerLifecycleAction = "start" | "stop" | "restart";

export interface ServerSummary {
  id: string;
  name: string;
  host: string;
  port: number;
  location: string;
}

export interface ServerStatusResponse {
  serverId: string;
  status: ServerRuntimeStatus;
}

export interface ServerLifecycleResponse extends ServerStatusResponse {
  action: ServerLifecycleAction;
}

export interface ServerMetricsResponse {
  serverId: string;
  cpuPercent: number;
  memoryUsage: string;
  memoryLimit: string;
  memoryPercent: number;
  blockRead: string;
  blockWrite: string;
  diskUsedBytes: number;
  diskTotalBytes: number;
  diskAvailableBytes: number;
  diskPercent: number;
  /** When the game server container started (UTC); null when unknown. */
  startedAtUtc?: string | null;
}

export interface ServerClientConnection {
  remoteAddress: string;
  remotePort: number;
  localPort: number;
  playerName: string | null;
  joinCount: number;
  rttMs: number;
  rttVarianceMs: number;
  minRttMs: number;
  retransmitPercent: number;
  retransmitsTotal: number;
  unackedSegments: number;
  receiveQueueBytes: number;
  sendQueueBytes: number;
  bytesSent: number;
  bytesReceived: number;
  lastReceiveMs: number;
  lastSendMs: number;
}

export interface ServerConnectionsResponse {
  serverId: string;
  connections: ServerClientConnection[];
}

export type ServerLogEvent =
  | { kind: "line"; data: string }
  | { kind: "error"; data: string }
  | { kind: "end"; data?: string };

interface OpenServerLogsOptions {
  onEvent: (event: ServerLogEvent) => void;
  onConnected: () => void;
  onConnectionError: () => void;
}

function serverPath(serverId: string, suffix: string) {
  return `/api/servers/${encodeURIComponent(serverId)}/${suffix}`;
}

function parseEventData(event: MessageEvent<string>): string {
  try {
    const value = JSON.parse(event.data) as unknown;
    return typeof value === "string" ? value : "";
  } catch {
    return "";
  }
}

/** Pretty: coordinates box (x y z); absolute: debug screen (=x); relative: offset from the player (~x). */
export type TeleportCoordinates = "pretty" | "absolute" | "relative";
/** allowance: extra land claim allowance; maxAreas: extra land claim areas. Both on top of the role's. */
export type LandClaimSetting = "allowance" | "maxAreas";

// Moderator actions: the server builds each console command from these fields.
function runAction(serverId: string, action: string, body: object) {
  return apiRequest<{ serverId: string; accepted: boolean }>(serverPath(serverId, `actions/${action}`), {
    method: "POST",
    body: JSON.stringify(body)
  });
}

export const serverApi = {
  list: () => apiRequest<ServerSummary[]>("/api/servers/"),
  status: (serverId: string) => apiRequest<ServerStatusResponse>(serverPath(serverId, "status")),
  start: (serverId: string) =>
    apiRequest<ServerLifecycleResponse>(serverPath(serverId, "start"), { method: "POST" }),
  stop: (serverId: string) =>
    apiRequest<ServerLifecycleResponse>(serverPath(serverId, "stop"), { method: "POST" }),
  restart: (serverId: string) =>
    apiRequest<ServerLifecycleResponse>(serverPath(serverId, "restart"), { method: "POST" }),
  sendCommand: (serverId: string, command: string) =>
    apiRequest<{ serverId: string; accepted: boolean }>(serverPath(serverId, "commands"), {
      method: "POST",
      body: JSON.stringify({ command })
    }),
  setGameMode: (serverId: string, playerName: string, mode: number) =>
    runAction(serverId, "gamemode", { playerName, mode }),
  teleport: (serverId: string, playerName: string, coordinates: TeleportCoordinates, x: number, y: number, z: number) =>
    runAction(serverId, "teleport", { playerName, coordinates, x, y, z }),
  warn: (serverId: string, playerName: string, reason: string) => runAction(serverId, "warn", { playerName, reason }),
  kick: (serverId: string, playerName: string, reason: string) => runAction(serverId, "kick", { playerName, reason }),
  ban: (serverId: string, playerName: string, reason: string) => runAction(serverId, "ban", { playerName, reason }),
  unban: (serverId: string, playerName: string) => runAction(serverId, "unban", { playerName }),
  hardban: (serverId: string, playerName: string) => runAction(serverId, "hardban", { playerName }),
  setLandClaim: (serverId: string, playerName: string, setting: LandClaimSetting, value: number) =>
    runAction(serverId, "landclaim", { playerName, setting, value }),
  allowClassReselect: (serverId: string, playerName: string) => runAction(serverId, "allowcharselonce", { playerName }),
  metrics: (serverId: string) => apiRequest<ServerMetricsResponse>(serverPath(serverId, "metrics")),
  connections: (serverId: string) =>
    apiRequest<ServerConnectionsResponse>(serverPath(serverId, "connections")),
  openLogs(serverId: string, options: OpenServerLogsOptions): () => void {
    const source = new EventSource(serverPath(serverId, "logs"), { withCredentials: true });
    source.onopen = () => options.onConnected();

    source.addEventListener("line", (event) => {
      options.onEvent({ kind: "line", data: parseEventData(event as MessageEvent<string>) });
    });
    source.addEventListener("error", (event) => {
      if (event instanceof MessageEvent) {
        options.onEvent({ kind: "error", data: parseEventData(event as MessageEvent<string>) });
      }
    });
    source.addEventListener("end", () => options.onEvent({ kind: "end" }));
    source.onerror = (event) => {
      if (!(event instanceof MessageEvent)) options.onConnectionError();
    };

    return () => source.close();
  }
};
