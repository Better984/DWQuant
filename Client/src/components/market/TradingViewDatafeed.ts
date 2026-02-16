import { HttpClient } from "../../network/index.ts";
import { subscribeMarket } from "../../network/index.ts";

type TvSymbol = {
  name: string;
  description: string;
  type: string;
  exchange: string;
  ticker: string;
};

type TvBar = {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume?: number;
};

type MarketSubscribe = {
  symbol: string;
  exchange: string;
  resolution: string;
  subscriberUID: string;
  onRealtimeCallback: (bar: TvBar) => void;
  unsubscribe: () => void;
};

const SUPPORTED_RESOLUTIONS = ["1", "3", "5", "15", "30", "60", "240", "D", "W"];
const SYMBOLS = ["BTC/USDT", "ETH/USDT", "XRP/USDT", "SOL/USDT", "DOGE/USDT", "BNB/USDT"];
const EXCHANGES = ["Binance", "OKX", "Bitget"];

const RESOLUTION_TO_TIMEFRAME: Record<string, string> = {
  "1": "m1",
  "3": "m3",
  "5": "m5",
  "15": "m15",
  "30": "m30",
  "60": "h1",
  "240": "h4",
  "D": "d1",
  "1D": "d1",
  "W": "w1",
  "1W": "w1",
};

const RESOLUTION_TO_MS: Record<string, number> = {
  "1": 60_000,
  "3": 180_000,
  "5": 300_000,
  "15": 900_000,
  "30": 1_800_000,
  "60": 3_600_000,
  "240": 14_400_000,
  "D": 86_400_000,
  "1D": 86_400_000,
  "W": 604_800_000,
  "1W": 604_800_000,
};

export class TradingViewDatafeed {
  private http = new HttpClient();
  private symbols: TvSymbol[] = buildSymbols();
  private subscriptions = new Map<string, MarketSubscribe>();
  private lastBars = new Map<string, TvBar>();

  onReady(callback: (config: unknown) => void): void {
    const config = {
      supported_resolutions: SUPPORTED_RESOLUTIONS,
      supports_search: true,
      supports_group_request: false,
      supports_marks: false,
      supports_timescale_marks: false,
      supports_time: true,
      exchanges: EXCHANGES.map((exchange) => ({
        value: exchange,
        name: exchange,
        desc: `${exchange} Futures`,
      })),
      symbols_types: [
        {
          name: "crypto",
          value: "crypto",
        },
      ],
    };
    setTimeout(() => callback(config), 0);
  }

  searchSymbols(
    userInput: string,
    exchange: string,
    symbolType: string,
    onResult: (result: TvSymbol[]) => void
  ): void {
    const input = userInput.trim().toLowerCase();
    const results = this.symbols.filter((symbol) => {
      if (exchange && symbol.exchange !== exchange) {
        return false;
      }
      if (symbolType && symbol.type !== symbolType) {
        return false;
      }
      if (!input) {
        return true;
      }
      return (
        symbol.name.toLowerCase().includes(input) ||
        symbol.description.toLowerCase().includes(input)
      );
    });
    onResult(results);
  }

  resolveSymbol(
    symbolName: string,
    onResolve: (symbolInfo: unknown) => void,
    onError: (reason: string) => void
  ): void {
    console.info("[TV] 解析请求:", symbolName);
    const found = this.symbols.find((symbol) => symbol.name === symbolName || symbol.ticker === symbolName);
    if (!found) {
      console.warn("[TV] 未找到交易对:", symbolName);
      onError("未找到交易对");
      return;
    }

    const symbolInfo = {
      name: found.name,
      ticker: found.ticker,
      description: found.description,
      type: found.type,
      session: "24x7",
      timezone: "Asia/Shanghai",
      exchange: found.exchange,
      listed_exchange: found.exchange,
      minmov: 1,
      pricescale: 100,
      has_intraday: true,
      has_weekly_and_monthly: true,
      supported_resolutions: SUPPORTED_RESOLUTIONS,
      volume_precision: 2,
      data_status: "streaming",
    };

    setTimeout(() => {
      console.info("[TV] 解析完成:", symbolInfo);
      onResolve(symbolInfo);
    }, 0);
  }

  async getBars(
    symbolInfo: { name: string },
    resolution: string,
    periodParams: { from: number; to: number },
    onResult: (bars: TvBar[], meta: { noData: boolean }) => void,
    onError: (reason: string) => void
  ): Promise<void> {
    const parsed = parseSymbolName(symbolInfo.name);
    if (!parsed) {
      console.warn("历史K线获取：交易对无效", symbolInfo.name);
      onResult([], { noData: true });
      return;
    }

    const timeframe = RESOLUTION_TO_TIMEFRAME[resolution];
    if (!timeframe) {
      console.warn("历史K线获取：不支持的周期", resolution);
      onError("不支持的周期");
      return;
    }

    const startMs = periodParams.from * 1000;
    const endMs = periodParams.to * 1000;
    const count = resolveCount(startMs, endMs, resolution);
    console.info("历史K线获取：请求", {
      symbol: parsed.symbol,
      exchange: parsed.exchange,
      resolution,
      timeframe,
      startMs,
      endMs,
      count,
    });

    const payload: Record<string, string | number> = {
      exchange: parsed.exchange,
      timeframe,
      symbol: toSymbolEnum(parsed.symbol),
      count,
    };

    if (Number.isFinite(startMs) && startMs > 0) {
      payload.startTime = formatDateTime(startMs);
    }
    if (Number.isFinite(endMs) && endMs > 0) {
      payload.endTime = formatDateTime(endMs);
    }

    try {
      const data = await this.http.postProtocol<OHLCV[]>("/api/marketdata/history", "marketdata.kline.history", payload);
      console.info("历史K线获取：响应", {
        symbol: parsed.symbol,
        exchange: parsed.exchange,
        count: data.length,
      });
      const bars = data
        .map((item) => toBar(item))
        .filter((bar): bar is TvBar => bar !== null);

      if (bars.length > 0) {
        const lastBarKey = buildBarKey(parsed.exchange, parsed.symbol, resolution);
        this.lastBars.set(lastBarKey, bars[bars.length - 1]);
      }

      console.info("历史K线获取：结果", { bars: bars.length, noData: bars.length === 0 });
      onResult(bars, { noData: bars.length === 0 });
    } catch (error) {
      const message = error instanceof Error ? error.message : "历史K线加载失败";
      console.error("历史K线获取：异常", message);
      onError(message);
    }
  }

