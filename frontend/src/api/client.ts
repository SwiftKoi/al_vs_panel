type ProblemDetails = { title?: string; detail?: string };

type CsrfResponse = { token: string };

let csrfToken: string | undefined;
let csrfRequest: Promise<string> | undefined;

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken;
  csrfRequest ??= fetch("/auth/csrf", { credentials: "include" })
    .then(async (response) => {
      if (!response.ok) throw new ApiError(response.status, "Unable to initialize request security.");
      const result = (await response.json()) as CsrfResponse;
      csrfToken = result.token;
      return result.token;
    })
    .finally(() => { csrfRequest = undefined; });
  return csrfRequest;
}

function isStateChanging(method?: string) {
  return method !== undefined && !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
}

export class ApiError extends Error {
  constructor(public readonly status: number, message?: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (!(init?.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }
  if (isStateChanging(init?.method)) {
    headers.set("X-CSRF-TOKEN", await getCsrfToken());
  }

  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers
  });

  if (response.status === 401 && path !== "/auth/login") {
    window.dispatchEvent(new Event("auth:unauthorized"));
  }

  if (!response.ok) {
    let message: string | undefined;
    try {
      const problem = (await response.json()) as ProblemDetails;
      message = problem.detail || problem.title || message;
    } catch {
      // Keep the status-based message for non-JSON responses.
    }
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  if (!text) return undefined as T;
  return JSON.parse(text) as T;
}

export type LoginResponse = {
  authenticated: boolean;
  requiresTwoFactor?: boolean;
  user?: AuthenticatedUser;
};
export type AuthenticatedUser = { id: string; username: string };
export type AuthenticationSessionResponse = { authenticated: boolean; user: AuthenticatedUser };
export type HealthResponse = { status: string };

export type UserResponse = {
  id: string;
  username: string;
  twoFactorEnabled: boolean;
};

export type TwoFactorSetupResponse = {
  sharedSecret: string;
  provisioningUri: string;
};

export const api = {
  login: (username: string, password: string) =>
    apiRequest<LoginResponse>("/auth/login", { method: "POST", body: JSON.stringify({ username, password }) }),
  session: () => apiRequest<AuthenticationSessionResponse>("/auth/session"),
  refresh: () => apiRequest<AuthenticationSessionResponse>("/auth/refresh", { method: "POST" }),
  logout: () => apiRequest<void>("/auth/logout", { method: "POST" }),
  health: () => apiRequest<HealthResponse>("/health"),

  // 2FA Endpoints
  login2fa: (code: string) =>
    apiRequest<LoginResponse>("/auth/login/2fa", { method: "POST", body: JSON.stringify({ code }) }),
  setup2fa: () =>
    apiRequest<TwoFactorSetupResponse>("/auth/2fa/setup", { method: "POST" }),
  enable2fa: (code: string) =>
    apiRequest<{ success: boolean }>("/auth/2fa/enable", { method: "POST", body: JSON.stringify({ code }) }),
  disable2fa: () =>
    apiRequest<{ success: boolean }>("/auth/2fa/disable", { method: "POST" }),

  // User CRUD Endpoints
  listUsers: () =>
    apiRequest<UserResponse[]>("/api/users"),
  createUser: (username: string, password: string) =>
    apiRequest<UserResponse>("/api/users", { method: "POST", body: JSON.stringify({ username, password }) }),
  deleteUser: (id: string) =>
    apiRequest<void>(`/api/users/${id}`, { method: "DELETE" })
};

