import type { KLineData } from 'klinecharts';
import {
  fetchOfflinePackageManifest,
  type OfflinePackageDataset,
  type OfflinePackageManifest,
} from '../network/marketDataPackageClient';
import {
  buildLocalKlineDatasetId,
  queryLocalKlineBars,
  queryLocalKlineBarsBefore,
  saveLocalKlineDataset,
  type KlineTuple,
} from './klineOfflineCacheDb';

type OfflineDatasetPayload = {
  exchange?: string;
  symbol?: string;
  timeframe?: string;
  generatedAtUtc?: string;
  retentionDays?: number;
  startTime?: number;
  endTime?: number;
  count?: number;
  bars?: unknown[];
};

export type OfflinePackageSyncProgress = {
  current: number;
  total: number;
  datasetId: string;
  symbol: string;
  timeframe: string;
};

export type OfflinePackageSyncResult = {
  successCount: number;
  failedCount: number;
  errors: string[];
};

export type OfflinePackageSyncFilter = {
  exchanges?: string[];
  symbols?: string[];
  timeframes?: string[];
};

export type OfflinePackageSyncOptions = {
  filter?: OfflinePackageSyncFilter;
  signal?: AbortSignal;
};

export type LocalKlineLoadSource = 'local' | 'cloud' | 'none';

export type LocalKlineLoadResult = {
  bars: KLineData[];
  source: LocalKlineLoadSource;
};

export type LocalKlineLoadMoreResult = {
  bars: KLineData[];
  hasMore: boolean;
};

const toNumber = (value: unknown): number | null => {
  const num = Number(value);
  return Number.isFinite(num) ? num : null;
};

const toTuple = (value: unknown): KlineTuple | null => {
  if (!Array.isArray(value) || value.length < 6) {
    return null;
  }
  const timestamp = toNumber(value[0]);
  const open = toNumber(value[1]);
  const high = toNumber(value[2]);
  const low = toNumber(value[3]);
  const close = toNumber(value[4]);
  const volume = toNumber(value[5]) ?? 0;
  if (
    timestamp === null ||
    open === null ||
    high === null ||
    low === null ||
    close === null
  ) {
    return null;
  }
  return [timestamp, open, high, low, close, volume];
};

const decompressGzipToText = async (blob: Blob): Promise<string> => {
  if (typeof DecompressionStream !== 'undefined') {
    const stream = blob.stream().pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).text();
  }
  throw new Error('当前浏览器不支持 GZIP 解压，请升级到最新版 Chrome/Edge');
};

const decodeDatasetPayload = async (response: Response): Promise<OfflineDatasetPayload> => {
  const contentType = (response.headers.get('content-type') || '').toLowerCase();
  const url = response.url || '';
  if (url.endsWith('.gz') || contentType.includes('application/gzip')) {
    const blob = await response.blob();
    const text = await decompressGzipToText(blob);
    return JSON.parse(text) as OfflineDatasetPayload;
  }

  return (await response.json()) as OfflineDatasetPayload;
};

const normalizeExchangeKey = (value: string) => (value || '').trim().toLowerCase();

const normalizeSymbolKey = (value: string) =>
  (value || '').replaceAll('_', '/').replaceAll('-', '/').trim().toUpperCase();

const normalizeTimeframeKey = (value: string) => (value || '').trim().toLowerCase();

const toNormalizedSet = (
  values: string[] | undefined,
  normalizer: (value: string) => string,
): Set<string> | null => {
  if (!Array.isArray(values) || values.length <= 0) {
    return null;
  }
  const normalized = values
    .map((item) => normalizer(item))
    .filter((item) => item.length > 0);
  return normalized.length > 0 ? new Set(normalized) : null;
};

const datasetMatchesFilter = (
  dataset: OfflinePackageDataset,
  filter?: OfflinePackageSyncFilter,
): boolean => {
  if (!filter) {
    return true;
  }
  const exchangeSet = toNormalizedSet(filter.exchanges, normalizeExchangeKey);
  if (exchangeSet && !exchangeSet.has(normalizeExchangeKey(dataset.exchange))) {
    return false;
  }
  const symbolSet = toNormalizedSet(filter.symbols, normalizeSymbolKey);
  if (symbolSet && !symbolSet.has(normalizeSymbolKey(dataset.symbol))) {
    return false;
  }
  const timeframeSet = toNormalizedSet(filter.timeframes, normalizeTimeframeKey);
  if (timeframeSet && !timeframeSet.has(normalizeTimeframeKey(dataset.timeframe))) {
    return false;
  }
  return true;
};

const toKlineDataList = (bars: KlineTuple[]): KLineData[] =>
  bars.map((item) => ({
    timestamp: item[0],
    open: item[1],
    high: item[2],
    low: item[3],
    close: item[4],
    volume: item[5],
  }));

const findDatasetFromManifest = (
  manifest: OfflinePackageManifest,
  exchange: string,
  symbol: string,
  timeframe: string,
): OfflinePackageDataset | null => {
  const datasets = Array.isArray(manifest.datasets) ? manifest.datasets : [];
  if (datasets.length <= 0) {
    return null;
  }

  const targetId = buildLocalKlineDatasetId(exchange, symbol, timeframe);
  const targetExchange = normalizeExchangeKey(exchange);
  const targetSymbol = normalizeSymbolKey(symbol);
  const targetTimeframe = normalizeTimeframeKey(timeframe);

  const byId = datasets.find((item) => {
    const itemId = buildLocalKlineDatasetId(item.exchange, item.symbol, item.timeframe);
    return item.id === targetId || itemId === targetId;
  });
  if (byId) {
    return byId;
  }

  return datasets.find((item) => {
    return normalizeExchangeKey(item.exchange) === targetExchange
      && normalizeSymbolKey(item.symbol) === targetSymbol
      && normalizeTimeframeKey(item.timeframe) === targetTimeframe;
  }) || null;
};

