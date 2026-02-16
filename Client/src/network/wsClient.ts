import { buildWsUrl, getNetworkConfig } from "./config";
import { clearToken } from "./tokenStore";
import { notifyAuthExpired } from "./authEvents";
import { generateReqId } from "./requestId";
import type { ProtocolRequest, WsEnvelope } from "./types";

export type WsClientOptions = {
  baseUrl?: string;
  path?: string;
  system?: string;
  tokenProvider?: () => string | null;
  pingIntervalMs?: number;
  pongTimeoutMs?: number;
  reconnectDelayMs?: number;
  reconnectMaxAttempts?: number;
  requestTimeoutMs?: number;
  onOpen?: () => void;
  onClose?: () => void;
  onError?: (event: Event) => void;
};

export type WsHandler<T = unknown> = (message: WsEnvelope<T>) => void;

type PendingRequest = {
  resolve: (value: WsEnvelope<unknown>) => void;
  reject: (error: Error) => void;
  timeoutId: number;
};

export class WsClient {
  private baseUrl: string;
  private path: string;
  private system: string;
  private tokenProvider?: () => string | null;
  private pingIntervalMs: number;
  private pongTimeoutMs: number;
  private reconnectDelayMs: number;
  private maxReconnectAttempts: number;
  private reconnectAttempts = 0;
  private requestTimeoutMs: number;
  private onOpen?: () => void;
  private onClose?: () => void;
  private onError?: (event: Event) => void;
  private socket: WebSocket | null = null;
  private reconnectTimer: number | null = null;
  private pingTimer: number | null = null;
  private pongTimer: number | null = null;
  private awaitingPong = false;
  private manuallyClosed = false;
  private pending = new Map<string, PendingRequest>();
  private handlers = new Map<string, Set<WsHandler>>();
  private anyHandlers = new Set<WsHandler>();

  constructor(options: WsClientOptions = {}) {
    const config = getNetworkConfig();
    this.baseUrl = options.baseUrl ?? config.wsBaseUrl;
    this.path = options.path ?? config.wsPath;
    this.system = options.system ?? config.system;
    this.tokenProvider = options.tokenProvider;
    this.pingIntervalMs = options.pingIntervalMs ?? 15000;
    this.pongTimeoutMs = options.pongTimeoutMs ?? 5000;
    // 固定重连间隔为 5 秒，可通过 options 覆盖
    this.reconnectDelayMs = options.reconnectDelayMs ?? 5000;
    // 最大重连次数默认 3 次
    this.maxReconnectAttempts = options.reconnectMaxAttempts ?? 3;
    this.requestTimeoutMs = options.requestTimeoutMs ?? 10000;
    this.onOpen = options.onOpen;
    this.onClose = options.onClose;
    this.onError = options.onError;
  }