  subscribeBars(
    symbolInfo: { name: string },
    resolution: string,
    onRealtimeCallback: (bar: TvBar) => void,
    subscriberUID: string
  ): void {
    const parsed = parseSymbolName(symbolInfo.name);
    if (!parsed) {
      console.warn("[TV] 订阅行情失败，交易对无效:", symbolInfo.name);
      return;
    }

    console.info("[TV] 订阅行情:", {
      uid: subscriberUID,
      symbol: parsed.symbol,
      exchange: parsed.exchange,
      resolution,
    });
    const unsubscribe = subscribeMarket([parsed.symbol], (ticks) => {
      if (ticks.length > 0) {
        // console.info("[TV] 实时行情:", ticks.slice(0, 3));
      }
      for (const tick of ticks) {
        if (tick.symbol !== parsed.symbol) {
          continue;
        }
        const bar = this.updateBar(parsed.exchange, parsed.symbol, resolution, tick.price, tick.ts);
        if (bar) {
          console.info("[TV] 实时K线:", bar);
          onRealtimeCallback(bar);
        }
      }
    });

    this.subscriptions.set(subscriberUID, {
      symbol: parsed.symbol,
      exchange: parsed.exchange,
      resolution,
      subscriberUID,
      onRealtimeCallback,
      unsubscribe,
    });
  }

  unsubscribeBars(subscriberUID: string): void {
    const subscription = this.subscriptions.get(subscriberUID);
    if (subscription) {
      subscription.unsubscribe();
      this.subscriptions.delete(subscriberUID);
    }
  }

  destroy(): void {
    for (const subscription of this.subscriptions.values()) {
      subscription.unsubscribe();
    }
    this.subscriptions.clear();
    this.lastBars.clear();
  }

  private updateBar(exchange: string, symbol: string, resolution: string, price: number, ts: number): TvBar | null {
    const intervalMs = RESOLUTION_TO_MS[resolution];
    if (!intervalMs) {
      return null;
    }

    const barTime = Math.floor(ts / intervalMs) * intervalMs;
    const key = buildBarKey(exchange, symbol, resolution);
    const lastBar = this.lastBars.get(key);

    if (!lastBar || barTime > lastBar.time) {
      const newBar: TvBar = {
        time: barTime,
        open: price,
        high: price,
        low: price,
        close: price,
        volume: 0,
      };
      this.lastBars.set(key, newBar);
      return newBar;
    }

    if (barTime < lastBar.time) {
      return null;
    }

    const nextBar: TvBar = {
      ...lastBar,
      high: Math.max(lastBar.high, price),
      low: Math.min(lastBar.low, price),
      close: price,
    };
    this.lastBars.set(key, nextBar);
    return nextBar;
  }
}

type OHLCV = {
  timestamp?: number | null;
  open?: number | null;
  high?: number | null;
  low?: number | null;
  close?: number | null;
  volume?: number | null;
};

function buildSymbols(): TvSymbol[] {
  const result: TvSymbol[] = [];
  for (const exchange of EXCHANGES) {
    for (const symbol of SYMBOLS) {
      const name = `${exchange}:${symbol}`;
      result.push({
        name,
        ticker: name,
        description: symbol,
        type: "crypto",
        exchange,
      });
    }
  }
  return result;
}

function parseSymbolName(symbolName: string): { exchange: string; symbol: string } | null {
  if (!symbolName) {
    return null;
  }
  if (symbolName.includes(":")) {
    const [exchange, symbol] = symbolName.split(":");
    if (!exchange || !symbol) {
      return null;
    }
    return { exchange, symbol };
  }
  return { exchange: EXCHANGES[0], symbol: symbolName };
}

function toSymbolEnum(symbol: string): string {
  return symbol.replace("/", "_").replace("-", "_").toUpperCase();
}

function resolveCount(startMs: number, endMs: number, resolution: string): number {
  const intervalMs = RESOLUTION_TO_MS[resolution] ?? 60_000;
  if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || endMs <= startMs) {
    return 2000;
  }
  const count = Math.ceil((endMs - startMs) / intervalMs) + 1;
  return Math.min(Math.max(count, 1), 2000);
}

function toBar(item: OHLCV): TvBar | null {
  if (!item.timestamp) {
    return null;
  }
  const open = item.open ?? item.close ?? 0;
  const high = item.high ?? open;
  const low = item.low ?? open;
  const close = item.close ?? open;
  return {
    time: item.timestamp,
    open,
    high,
    low,
    close,
    volume: item.volume ?? 0,
  };
}

function formatDateTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function buildBarKey(exchange: string, symbol: string, resolution: string): string {
  return `${exchange}:${symbol}:${resolution}`;
}