const downloadAndSaveSingleDataset = async (
  manifest: OfflinePackageManifest,
  dataset: OfflinePackageDataset,
  signal?: AbortSignal,
): Promise<void> => {
  if (!dataset.url) {
    throw new Error('缺少下载地址');
  }

  const response = await fetch(dataset.url, { method: 'GET', signal, cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`下载失败: HTTP ${response.status}`);
  }

  const payload = await decodeDatasetPayload(response);
  const rawBars = Array.isArray(payload.bars) ? payload.bars : [];
  const bars = rawBars.map(toTuple).filter((item): item is KlineTuple => item !== null);
  if (bars.length <= 0) {
    throw new Error('数据分片无有效K线');
  }

  const startTime = toNumber(payload.startTime) ?? bars[0][0];
  const endTime = toNumber(payload.endTime) ?? bars[bars.length - 1][0];
  const id = buildLocalKlineDatasetId(
    payload.exchange || dataset.exchange,
    payload.symbol || dataset.symbol,
    payload.timeframe || dataset.timeframe,
  );

  await saveLocalKlineDataset({
    id,
    exchange: payload.exchange || dataset.exchange,
    symbol: payload.symbol || dataset.symbol,
    timeframe: payload.timeframe || dataset.timeframe,
    retentionDays: toNumber(payload.retentionDays) ?? dataset.retentionDays ?? 0,
    startTime,
    endTime,
    count: bars.length,
    sourceVersion: manifest.version,
    sourceGeneratedAtUtc: manifest.generatedAtUtc,
    sourceUrl: dataset.url,
    compressedBytes: dataset.compressedBytes ?? 0,
    rawBytes: dataset.rawBytes ?? 0,
    sha256: dataset.sha256 || '',
    updatedAt: Date.now(),
    bars,
  });
};

/**
 * 下载并保存离线K线包到 IndexedDB。
 */
export async function syncOfflineKlinePackage(
  manifest: OfflinePackageManifest,
  onProgress?: (progress: OfflinePackageSyncProgress) => void,
  options?: OfflinePackageSyncOptions,
): Promise<OfflinePackageSyncResult> {
  const signal = options?.signal;
  const allDatasets = Array.isArray(manifest.datasets) ? manifest.datasets : [];
  const datasets = allDatasets.filter((dataset) => datasetMatchesFilter(dataset, options?.filter));
  const errors: string[] = [];
  let successCount = 0;
  let failedCount = 0;

  for (let index = 0; index < datasets.length; index += 1) {
    const dataset = datasets[index];
    const datasetId = dataset.id || `${dataset.exchange}|${dataset.symbol}|${dataset.timeframe}`;
    onProgress?.({
      current: index + 1,
      total: datasets.length,
      datasetId,
      symbol: dataset.symbol,
      timeframe: dataset.timeframe,
    });

    try {
      await downloadAndSaveSingleDataset(manifest, dataset, signal);

      successCount += 1;
    } catch (error) {
      failedCount += 1;
      const message = error instanceof Error ? error.message : '未知错误';
      errors.push(`${datasetId}: ${message}`);
    }
  }

  return {
    successCount,
    failedCount,
    errors,
  };
}

/**
 * 为 K 线图读取本地缓存数据。
 */
export async function loadLocalKlineBars(
  exchange: string,
  symbol: string,
  timeframe: string,
  count: number,
): Promise<KLineData[]> {
  const bars = await queryLocalKlineBars(exchange, symbol, timeframe, count);
  return toKlineDataList(bars);
}

/**
 * 查询某个时间点之前的本地缓存 K 线，用于图表左滑懒加载历史数据。
 */
export async function loadLocalKlineBarsBefore(
  exchange: string,
  symbol: string,
  timeframe: string,
  beforeTimestamp: number,
  count: number,
): Promise<LocalKlineLoadMoreResult> {
  const result = await queryLocalKlineBarsBefore(
    exchange,
    symbol,
    timeframe,
    beforeTimestamp,
    count,
  );
  return {
    bars: toKlineDataList(result.bars),
    hasMore: result.hasMore,
  };
}

/**
 * 优先读取本地缓存；本地无数据时尝试云端离线包单分片下载并落库。
 */
export async function loadLocalKlineBarsWithCloudFallback(
  exchange: string,
  symbol: string,
  timeframe: string,
  count: number,
  signal?: AbortSignal,
): Promise<LocalKlineLoadResult> {
  const localBars = await queryLocalKlineBars(exchange, symbol, timeframe, count);
  if (localBars.length > 0) {
    return {
      bars: toKlineDataList(localBars),
      source: 'local',
    };
  }

  try {
    const manifestResponse = await fetchOfflinePackageManifest(signal);
    const manifest = manifestResponse.latestManifest;
    if (!manifest || !Array.isArray(manifest.datasets) || manifest.datasets.length <= 0) {
      return { bars: [], source: 'none' };
    }

    const targetDataset = findDatasetFromManifest(manifest, exchange, symbol, timeframe);
    if (!targetDataset) {
      return { bars: [], source: 'none' };
    }

    await downloadAndSaveSingleDataset(manifest, targetDataset, signal);
    const syncedBars = await queryLocalKlineBars(exchange, symbol, timeframe, count);
    if (syncedBars.length > 0) {
      return {
        bars: toKlineDataList(syncedBars),
        source: 'cloud',
      };
    }
  } catch (error) {
    // 云端离线分片拉取失败时，交给上层继续后备链路处理。
    console.warn('云端离线K线分片拉取失败', error);
  }

  return { bars: [], source: 'none' };
}
