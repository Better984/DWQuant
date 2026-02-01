import { WsClient } from "./wsClient";
import { getToken } from "./tokenStore";

export type WsConnectionStatus = "disconnected" | "connecting" | "connected" | "error";
export type WsStatusListener = (status: WsConnectionStatus) => void;

let client: WsClient | null = null;
let status: WsConnectionStatus = "disconnected";
let connectPromise: Promise<void> | null = null;
const listeners = new Set<WsStatusListener>();

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
      system: "admin", // 管理员系统标识
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

export async function ensureWsConnected(): Promise<void> {
  const token = getToken();
  if (!token) {
    setStatus("disconnected");
    throw new Error("Missing token");
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
  connectPromise = ws
    .connect()
    .then(() => {
      setStatus("connected");
    })
    .catch((error) => {
      setStatus("error");
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