  connect(): Promise<void> {
    this.manuallyClosed = false;
    if (this.socket && (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING)) {
      return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
      try {
        const token = this.tokenProvider?.();
        const url = buildWsUrl(this.baseUrl, this.path, this.system, token);
        const socket = new WebSocket(url);
        this.socket = socket;

        socket.addEventListener("open", () => {
          this.startPing();
          // 连接成功后重置重连计数
          this.reconnectAttempts = 0;
          dispatchWsPopup({ kind: "reconnect_success" });
          this.onOpen?.();
          resolve();
        });

        socket.addEventListener("message", (event) => {
          this.onMessage(event.data);
        });

        socket.addEventListener("error", (event) => {
          this.onError?.(event);
          reject(new Error("WebSocket 连接错误"));
        });

        socket.addEventListener("close", () => {
          this.stopPing();
          this.rejectAllPending(new Error("WebSocket 已关闭"));
          this.onClose?.();
          if (!this.manuallyClosed) {
            this.scheduleReconnect();
          }
        });
      } catch (error) {
        reject(error instanceof Error ? error : new Error("WebSocket 连接错误"));
      }
    });
  }

  disconnect(): void {
    this.manuallyClosed = true;
    this.clearReconnect();
    this.reconnectAttempts = 0;
    this.stopPing();
    this.clearPongTimeout();
    this.rejectAllPending(new Error("WebSocket 已关闭"));
    if (this.socket && this.socket.readyState === WebSocket.OPEN) {
      this.socket.close(1000, "client_close");
    }
    this.socket = null;
  }

  isConnected(): boolean {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  send(type: string, payload?: unknown, reqId?: string): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      throw new Error("WebSocket 未连接");
    }

    const requestId = reqId ?? generateReqId();
    const envelope: ProtocolRequest = {
      type,
      reqId: requestId,
      ts: Date.now(),
      data: payload ?? null,
    };

    this.socket.send(JSON.stringify(envelope));
  }

  request<TResponse = unknown>(type: string, payload?: unknown, reqId?: string): Promise<WsEnvelope<TResponse>> {
    const requestId = reqId ?? generateReqId();
    return new Promise((resolve, reject) => {
      const timeoutId = window.setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error("WebSocket 请求超时"));
      }, this.requestTimeoutMs);

      this.pending.set(requestId, {
        resolve: resolve as (value: WsEnvelope<unknown>) => void,
        reject,
        timeoutId,
      });

      try {
        this.send(type, payload, requestId);
      } catch (error) {
        window.clearTimeout(timeoutId);
        this.pending.delete(requestId);
        reject(error instanceof Error ? error : new Error("WebSocket 发送失败"));
      }
    });
  }

  on<T = unknown>(type: string, handler: WsHandler<T>): () => void {
    const set = this.handlers.get(type) ?? new Set();
    set.add(handler as WsHandler);
    this.handlers.set(type, set);
    return () => {
      set.delete(handler as WsHandler);
      if (set.size === 0) {
        this.handlers.delete(type);
      }
    };
  }

  onAny(handler: WsHandler): () => void {
    this.anyHandlers.add(handler);
    return () => this.anyHandlers.delete(handler);
  }

  private onMessage(raw: unknown): void {
    if (typeof raw !== "string") {
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      return;
    }

    if (!parsed || typeof parsed !== "object" || !("type" in parsed)) {
      return;
    }

    const message = parsed as WsEnvelope;

    if (message.type === "kicked") {
      this.disconnect();
      clearToken();
      notifyAuthExpired();
      return;
    }

    if (message.type === "pong") {
      this.clearPongTimeout();
      return;
    }

    if (message.type === "error") {
      // 后端错误响应使用 code 字段（数字），根据协议文档处理鉴权相关错误
      const code = message.code;
      if (code === 2000 || code === 2001 || code === 2002) {
        clearToken();
        notifyAuthExpired();
        return;
      }
    }

    if (message.reqId && this.pending.has(message.reqId)) {
      const pending = this.pending.get(message.reqId);
      if (pending) {
        window.clearTimeout(pending.timeoutId);
        this.pending.delete(message.reqId);
        if (message.code && message.code !== 0) {
          pending.reject(new Error(message.msg ?? "请求失败"));
        } else {
          pending.resolve(message);
        }
      }
      return;
    }

    const handlers = this.handlers.get(message.type);
    if (handlers) {
      for (const handler of handlers) {
        handler(message);
      }
    }

    for (const handler of this.anyHandlers) {
      handler(message);
    }
  }

  private startPing(): void {
    if (this.pingTimer) {
      window.clearInterval(this.pingTimer);
    }

    this.pingTimer = window.setInterval(() => {
      if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
        return;
      }
      if (this.awaitingPong) {
        this.disconnect();
        return;
      }
      this.awaitingPong = true;
      this.schedulePongTimeout();
      this.send("ping", null);
    }, this.pingIntervalMs);
  }

  private stopPing(): void {
    if (this.pingTimer) {
      window.clearInterval(this.pingTimer);
      this.pingTimer = null;
    }
    this.clearPongTimeout();
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) {
      return;
    }
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      dispatchWsPopup({ kind: "reconnect_exhausted", attempt: this.reconnectAttempts, maxAttempts: this.maxReconnectAttempts });
      return;
    }

    this.reconnectAttempts += 1;
    dispatchWsPopup({ kind: "reconnect_attempt", attempt: this.reconnectAttempts, maxAttempts: this.maxReconnectAttempts });

    const delay = this.reconnectDelayMs;
    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = null;
      this.connect()
        .then(() => {
          // 成功逻辑已在 open 事件中处理
        })
        .catch(() => {
          this.scheduleReconnect();
        });
    }, delay);
  }

  private clearReconnect(): void {
    if (this.reconnectTimer) {
      window.clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  private rejectAllPending(error: Error): void {
    for (const pending of this.pending.values()) {
      window.clearTimeout(pending.timeoutId);
      pending.reject(error);
    }
    this.pending.clear();
  }

  private schedulePongTimeout(): void {
    this.clearPongTimeout();
    this.pongTimer = window.setTimeout(() => {
      this.disconnect();
    }, this.pongTimeoutMs);
  }

  private clearPongTimeout(): void {
    this.awaitingPong = false;
    if (this.pongTimer) {
      window.clearTimeout(this.pongTimer);
      this.pongTimer = null;
    }
  }
}

export type WsPopupDetail =
  | { kind: "reconnect_attempt"; attempt: number; maxAttempts: number }
  | { kind: "reconnect_success" }
  | { kind: "reconnect_exhausted"; attempt: number; maxAttempts: number }
  | { kind: "connect_failed"; reason: string };

export function dispatchWsPopup(detail: WsPopupDetail): void {
  if (typeof window === "undefined" || typeof window.dispatchEvent !== "function") {
    return;
  }
  window.dispatchEvent(new CustomEvent<WsPopupDetail>("ws-popup", { detail }));
}
