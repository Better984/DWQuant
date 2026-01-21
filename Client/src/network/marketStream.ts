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
        // Connection handling is centralized; ignore here.
      });
  }

  return () => {
    listeners.delete(listener);

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
          // Connection handling is centralized; ignore here.
        });
    }
  };
}

function handleTick(message: WsEnvelope<unknown>): void {
  const ticks = parseTicks(message.payload);
  if (ticks.length === 0) {
    return;
  }

  for (const listener of listeners) {
    listener(ticks);
  }
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
