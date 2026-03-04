export type KlineTuple = [number, number, number, number, number, number];

export type LocalKlineDatasetSummary = {
  id: string;
  exchange: string;
  symbol: string;
  timeframe: string;
  retentionDays: number;
  startTime: number;
  endTime: number;
  count: number;
  sourceVersion: string;
  sourceGeneratedAtUtc: string;
  sourceUrl: string;
  compressedBytes: number;
  rawBytes: number;
  sha256: string;
  updatedAt: number;
};

export type LocalKlineDataset = LocalKlineDatasetSummary & {
  bars: KlineTuple[];
};

export type SaveLocalKlineDatasetInput = LocalKlineDataset;

export type QueryLocalKlineBarsBeforeResult = {
  bars: KlineTuple[];
  hasMore: boolean;
};

type LocalKlineBarsRecord = {
  id: string;
  bars: KlineTuple[];
  updatedAt: number;
};

const DB_NAME = 'dwquant_kline_offline_cache';
const DB_VERSION = 2;
const META_STORE = 'datasetMeta';
const BARS_STORE = 'datasetBars';
const LEGACY_STORE = 'datasets';

let dbPromise: Promise<IDBDatabase> | null = null;

const toFiniteNumber = (value: unknown, fallback = 0): number => {
  const num = Number(value);
  return Number.isFinite(num) ? num : fallback;
};

const normalizeExchange = (value: string): string => {
  const normalized = (value || '').trim().toLowerCase();
  return normalized || 'binance';
};

const normalizeSymbol = (value: string): string => {
  const base = (value || '').trim().replaceAll('_', '/').replaceAll('-', '/').toUpperCase();
  if (base.includes('/')) {
    const parts = base.split('/').filter(Boolean);
    if (parts.length === 2) {
      return `${parts[0]}/${parts[1]}`;
    }
  }
  if (base.endsWith('USDT') && base.length > 4) {
    return `${base.slice(0, -4)}/USDT`;
  }
  return base || 'BTC/USDT';
};

const normalizeTimeframe = (value: string): string => {
  const normalized = (value || '').trim().toLowerCase();
  return normalized || '1m';
};

const ensureIndexedDbAvailable = () => {
  if (typeof indexedDB === 'undefined') {
    throw new Error('当前环境不支持 IndexedDB');
  }
};

const requestToPromise = <T>(request: IDBRequest<T>): Promise<T> => {
  return new Promise<T>((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB 请求失败'));
  });
};

const transactionDone = (tx: IDBTransaction): Promise<void> => {
  return new Promise<void>((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onabort = () => reject(tx.error ?? new Error('IndexedDB 事务已中止'));
    tx.onerror = () => reject(tx.error ?? new Error('IndexedDB 事务失败'));
  });
};

const openDatabase = async (): Promise<IDBDatabase> => {
  ensureIndexedDbAvailable();
  if (dbPromise) {
    return dbPromise;
  }

  dbPromise = new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);

    request.onupgradeneeded = () => {
      const db = request.result;

      // 升级时清理旧结构并创建新结构，避免列表查询时读取全部 bars。
      if (db.objectStoreNames.contains(LEGACY_STORE)) {
        db.deleteObjectStore(LEGACY_STORE);
      }

      if (!db.objectStoreNames.contains(META_STORE)) {
        const metaStore = db.createObjectStore(META_STORE, { keyPath: 'id' });
        metaStore.createIndex('lookup', ['exchange', 'symbol', 'timeframe'], { unique: true });
        metaStore.createIndex('updatedAt', 'updatedAt', { unique: false });
      }

      if (!db.objectStoreNames.contains(BARS_STORE)) {
        db.createObjectStore(BARS_STORE, { keyPath: 'id' });
      }
    };

    request.onsuccess = () => {
      const db = request.result;
      db.onversionchange = () => {
        db.close();
      };
      resolve(db);
    };

    request.onerror = () => {
      reject(request.error ?? new Error('打开 IndexedDB 失败'));
    };
  }).catch((error) => {
    dbPromise = null;
    throw error;
  });

  return dbPromise;
};

const toTuple = (value: unknown): KlineTuple | null => {
  if (!Array.isArray(value) || value.length < 6) {
    return null;
  }
  const timestamp = toFiniteNumber(value[0], Number.NaN);
  const open = toFiniteNumber(value[1], Number.NaN);
  const high = toFiniteNumber(value[2], Number.NaN);
  const low = toFiniteNumber(value[3], Number.NaN);
  const close = toFiniteNumber(value[4], Number.NaN);
  const volume = toFiniteNumber(value[5], 0);
  if (
    !Number.isFinite(timestamp)
    || !Number.isFinite(open)
    || !Number.isFinite(high)
    || !Number.isFinite(low)
    || !Number.isFinite(close)
  ) {
    return null;
  }
  return [timestamp, open, high, low, close, volume];
};

