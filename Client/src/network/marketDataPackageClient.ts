import { HttpClient } from './httpClient';
import { getToken } from './tokenStore';

export type OfflinePackageDataset = {
  id: string;
  exchange: string;
  symbol: string;
  timeframe: string;
  retentionDays: number;
  startTime: number;
  endTime: number;
  count: number;
  rawBytes: number;
  compressedBytes: number;
  sha256: string;
  objectKey: string;
  url: string;
};

export type OfflinePackageManifest = {
  version: string;
  generatedAtUtc: string;
  updateIntervalMinutes: number;
  retentionDaysByTimeframe: Record<string, number>;
  datasets: OfflinePackageDataset[];
  manifestUrl?: string | null;
};

export type OfflinePackageManifestResponse = {
  enabled: boolean;
  updateIntervalMinutes: number;
  retentionDaysByTimeframe: Record<string, number>;
  latestManifest?: OfflinePackageManifest | null;
};

const defaultClient = new HttpClient({ tokenProvider: getToken });

/**
 * 获取离线K线包清单信息。
 */
export async function fetchOfflinePackageManifest(
  signal?: AbortSignal,
  client: HttpClient = defaultClient,
): Promise<OfflinePackageManifestResponse> {
  return client.postProtocol<OfflinePackageManifestResponse>(
    '/api/marketdata/offline-package/manifest',
    'marketdata.offline-package.manifest',
    {},
    { signal },
  );
}
