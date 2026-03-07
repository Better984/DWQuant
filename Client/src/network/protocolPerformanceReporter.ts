import { getNetworkConfig } from "./config";
import { generateReqId } from "./requestId";
import { getToken } from "./tokenStore";
import type { ProtocolRequest } from "./types";

export type ProtocolPerformanceMetric = {
  reqId: string;
  transport: "http" | "ws";
  protocolType: string;
  requestPath?: string;
  httpMethod?: string;
  systemName?: string;
  clientStartedAtMs: number;
  clientCompletedAtMs: number;
  clientElapsedMs: number;
  protocolCode?: number;
  httpStatus?: number;
  isSuccess: boolean;
  isTimeout: boolean;
  errorMessage?: string;
};

const MAX_QUEUE_SIZE = 2000;
const FLUSH_BATCH_SIZE = 50;
const FLUSH_INTERVAL_MS = 5000;

let queue: ProtocolPerformanceMetric[] = [];
let flushTimer: number | null = null;
let flushInFlight: Promise<void> | null = null;
let lifecycleBound = false;

export function recordProtocolPerformance(metric: ProtocolPerformanceMetric): void {
  if (!metric.reqId || !metric.protocolType) {
    return;
  }

  queue.push(normalizeMetric(metric));
  if (queue.length > MAX_QUEUE_SIZE) {
    queue = queue.slice(queue.length - MAX_QUEUE_SIZE);
  }

  ensureLifecycleBindings();
  scheduleFlush();

  if (queue.length >= FLUSH_BATCH_SIZE) {
    void flushProtocolPerformance();
  }
}

export async function flushProtocolPerformance(force = false): Promise<void> {
  if (flushInFlight) {
    return flushInFlight;
  }

  if (queue.length === 0) {
    return;
  }

  clearFlushTimer();
  const batch = queue.splice(0, force ? Math.min(queue.length, FLUSH_BATCH_SIZE * 2) : Math.min(queue.length, FLUSH_BATCH_SIZE));
  if (batch.length === 0) {
    return;
  }

  flushInFlight = sendBatch(batch, force)
    .catch(() => {
      queue = [...batch, ...queue];
      if (queue.length > MAX_QUEUE_SIZE) {
        queue = queue.slice(queue.length - MAX_QUEUE_SIZE);
      }
    })
    .finally(() => {
      flushInFlight = null;
      if (queue.length > 0 && !force) {
        scheduleFlush();
      }
    });

  return flushInFlight;
}

function ensureLifecycleBindings(): void {
  if (lifecycleBound || typeof window === "undefined") {
    return;
  }

  lifecycleBound = true;
  window.addEventListener("beforeunload", () => {
    void flushProtocolPerformance(true);
  });

  if (typeof document !== "undefined") {
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "hidden") {
        void flushProtocolPerformance(true);
      }
    });
  }
}

function scheduleFlush(): void {
  if (flushTimer !== null || typeof window === "undefined") {
    return;
  }

  flushTimer = window.setTimeout(() => {
    flushTimer = null;
    void flushProtocolPerformance();
  }, FLUSH_INTERVAL_MS);
}

function clearFlushTimer(): void {
  if (flushTimer === null || typeof window === "undefined") {
    return;
  }

  window.clearTimeout(flushTimer);
  flushTimer = null;
}

async function sendBatch(batch: ProtocolPerformanceMetric[], keepalive: boolean): Promise<void> {
  const config = getNetworkConfig();
  const reqId = generateReqId();
  const payload: ProtocolRequest<{
    items: Array<{
      reqId: string;
      transport: "http" | "ws";
      protocolType: string;
      requestPath?: string;
      httpMethod?: string;
      systemName?: string;
      clientStartedAtMs: number;
      clientCompletedAtMs: number;
      clientElapsedMs: number;
      protocolCode?: number;
      httpStatus?: number;
      isSuccess: boolean;
      isTimeout: boolean;
      errorMessage?: string;
    }>;
  }> = {
    type: "monitoring.protocol.performance.report",
    reqId,
    ts: Date.now(),
    data: {
      items: batch.map((item) => ({
        reqId: item.reqId,
        transport: item.transport,
        protocolType: item.protocolType,
        requestPath: item.requestPath,
        httpMethod: item.httpMethod,
        systemName: item.systemName,
        clientStartedAtMs: item.clientStartedAtMs,
        clientCompletedAtMs: item.clientCompletedAtMs,
        clientElapsedMs: item.clientElapsedMs,
        protocolCode: item.protocolCode,
        httpStatus: item.httpStatus,
        isSuccess: item.isSuccess,
        isTimeout: item.isTimeout,
        errorMessage: item.errorMessage,
      })),
    },
  };

  const url = new URL("/api/monitoring/protocol-performance/report", config.apiBaseUrl).toString();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    "Accept": "application/json",
    "X-Req-Id": reqId,
  };

  const token = getToken();
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
    keepalive,
  });

  if (!response.ok) {
    throw new Error("协议性能上报失败");
  }
}

function normalizeMetric(metric: ProtocolPerformanceMetric): ProtocolPerformanceMetric {
  const config = getNetworkConfig();
  return {
    ...metric,
    transport: metric.transport === "ws" ? "ws" : "http",
    protocolType: metric.protocolType.trim(),
    systemName: metric.systemName ?? config.system,
    clientElapsedMs: Math.max(0, Math.round(metric.clientElapsedMs)),
    errorMessage: metric.errorMessage?.trim() || undefined,
  };
}