const normalizeBars = (input: unknown[]): KlineTuple[] => {
  const map = new Map<number, KlineTuple>();
  input.forEach((item) => {
    const tuple = toTuple(item);
    if (!tuple) {
      return;
    }
    map.set(tuple[0], tuple);
  });
  return Array.from(map.values()).sort((a, b) => a[0] - b[0]);
};

const sanitizeSummary = (value: Partial<LocalKlineDatasetSummary>): LocalKlineDatasetSummary => {
  return {
    id: String(value.id || ''),
    exchange: normalizeExchange(String(value.exchange || '')),
    symbol: normalizeSymbol(String(value.symbol || '')),
    timeframe: normalizeTimeframe(String(value.timeframe || '')),
    retentionDays: Math.max(0, Math.floor(toFiniteNumber(value.retentionDays, 0))),
    startTime: toFiniteNumber(value.startTime, 0),
    endTime: toFiniteNumber(value.endTime, 0),
    count: Math.max(0, Math.floor(toFiniteNumber(value.count, 0))),
    sourceVersion: String(value.sourceVersion || ''),
    sourceGeneratedAtUtc: String(value.sourceGeneratedAtUtc || ''),
    sourceUrl: String(value.sourceUrl || ''),
    compressedBytes: Math.max(0, Math.floor(toFiniteNumber(value.compressedBytes, 0))),
    rawBytes: Math.max(0, Math.floor(toFiniteNumber(value.rawBytes, 0))),
    sha256: String(value.sha256 || ''),
    updatedAt: toFiniteNumber(value.updatedAt, Date.now()),
  };
};

export const buildLocalKlineDatasetId = (exchange: string, symbol: string, timeframe: string): string => {
  const normalizedExchange = normalizeExchange(exchange);
  const normalizedSymbol = normalizeSymbol(symbol);
  const normalizedTimeframe = normalizeTimeframe(timeframe);
  return `${normalizedExchange}|${normalizedSymbol}|${normalizedTimeframe}`;
};

export async function saveLocalKlineDataset(input: SaveLocalKlineDatasetInput): Promise<void> {
  const bars = normalizeBars(Array.isArray(input.bars) ? input.bars : []);
  if (bars.length <= 0) {
    throw new Error('本地缓存写入失败：bars 为空');
  }

  const exchange = normalizeExchange(input.exchange);
  const symbol = normalizeSymbol(input.symbol);
  const timeframe = normalizeTimeframe(input.timeframe);
  const id = buildLocalKlineDatasetId(exchange, symbol, timeframe);
  const startTime = Number.isFinite(input.startTime) ? input.startTime : bars[0][0];
  const endTime = Number.isFinite(input.endTime) ? input.endTime : bars[bars.length - 1][0];
  const updatedAt = Number.isFinite(input.updatedAt) ? input.updatedAt : Date.now();

  const summary = sanitizeSummary({
    ...input,
    id,
    exchange,
    symbol,
    timeframe,
    startTime,
    endTime,
    count: bars.length,
    updatedAt,
  });

  const barsRecord: LocalKlineBarsRecord = {
    id,
    bars,
    updatedAt,
  };

  const db = await openDatabase();
  const tx = db.transaction([META_STORE, BARS_STORE], 'readwrite');
  tx.objectStore(META_STORE).put(summary);
  tx.objectStore(BARS_STORE).put(barsRecord);
  await transactionDone(tx);
}

export async function getLocalKlineDataset(id: string): Promise<LocalKlineDataset | null> {
  const normalizedId = String(id || '').trim();
  if (!normalizedId) {
    return null;
  }

  const db = await openDatabase();
  const tx = db.transaction([META_STORE, BARS_STORE], 'readonly');
  const summaryRequest = tx.objectStore(META_STORE).get(normalizedId);
  const barsRequest = tx.objectStore(BARS_STORE).get(normalizedId);
  const [summaryRaw, barsRaw] = await Promise.all([
    requestToPromise(summaryRequest),
    requestToPromise(barsRequest),
  ]);

  if (!summaryRaw) {
    return null;
  }

  const summary = sanitizeSummary(summaryRaw as Partial<LocalKlineDatasetSummary>);
  const barsRecord = (barsRaw as Partial<LocalKlineBarsRecord> | undefined) ?? undefined;
  const bars = normalizeBars(Array.isArray(barsRecord?.bars) ? barsRecord.bars : []);
  return {
    ...summary,
    bars,
  };
}

