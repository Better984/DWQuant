import { getNetworkConfig } from "./config";
import { generateReqId } from "./requestId";
import { notifyAuthExpired } from "./authEvents";
import { clearToken } from "./tokenStore";
import type { ProtocolEnvelope, ProtocolRequest } from "./types";

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
  skipAuth?: boolean;
};

export type ProtocolRequestOptions = {
  headers?: Record<string, string>;
  timeoutMs?: number;
  signal?: AbortSignal;
  reqId?: string;
  skipAuth?: boolean;
};

export class HttpError extends Error {
  status: number;
  code?: number;
  reqId?: string;
  traceId?: string;
  payload?: unknown;

  constructor(message: string, status: number, code?: number, reqId?: string, traceId?: string, payload?: unknown) {
    super(message);
    this.name = "HttpError";
    this.status = status;
    this.code = code;
    this.reqId = reqId;
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
    const { response, payload } = await this.executeRequest(options);
    if (!response.ok) {
      throw toHttpError(payload, response.status);
    }
    return payload as T;
  }

  get<T = unknown>(path: string, query?: RequestOptions["query"], options: Omit<RequestOptions, "path" | "query"> = {}): Promise<T> {
    return this.request<T>({ ...options, method: "GET", path, query });
  }

  post<T = unknown>(path: string, body?: unknown, options: Omit<RequestOptions, "path" | "body"> = {}): Promise<T> {
    return this.request<T>({ ...options, method: "POST", path, body });
  }

  async postProtocol<TResponse = unknown, TData = unknown>(
    path: string,
    type: string,
    data?: TData,
    options: ProtocolRequestOptions = {}
  ): Promise<TResponse> {
    const reqId = options.reqId ?? generateReqId();
    const payload: ProtocolRequest<TData> = {
      type,
      reqId,
      ts: Date.now(),
      data: data ?? null,
    };

    const { response, payload: responsePayload } = await this.executeRequest({
      method: "POST",
      path,
      body: payload,
      headers: {
        "X-Req-Id": reqId,
        ...options.headers,
      },
      timeoutMs: options.timeoutMs,
      signal: options.signal,
      skipAuth: options.skipAuth,
    });

    const envelope = ensureProtocolEnvelope(responsePayload, response.status);
    if (!response.ok || envelope.code !== 0) {
      throw toProtocolError(envelope, response.status);
    }

    return envelope.data as TResponse;
  }

  async postForm<TResponse = unknown>(
    path: string,
    type: string,
    formData: FormData,
    options: ProtocolRequestOptions = {}
  ): Promise<TResponse> {
    const reqId = options.reqId ?? generateReqId();
    const now = Date.now();
    formData.set("type", type);
    formData.set("reqId", reqId);
    formData.set("ts", String(now));

    const { response, payload } = await this.executeRequest({
      method: "POST",
      path,
      body: formData,
      headers: {
        "X-Req-Id": reqId,
        ...options.headers,
      },
      timeoutMs: options.timeoutMs,
      signal: options.signal,
      skipAuth: options.skipAuth,
    });

    const envelope = ensureProtocolEnvelope(payload, response.status);
    if (!response.ok || envelope.code !== 0) {
      throw toProtocolError(envelope, response.status);
    }

    return envelope.data as TResponse;
  }

