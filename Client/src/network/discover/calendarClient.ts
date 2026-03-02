import { HttpClient } from "../httpClient";
import { getToken } from "../tokenStore";

const client = new HttpClient({ tokenProvider: getToken });

export type DiscoverCalendarItem = {
  id: number;
  calendarName: string;
  countryCode: string;
  countryName: string;
  publishTimestamp: number;
  importanceLevel: number;
  hasExactPublishTime: boolean;
  dataEffect?: string | null;
  forecastValue?: string | null;
  previousValue?: string | null;
  revisedPreviousValue?: string | null;
  publishedValue?: string | null;
  createdAt: number;
  updatedAt: number;
};

export type DiscoverCalendarPullResponse = {
  mode: "latest" | "incremental" | "history" | "range" | string;
  latestServerId: number;
  hasMore: boolean;
  items: DiscoverCalendarItem[];
  total: number;
};

export type DiscoverCalendarPullRequest = {
  latestId?: number;
  beforeId?: number;
  startTime?: number;
  endTime?: number;
  limit?: number;
};

export async function pullDiscoverCentralBankCalendars(
  request: DiscoverCalendarPullRequest,
  options?: { signal?: AbortSignal }
): Promise<DiscoverCalendarPullResponse> {
  return client.postProtocol<DiscoverCalendarPullResponse, DiscoverCalendarPullRequest>(
    "/api/discover/calendar/central-bank/pull",
    "discover.calendar.central-bank.pull",
    normalizeRequest(request),
    { signal: options?.signal }
  );
}

export async function pullDiscoverFinancialEventsCalendars(
  request: DiscoverCalendarPullRequest,
  options?: { signal?: AbortSignal }
): Promise<DiscoverCalendarPullResponse> {
  return client.postProtocol<DiscoverCalendarPullResponse, DiscoverCalendarPullRequest>(
    "/api/discover/calendar/financial-events/pull",
    "discover.calendar.financial-events.pull",
    normalizeRequest(request),
    { signal: options?.signal }
  );
}

export async function pullDiscoverEconomicDataCalendars(
  request: DiscoverCalendarPullRequest,
  options?: { signal?: AbortSignal }
): Promise<DiscoverCalendarPullResponse> {
  return client.postProtocol<DiscoverCalendarPullResponse, DiscoverCalendarPullRequest>(
    "/api/discover/calendar/economic-data/pull",
    "discover.calendar.economic-data.pull",
    normalizeRequest(request),
    { signal: options?.signal }
  );
}

function normalizeRequest(request: DiscoverCalendarPullRequest): DiscoverCalendarPullRequest {
  const next: DiscoverCalendarPullRequest = {};

  if (typeof request.latestId === "number" && Number.isFinite(request.latestId) && request.latestId > 0) {
    next.latestId = Math.floor(request.latestId);
  }
  if (typeof request.beforeId === "number" && Number.isFinite(request.beforeId) && request.beforeId > 0) {
    next.beforeId = Math.floor(request.beforeId);
  }
  if (typeof request.startTime === "number" && Number.isFinite(request.startTime) && request.startTime > 0) {
    next.startTime = Math.floor(request.startTime);
  }
  if (typeof request.endTime === "number" && Number.isFinite(request.endTime) && request.endTime > 0) {
    next.endTime = Math.floor(request.endTime);
  }
  if (typeof request.limit === "number" && Number.isFinite(request.limit) && request.limit > 0) {
    next.limit = Math.floor(request.limit);
  }

  return next;
}
