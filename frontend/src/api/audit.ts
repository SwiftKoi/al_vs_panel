import { apiRequest } from "@/api/client";

export interface AuditEntry {
  id: number;
  timestampUtc: string;
  actor: string;
  actorRole: string;
  ipAddress?: string | null;
  category: string;
  action: string;
  serverId?: string | null;
  target?: string | null;
  detailsJson?: string | null;
  succeeded: boolean;
  error?: string | null;
}

export interface AuditPageResult {
  items: AuditEntry[];
  total: number;
  offset: number;
  limit: number;
}

export interface AuditFacets {
  actors: string[];
  actions: { category: string; action: string }[];
  servers: string[];
}

export interface AuditQueryParams {
  actor?: string;
  category?: string;
  action?: string;
  server?: string;
  succeeded?: boolean;
  from?: string;
  to?: string;
  search?: string;
  limit?: number;
  offset?: number;
}

function buildQuery(params: AuditQueryParams): string {
  const query = new URLSearchParams();
  if (params.actor) query.set("actor", params.actor);
  if (params.category) query.set("category", params.category);
  if (params.action) query.set("action", params.action);
  if (params.server) query.set("server", params.server);
  if (params.succeeded !== undefined) query.set("succeeded", String(params.succeeded));
  if (params.from) query.set("from", new Date(params.from).toISOString());
  if (params.to) query.set("to", new Date(params.to).toISOString());
  if (params.search) query.set("search", params.search);
  if (params.limit !== undefined) query.set("limit", String(params.limit));
  if (params.offset !== undefined) query.set("offset", String(params.offset));
  const serialized = query.toString();
  return serialized ? `?${serialized}` : "";
}

export const auditApi = {
  query: (params: AuditQueryParams) => apiRequest<AuditPageResult>(`/api/audit/${buildQuery(params)}`),
  facets: () => apiRequest<AuditFacets>("/api/audit/facets")
};
