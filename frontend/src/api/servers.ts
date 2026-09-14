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
  metrics: (serverId: string) => apiRequest<ServerMetricsResponse>(serverPath(serverId, "metrics")),
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
