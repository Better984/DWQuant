import { HttpClient } from "../httpClient";
import { getToken } from "../tokenStore";

const client = new HttpClient({ tokenProvider: getToken });

export type DiscoverFeedItem = {
  id: number;
  title: string;
  summary: string;
  contentHtml: string;
  source: string;
  sourceLogo?: string | null;
  pictureUrl?: string | null;
  releaseTime: number;
  createdAt: number;
};

export type DiscoverPullResponse = {
  mode: "latest" | "incremental" | "history" | string;
  latestServerId: number;
  hasMore: boolean;
  items: DiscoverFeedItem[];
  total: number;
};

export type DiscoverPullRequest = {
  latestId?: number;
  beforeId?: number;
  limit?: number;
};

export async function pullDiscoverArticles(
  request: DiscoverPullRequest,
  options?: { signal?: AbortSignal }
): Promise<DiscoverPullResponse> {
  return client.postProtocol<DiscoverPullResponse, DiscoverPullRequest>(
    "/api/discover/article/pull",
    "discover.article.pull",
    normalizeRequest(request),
    { signal: options?.signal }
  );
}

export async function pullDiscoverNewsflashes(
  request: DiscoverPullRequest,
  options?: { signal?: AbortSignal }
): Promise<DiscoverPullResponse> {
  return client.postProtocol<DiscoverPullResponse, DiscoverPullRequest>(
    "/api/discover/newsflash/pull",
    "discover.newsflash.pull",
    normalizeRequest(request),
    { signal: options?.signal }
  );
}

function normalizeRequest(request: DiscoverPullRequest): DiscoverPullRequest {
  const next: DiscoverPullRequest = {};
  if (typeof request.latestId === "number" && Number.isFinite(request.latestId) && request.latestId > 0) {
    next.latestId = Math.floor(request.latestId);
  }
  if (typeof request.beforeId === "number" && Number.isFinite(request.beforeId) && request.beforeId > 0) {
    next.beforeId = Math.floor(request.beforeId);
  }
  if (typeof request.limit === "number" && Number.isFinite(request.limit) && request.limit > 0) {
    next.limit = Math.floor(request.limit);
  }
  return next;
}
