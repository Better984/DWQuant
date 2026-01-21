import { getNetworkConfig } from "./config";
import type { ApiResponse, ErrorResponse } from "./types";

export type HttpClientOptions = {
  baseUrl?: string;
  tokenProvider?: () => string | null;
  defaultTimeoutMs?: number;
};

export type RequestOptions = {
  method?: string;
  path: string;
  query?: Record<string, string | number | boolean | null | undefined>;
  body?: unknown;
  headers?: Record<string, string>;
  timeoutMs?: number;
  signal?: AbortSignal;
};

export class HttpError extends Error {
  status: number;
  code?: string;
  traceId?: string;
  payload?: unknown;

  constructor(message: string, status: number, code?: string, traceId?: string, payload?: unknown) {
    super(message);
    this.name = "HttpError";
    this.status = status;
    this.code = code;
    this.traceId = traceId;
    this.payload = payload;
  }
}

export class HttpClient {
  private baseUrl: string;
  private tokenProvider?: () => string | null;
  private defaultTimeoutMs: number;

  constructor(options: HttpClientOptions = {}) {
    const config = getNetworkConfig();
    this.baseUrl = options.baseUrl ?? config.apiBaseUrl;
    this.tokenProvider = options.tokenProvider;
    this.defaultTimeoutMs = options.defaultTimeoutMs ?? 15000;
  }

  setTokenProvider(provider?: () => string | null): void {
    this.tokenProvider = provider;
  }

  async request<T = unknown>(options: RequestOptions): Promise<T> {
    const url = buildUrl(this.baseUrl, options.path, options.query);
    const controller = new AbortController();
    const timeoutMs = options.timeoutMs ?? this.defaultTimeoutMs;
    const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

    try {
      const headers: Record<string, string> = {
        "Accept": "application/json",
        ...options.headers,
      };

      if (options.body !== undefined) {
        headers["Content-Type"] = "application/json";
      }

      // 登录和注册接口不需要 Authorization header
      const isAuthEndpoint = options.path.includes("/api/auth/login") || options.path.includes("/api/auth/register");
      const token = this.tokenProvider?.();
      if (token && !isAuthEndpoint) {
        headers["Authorization"] = `Bearer ${token}`;
      }

      const response = await fetch(url, {
        method: options.method ?? (options.body ? "POST" : "GET"),
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: mergeSignals(controller.signal, options.signal),
      });

      const payload = await parseResponse(response);
      if (!response.ok) {
        throw toHttpError(payload, response.status);
      }

      if (isApiResponse(payload)) {
        if (!payload.success) {
          throw new HttpError(payload.message ?? "Request failed", response.status, undefined, undefined, payload);
        }
        return payload.data as T;
      }

      return payload as T;
    } catch (error) {
      if (error instanceof HttpError) {
        throw error;
      }

      if (error instanceof DOMException && error.name === "AbortError") {
        throw new HttpError("Request timeout", 408);
      }

      const message = error instanceof Error ? error.message : "Network error";
      throw new HttpError(message, 0);
    } finally {
      window.clearTimeout(timeoutId);
    }
  }

  get<T = unknown>(path: string, query?: RequestOptions["query"], options: Omit<RequestOptions, "path" | "query"> = {}): Promise<T> {
    return this.request<T>({ ...options, method: "GET", path, query });
  }

  post<T = unknown>(path: string, body?: unknown, options: Omit<RequestOptions, "path" | "body"> = {}): Promise<T> {
    return this.request<T>({ ...options, method: "POST", path, body });
  }
}

function buildUrl(baseUrl: string, path: string, query?: RequestOptions["query"]): string {
  const url = new URL(path.startsWith("/") ? path : `/${path}`, baseUrl);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === null || value === undefined) {
        continue;
      }
      url.searchParams.set(key, String(value));
    }
  }
  return url.toString();
}

async function parseResponse(response: Response): Promise<unknown> {
  const contentType = response.headers.get("content-type") ?? "";
  const text = await response.text();
  if (!text) {
    return null;
  }

  if (contentType.includes("application/json")) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }

  return text;
}

function isApiResponse(payload: unknown): payload is ApiResponse<unknown> {
  if (!payload || typeof payload !== "object") {
    return false;
  }
  return "success" in payload;
}

function toHttpError(payload: unknown, status: number): HttpError {
  if (payload && typeof payload === "object") {
    const errorPayload = payload as ErrorResponse;
    if (typeof errorPayload.code === "string" && typeof errorPayload.message === "string") {
      return new HttpError(errorPayload.message, status, errorPayload.code, errorPayload.traceId, payload);
    }
  }
  return new HttpError("Request failed", status, undefined, undefined, payload);
}

function mergeSignals(primary: AbortSignal, secondary?: AbortSignal): AbortSignal {
  if (!secondary) {
    return primary;
  }

  if (primary.aborted) {
    return primary;
  }

  if (secondary.aborted) {
    return secondary;
  }

  const controller = new AbortController();
  const onAbort = () => controller.abort();
  primary.addEventListener("abort", onAbort);
  secondary.addEventListener("abort", onAbort);

  return controller.signal;
}
