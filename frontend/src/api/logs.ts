import { apiRequest } from "@/api/client";

export type LogLevel =
  | "Trace"
  | "Debug"
  | "Information"
  | "Warning"
  | "Error"
  | "Critical";

export interface LogEvent {
  id: number;
  timestampUtc: string;
  level: string;
  category: string;
  eventId: number;
  message: string;
  stateJson?: string | null;
  exceptionType?: string | null;
  exceptionMessage?: string | null;
  stackTrace?: string | null;
}

export interface LogPageResult {
  items: LogEvent[];
  total: number;
  offset: number;
  limit: number;
}

export interface LogLevelCounts {
  debug: number;
  information: number;
  warning: number;
  error: number;
  critical: number;
}

export interface LogQueryParams {
  level?: string;
  source?: string;
  from?: string;
  to?: string;
  search?: string;
  limit?: number;
  offset?: number;
}

function buildQuery(params: LogQueryParams): string {
  const query = new URLSearchParams();
  if (params.level) query.set("level", params.level);
  if (params.source) query.set("source", params.source);
  if (params.from) query.set("from", params.from);
  if (params.to) query.set("to", params.to);
  if (params.search) query.set("search", params.search);
  if (params.limit !== undefined) query.set("limit", String(params.limit));
  if (params.offset !== undefined) query.set("offset", String(params.offset));
  const serialized = query.toString();
  return serialized ? `?${serialized}` : "";
}

export const logsApi = {
  query: (params: LogQueryParams) => apiRequest<LogPageResult>(`/api/logs/${buildQuery(params)}`),
  errors: (limit = 100, offset = 0) =>
    apiRequest<LogPageResult>(`/api/logs/errors?limit=${limit}&offset=${offset}`),
  sources: () => apiRequest<string[]>("/api/logs/sources"),
  summary: (from?: string, to?: string) => {
    const query = new URLSearchParams();
    if (from) query.set("from", from);
    if (to) query.set("to", to);
    const serialized = query.toString();
    return apiRequest<LogLevelCounts>(`/api/logs/summary${serialized ? `?${serialized}` : ""}`);
  },
  clear: (olderThanDays?: number) =>
    apiRequest<{ deleted: number }>(
      `/api/logs${olderThanDays ? `?olderThanDays=${olderThanDays}` : ""}`,
      { method: "DELETE" }
    )
};