  private async executeRequest(options: RequestOptions): Promise<{ response: Response; payload: unknown }> {
    const url = buildUrl(this.baseUrl, options.path, options.query);
    const controller = new AbortController();
    const timeoutMs = options.timeoutMs ?? this.defaultTimeoutMs;
    const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

    try {
      const headers: Record<string, string> = {
        "Accept": "application/json",
        ...options.headers,
      };

      const body = options.body;
      const isFormData = typeof FormData !== "undefined" && body instanceof FormData;

      if (body !== undefined && !isFormData) {
        headers["Content-Type"] = "application/json";
      }

      // 登录与注册接口不需要 Authorization
      const isAuthEndpoint = options.skipAuth ?? (
        options.path.includes("/api/auth/login") || options.path.includes("/api/auth/register")
      );
      const token = this.tokenProvider?.();
      if (token && !isAuthEndpoint) {
        headers["Authorization"] = `Bearer ${token}`;
      }

      const response = await fetch(url, {
        method: options.method ?? (body ? "POST" : "GET"),
        headers,
        body: body === undefined ? undefined : (isFormData ? body : JSON.stringify(body)),
        signal: mergeSignals(controller.signal, options.signal),
      });

      const payload = await parseResponse(response);
      handleAuthExpired(options.path, response.status, payload);
      return { response, payload };
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        throw new HttpError("请求超时", 408);
      }

      if (error instanceof HttpError) {
        throw error;
      }

      const message = error instanceof Error ? error.message : "网络错误";
      throw new HttpError(message, 0);
    } finally {
      window.clearTimeout(timeoutId);
    }
  }
}

const AUTH_ERROR_CODES = new Set([2000, 2001, 2002]);
const AUTH_BYPASS_PATHS = new Set(["/api/auth/login", "/api/auth/register"]);

function handleAuthExpired(path: string, status: number, payload: unknown): void {
  if (!shouldNotifyAuthExpired(path, status, payload)) {
    return;
  }

  clearToken();
  notifyAuthExpired();
}

function shouldNotifyAuthExpired(path: string, status: number, payload: unknown): boolean {
  const normalizedPath = normalizePath(path);
  if (AUTH_BYPASS_PATHS.has(normalizedPath)) {
    return false;
  }

  const protocolCode = resolveProtocolCode(payload);
  if (typeof protocolCode === "number") {
    return AUTH_ERROR_CODES.has(protocolCode);
  }

  return status === 401;
}

function resolveProtocolCode(payload: unknown): number | undefined {
  if (isProtocolEnvelope(payload) && typeof payload.code === "number") {
    return payload.code;
  }

  if (payload && typeof payload === "object") {
    const code = (payload as { code?: unknown }).code;
    return typeof code === "number" ? code : undefined;
  }

  return undefined;
}

function normalizePath(path: string): string {
  if (!path) {
    return "/";
  }

  const withoutQuery = path.split("?")[0]?.trim() ?? "/";
  if (withoutQuery.length <= 1) {
    return "/";
  }

  return withoutQuery.endsWith("/") ? withoutQuery.slice(0, -1) : withoutQuery;
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

function ensureProtocolEnvelope(payload: unknown, status: number): ProtocolEnvelope<unknown> {
  if (isProtocolEnvelope(payload)) {
    return payload;
  }
  throw new HttpError("响应格式不正确", status, undefined, undefined, undefined, payload);
}

function isProtocolEnvelope(payload: unknown): payload is ProtocolEnvelope<unknown> {
  if (!payload || typeof payload !== "object") {
    return false;
  }
  return "type" in payload && "ts" in payload && "code" in payload;
}

function toProtocolError(envelope: ProtocolEnvelope<unknown>, status: number): HttpError {
  return new HttpError(
    envelope.msg ?? "请求失败",
    status,
    envelope.code,
    envelope.reqId ?? undefined,
    envelope.traceId ?? undefined,
    envelope
  );
}

function toHttpError(payload: unknown, status: number): HttpError {
  if (isProtocolEnvelope(payload)) {
    return toProtocolError(payload, status);
  }

  if (payload && typeof payload === "object") {
    const message = (payload as { message?: string }).message;
    const code = (payload as { code?: number }).code;
    const traceId = (payload as { traceId?: string }).traceId;
    if (typeof message === "string") {
      return new HttpError(message, status, typeof code === "number" ? code : undefined, undefined, traceId, payload);
    }
  }

  return new HttpError("请求失败", status, undefined, undefined, undefined, payload);
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
