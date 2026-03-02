import { HttpClient } from "./httpClient";
import { getToken } from "./tokenStore";

const client = new HttpClient({ tokenProvider: getToken });

export type IndicatorLatestItem = {
  code: string;
  provider: string;
  displayName: string;
  shape: string;
  unit?: string | null;
  description?: string | null;
  scopeKey: string;
  sourceTs: number;
  fetchedAt: number;
  expireAt: number;
  stale: boolean;
  origin: "cache" | "database" | "provider" | string;
  payload: unknown;
};

export type IndicatorRealtimeChannelItem = {
  channel: string;
  source: string;
  receivedAt: number;
  expireAt: number;
  stale: boolean;
  payload: unknown;
};

export async function getIndicatorLatest(
  code: string,
  scope?: Record<string, string>,
  options?: {
    allowStale?: boolean;
    forceRefresh?: boolean;
    signal?: AbortSignal;
  },
): Promise<IndicatorLatestItem> {
  return client.postProtocol<IndicatorLatestItem, {
    code: string;
    scope?: Record<string, string>;
    allowStale?: boolean;
    forceRefresh?: boolean;
  }>(
    "/api/indicator/latest/get",
    "indicator.latest.get",
    {
      code,
      scope,
      allowStale: options?.allowStale ?? true,
      forceRefresh: options?.forceRefresh ?? false,
    },
    { signal: options?.signal },
  );
}

export async function getIndicatorRealtimeChannel(
  channel: string,
  options?: {
    allowStale?: boolean;
    signal?: AbortSignal;
  },
): Promise<IndicatorRealtimeChannelItem> {
  return client.postProtocol<IndicatorRealtimeChannelItem, {
    channel: string;
    allowStale?: boolean;
  }>(
    "/api/indicator/realtime/channel/get",
    "indicator.realtime.channel.get",
    {
      channel,
      allowStale: options?.allowStale ?? true,
    },
    { signal: options?.signal },
  );
}
