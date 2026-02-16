import { WsClient, dispatchWsPopup } from "./wsClient";
import { getToken } from "./tokenStore";
import { HttpClient, HttpError } from "./httpClient";
import { getNetworkConfig } from "./config";

export type WsConnectionStatus = "disconnected" | "connecting" | "connected" | "error";
export type WsStatusListener = (status: WsConnectionStatus) => void;

let client: WsClient | null = null;
let status: WsConnectionStatus = "disconnected";
let connectPromise: Promise<void> | null = null;
const listeners = new Set<WsStatusListener>();
const httpClient = new HttpClient({ tokenProvider: getToken });

function setStatus(next: WsConnectionStatus): void {
  if (status === next) {
    return;
  }
  status = next;
  for (const listener of listeners) {
    listener(status);
  }
}

function getClient(): WsClient {
  if (!client) {
    client = new WsClient({
      tokenProvider: getToken,
      onOpen: () => setStatus("connected"),
      onClose: () => setStatus("disconnected"),
      onError: () => setStatus("error"),
    });
  }
  return client;
}

export function getWsStatus(): WsConnectionStatus {
  return status;
}

export function onWsStatusChange(listener: WsStatusListener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

async function checkWsHandshake(): Promise<void> {
  const token = getToken();
  if (!token) {
    throw new Error("尚未登录或登录已过期");
  }

  // 这里不需要手动拼接 access_token，HttpClient 会自动加 Authorization 头，
  // 后端 WsHandshakeController 会先读 query，再读 Authorization: Bearer xxx。
  const config = getNetworkConfig();

   // 调试输出：查看发给后端的关键参数（不打印完整 token，避免日志过长）
   const tokenPreview =
     token.length > 24 ? `${token.slice(0, 12)}...${token.slice(-8)}` : token;
   // eslint-disable-next-line no-console
   console.log("[WS Handshake] 请求参数", {
     url: "/api/ws/handshake/check",
     system: config.system,
     tokenPreview,
   });

  try {
    await httpClient.postProtocol<unknown>(
      "/api/ws/handshake/check",
      "ws.handshake.check",
      {
        // 预留给后续使用的扩展字段，目前仅用于打通协议管线
        system: config.system,
      } as unknown
    );
  } catch (error) {
    if (error instanceof HttpError) {
      throw new Error(error.message || "WebSocket 预校验失败");
    }
    if (error instanceof Error) {
      throw error;
    }
    throw new Error("WebSocket 预校验失败");
  }
}

export async function ensureWsConnected(): Promise<void> {
  const token = getToken();
  if (!token) {
    setStatus("disconnected");
    throw new Error("尚未登录或登录已过期");
  }

  const ws = getClient();
  if (ws.isConnected()) {
    setStatus("connected");
    return;
  }

  if (connectPromise) {
    return connectPromise;
  }

  setStatus("connecting");
  connectPromise = (async () => {
    // 先通过 HTTP 预校验拿到清晰的失败原因
    await checkWsHandshake();
    await ws.connect();
    setStatus("connected");
  })()
    .catch((error) => {
      setStatus("error");
      const message =
        error instanceof Error ? error.message : "WebSocket 连接失败";
      dispatchWsPopup({ kind: "connect_failed", reason: message });
      throw error;
    })
    .finally(() => {
      connectPromise = null;
    });

  return connectPromise;
}

export function disconnectWs(): void {
  if (client) {
    client.disconnect();
  }
  setStatus("disconnected");
}

export function getWsClient(): WsClient {
  return getClient();
}
