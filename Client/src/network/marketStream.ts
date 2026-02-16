import type { WsEnvelope } from "./types";
import { ensureWsConnected, getWsClient, onWsStatusChange } from "./wsConnection";

export type MarketTick = {
  symbol: string;
  price: number;
  ts: number;
};

export type MarketListener = (ticks: MarketTick[]) => void;

const listeners = new Set<MarketListener>();
const symbolRefCounts = new Map<string, number>();
let handlerAttached = false;
let statusListenerAttached = false;
const pendingTickMap = new Map<string, MarketTick>();
let dispatchFrameId: number | null = null;

export function subscribeMarket(symbols: string[], listener: MarketListener): () => void {
  const uniqueSymbols = Array.from(
    new Set(symbols.map((symbol) => symbol.trim()).filter((symbol) => symbol.length > 0))
  );

  if (!handlerAttached) {
    handlerAttached = true;
    getWsClient().on("mkt.tick", handleTick);
  }

  if (!statusListenerAttached) {
    statusListenerAttached = true;
    onWsStatusChange((status) => {
      if (status !== "connected" || symbolRefCounts.size === 0) {
        return;
      }
      const symbols = Array.from(symbolRefCounts.keys());
      getWsClient().send("market.subscribe", { symbols });
    });
  }

  listeners.add(listener);

  const newlyAdded: string[] = [];
  for (const symbol of uniqueSymbols) {
    const current = symbolRefCounts.get(symbol) ?? 0;
    symbolRefCounts.set(symbol, current + 1);
    if (current === 0) {
      newlyAdded.push(symbol);
    }
  }

  if (newlyAdded.length > 0) {
    ensureWsConnected()
      .then(() => {
        getWsClient().send("market.subscribe", { symbols: newlyAdded });
      })
      .catch(() => {
        // 连接处理由统一逻辑负责，此处忽略
      });
  }

  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      pendingTickMap.clear();
      if (dispatchFrameId !== null) {
        window.cancelAnimationFrame(dispatchFrameId);
        dispatchFrameId = null;
      }
    }

    const removed: string[] = [];
    for (const symbol of uniqueSymbols) {
      const current = symbolRefCounts.get(symbol) ?? 0;
      if (current <= 1) {
        symbolRefCounts.delete(symbol);
        removed.push(symbol);
      } else {
        symbolRefCounts.set(symbol, current - 1);
      }
    }

    if (removed.length > 0) {
      ensureWsConnected()
        .then(() => {
          getWsClient().send("market.unsubscribe", { symbols: removed });
        })
        .catch(() => {
          // 连接处理由统一逻辑负责，此处忽略
        });
    }
  };
}

function handleTick(message: WsEnvelope<unknown>): void {
  const ticks = parseTicks(message.data);
  if (ticks.length === 0) {
    return;
  }

  // 将同一帧内的重复 symbol 合并，减少高频推送导致的渲染抖动。
  for (const tick of ticks) {
    pendingTickMap.set(tick.symbol, tick);
  }

  scheduleDispatch();
}

function scheduleDispatch(): void {
  if (dispatchFrameId !== null) {
    return;
  }

  dispatchFrameId = window.requestAnimationFrame(() => {
    dispatchFrameId = null;
    if (pendingTickMap.size === 0 || listeners.size === 0) {
      pendingTickMap.clear();
      return;
    }

    const mergedTicks = Array.from(pendingTickMap.values());
    pendingTickMap.clear();

    for (const listener of listeners) {
      listener(mergedTicks);
    }
  });
}

function parseTicks(payload: unknown): MarketTick[] {
  const raw = extractArray(payload);
  if (!raw) {
    return [];
  }

  const ticks: MarketTick[] = [];
  for (const entry of raw) {
    if (!Array.isArray(entry) || entry.length < 2) {
      continue;
    }
    const symbol = typeof entry[0] === "string" ? entry[0] : "";
    const price = typeof entry[1] === "number" ? entry[1] : Number(entry[1]);
    const ts = typeof entry[2] === "number" ? entry[2] : Date.now();
    if (!symbol || !Number.isFinite(price)) {
      continue;
    }
    ticks.push({ symbol, price, ts });
  }

  return ticks;
}

function extractArray(payload: unknown): unknown[] | null {
  if (Array.isArray(payload)) {
    return payload;
  }

  if (payload && typeof payload === "object" && "a" in payload) {
    const array = (payload as { a?: unknown }).a;
    if (Array.isArray(array)) {
      return array;
    }
  }

  return null;
}
