type ProblemDetails = { title?: string; detail?: string };

type CsrfResponse = { token: string };

let csrfToken: string | undefined;
let csrfRequest: Promise<string> | undefined;

/**
 * Drop the cached anti-forgery token so the next state-changing request fetches
 * a fresh one. The token is bound to the current user's claims, so any change of
 * identity — login, logout, or a session refresh — invalidates every token that
 * was issued before it.
 */
export function invalidateCsrfToken(): void {
  csrfToken = undefined;
}

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
  return sendRequest<T>(path, init, false);
}

async function sendRequest<T>(path: string, init: RequestInit | undefined, isRetry: boolean): Promise<T> {
  const stateChanging = isStateChanging(init?.method);
  // A cached token can belong to a previous session. If the server rejects it we
  // refresh once and retry, rather than leaving the panel broken until the user
  // reloads the page.
  const usedCachedToken = stateChanging && csrfToken !== undefined;

  const headers = new Headers(init?.headers);
  if (!(init?.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }
  if (stateChanging) {
    headers.set("X-CSRF-TOKEN", await getCsrfToken());
  }

  const response = await fetch(path, {
    ...init,
    credentials: "include",
    headers
  });

  if (!response.ok) {
    if (stateChanging && usedCachedToken && !isRetry && response.status === 400) {
      invalidateCsrfToken();
      return sendRequest<T>(path, init, true);
    }

    if (response.status === 401 && path !== "/auth/login") {
      invalidateCsrfToken();
      window.dispatchEvent(new Event("auth:unauthorized"));
    }

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
  login: async (username: string, password: string) => {
    // The identity changes on success, so a token fetched for the anonymous
    // session must not survive it.
    invalidateCsrfToken();
    return apiRequest<LoginResponse>("/auth/login", { method: "POST", body: JSON.stringify({ username, password }) });
  },
  session: () => apiRequest<AuthenticationSessionResponse>("/auth/session"),
  refresh: async () => {
    const session = await apiRequest<AuthenticationSessionResponse>("/auth/refresh", { method: "POST" });
    // A refreshed session carries new claims, which invalidates the old token.
    invalidateCsrfToken();
    return session;
  },
  logout: async () => {
    try {
      await apiRequest<void>("/auth/logout", { method: "POST" });
    } finally {
      invalidateCsrfToken();
    }
  },
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

export type UploadProgress = { loaded: number; total: number };

/**
 * Sends multipart form data with XMLHttpRequest, because fetch cannot report upload
 * progress. Uses the same CSRF token handling (and single stale-token retry) as
 * apiRequest. Aborting the signal rejects with an AbortError.
 */
export async function uploadWithProgress<T>(
  path: string,
  body: FormData,
  options: { onProgress?: (progress: UploadProgress) => void; onUploaded?: () => void; signal?: AbortSignal } = {},
  isRetry = false
): Promise<T> {
  const usedCachedToken = csrfToken !== undefined;
  const token = await getCsrfToken();

  const result = await new Promise<{ status: number; text: string }>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", path);
    xhr.withCredentials = true;
    xhr.setRequestHeader("X-CSRF-TOKEN", token);
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) options.onProgress?.({ loaded: event.loaded, total: event.total });
    };
    // All bytes are sent; the server is still writing them to disk.
    xhr.upload.onload = () => options.onUploaded?.();
    xhr.onload = () => resolve({ status: xhr.status, text: xhr.responseText });
    xhr.onerror = () => reject(new ApiError(0, "Network error while uploading."));
    xhr.onabort = () => reject(new DOMException("Upload cancelled.", "AbortError"));
    if (options.signal) {
      if (options.signal.aborted) {
        reject(new DOMException("Upload cancelled.", "AbortError"));
        return;
      }
      options.signal.addEventListener("abort", () => xhr.abort(), { once: true });
    }
    xhr.send(body);
  });

  if (result.status >= 200 && result.status < 300) {
    return (result.text ? JSON.parse(result.text) : undefined) as T;
  }

  if (usedCachedToken && !isRetry && result.status === 400) {
    invalidateCsrfToken();
    return uploadWithProgress<T>(path, body, options, true);
  }
  if (result.status === 401) {
    invalidateCsrfToken();
    window.dispatchEvent(new Event("auth:unauthorized"));
  }

  let message: string | undefined;
  try {
    const problem = JSON.parse(result.text) as ProblemDetails;
    message = problem.detail || problem.title;
  } catch {
    // Keep the status-based message for non-JSON responses.
  }
  throw new ApiError(result.status, message ?? `Upload failed (HTTP ${result.status}).`);
}
