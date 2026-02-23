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
