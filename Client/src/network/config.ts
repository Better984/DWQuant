const FALLBACK_API_BASE_URL = "http://localhost:9635";
const FALLBACK_WS_PATH = "/ws";

export type NetworkConfig = {
  apiBaseUrl: string;
  wsBaseUrl: string;
  wsPath: string;
  system: string;
};

export function getNetworkConfig(): NetworkConfig {
  const apiBaseUrl =
    import.meta.env.VITE_API_BASE_URL ?? FALLBACK_API_BASE_URL;
  const wsBaseUrl =
    import.meta.env.VITE_WS_BASE_URL ?? toWebSocketBaseUrl(apiBaseUrl);
  const wsPath = import.meta.env.VITE_WS_PATH ?? FALLBACK_WS_PATH;
  const system = import.meta.env.VITE_WS_SYSTEM ?? "web";

  return {
    apiBaseUrl: trimTrailingSlash(apiBaseUrl),
    wsBaseUrl: trimTrailingSlash(wsBaseUrl),
    wsPath: wsPath.startsWith("/") ? wsPath : `/${wsPath}`,
    system,
  };
}

export function buildWsUrl(
  baseUrl: string,
  path: string,
  system: string,
  token?: string | null,
): string {
  const url = new URL(path, baseUrl);
  url.searchParams.set("system", system);
  if (token) {
    url.searchParams.set("access_token", token);
  }
  return url.toString();
}

function toWebSocketBaseUrl(baseUrl: string): string {
  if (baseUrl.startsWith("ws://") || baseUrl.startsWith("wss://")) {
    return baseUrl;
  }

  if (baseUrl.startsWith("https://")) {
    return `wss://${baseUrl.slice("https://".length)}`;
  }

  if (baseUrl.startsWith("http://")) {
    return `ws://${baseUrl.slice("http://".length)}`;
  }

  return baseUrl;
}

function trimTrailingSlash(value: string): string {
  return value.endsWith("/") ? value.slice(0, -1) : value;
}
