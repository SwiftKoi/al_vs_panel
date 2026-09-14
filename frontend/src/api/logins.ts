import { apiRequest } from "@/api/client";

export interface LoginEvent {
  id: number;
  username: string;
  timestampUtc: string;
  ipAddress: string;
  succeeded: boolean;
}

export interface LoginLogPage {
  items: LoginEvent[];
  total: number;
  offset: number;
  limit: number;
}

export const loginsApi = {
  recent: (page = 1, pageSize = 10) =>
    apiRequest<LoginLogPage>(`/auth/login-logs?page=${page}&pageSize=${pageSize}`)
};