export async function listLocalKlineDatasetSummaries(): Promise<LocalKlineDatasetSummary[]> {
  const db = await openDatabase();
  const tx = db.transaction(META_STORE, 'readonly');
  const request = tx.objectStore(META_STORE).getAll();
  const raw = await requestToPromise(request);
  return (raw as Partial<LocalKlineDatasetSummary>[])
    .map((item) => sanitizeSummary(item))
    .sort((a, b) => {
      if (a.exchange !== b.exchange) {
        return a.exchange.localeCompare(b.exchange);
      }
      if (a.symbol !== b.symbol) {
        return a.symbol.localeCompare(b.symbol);
      }
      if (a.timeframe !== b.timeframe) {
        return a.timeframe.localeCompare(b.timeframe);
      }
      return b.updatedAt - a.updatedAt;
    });
}

export async function deleteLocalKlineDataset(id: string): Promise<void> {
  const normalizedId = String(id || '').trim();
  if (!normalizedId) {
    return;
  }

  const db = await openDatabase();
  const tx = db.transaction([META_STORE, BARS_STORE], 'readwrite');
  tx.objectStore(META_STORE).delete(normalizedId);
  tx.objectStore(BARS_STORE).delete(normalizedId);
  await transactionDone(tx);
}

export async function clearLocalKlineDatasets(): Promise<void> {
  const db = await openDatabase();
  const tx = db.transaction([META_STORE, BARS_STORE], 'readwrite');
  tx.objectStore(META_STORE).clear();
  tx.objectStore(BARS_STORE).clear();
  await transactionDone(tx);
}

export async function queryLocalKlineBars(
  exchange: string,
  symbol: string,
  timeframe: string,
  count: number,
): Promise<KlineTuple[]> {
  const id = buildLocalKlineDatasetId(exchange, symbol, timeframe);
  const db = await openDatabase();
  const tx = db.transaction(BARS_STORE, 'readonly');
  const request = tx.objectStore(BARS_STORE).get(id);
  const raw = await requestToPromise(request);
  if (!raw) {
    return [];
  }

  const barsRecord = raw as Partial<LocalKlineBarsRecord>;
  const bars = normalizeBars(Array.isArray(barsRecord.bars) ? barsRecord.bars : []);
  if (count <= 0 || bars.length <= count) {
    return bars;
  }
  return bars.slice(bars.length - count);
}

/**
 * 查询某个时间点之前的本地 K 线数据（不包含 beforeTimestamp）。
 */
export async function queryLocalKlineBarsBefore(
  exchange: string,
  symbol: string,
  timeframe: string,
  beforeTimestamp: number,
  count: number,
): Promise<QueryLocalKlineBarsBeforeResult> {
  const id = buildLocalKlineDatasetId(exchange, symbol, timeframe);
  const db = await openDatabase();
  const tx = db.transaction(BARS_STORE, 'readonly');
  const request = tx.objectStore(BARS_STORE).get(id);
  const raw = await requestToPromise(request);
  if (!raw) {
    return { bars: [], hasMore: false };
  }

  const barsRecord = raw as Partial<LocalKlineBarsRecord>;
  const bars = normalizeBars(Array.isArray(barsRecord.bars) ? barsRecord.bars : []);
  const targetTimestamp = toFiniteNumber(beforeTimestamp, Number.NaN);
  if (!Number.isFinite(targetTimestamp) || bars.length <= 0) {
    return { bars: [], hasMore: false };
  }

  // 二分定位到第一个 >= targetTimestamp 的位置，前一段即为可加载历史区间。
  let left = 0;
  let right = bars.length;
  while (left < right) {
    const mid = Math.floor((left + right) / 2);
    if (bars[mid][0] < targetTimestamp) {
      left = mid + 1;
    } else {
      right = mid;
    }
  }

  const endIndex = left;
  if (endIndex <= 0) {
    return { bars: [], hasMore: false };
  }

  if (count <= 0) {
    return { bars: bars.slice(0, endIndex), hasMore: false };
  }

  const safeCount = Math.max(1, Math.floor(count));
  const startIndex = Math.max(0, endIndex - safeCount);
  return {
    bars: bars.slice(startIndex, endIndex),
    hasMore: startIndex > 0,
  };
}
