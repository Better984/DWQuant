export type ProtocolRequest<T = unknown> = {
  type: string;
  reqId: string;
  ts: number;
  data?: T | null;
};

export type ProtocolEnvelope<T = unknown> = {
  type: string;
  reqId?: string | null;
  ts: number;
  code: number;
  msg?: string | null;
  data?: T | null;
  traceId?: string | null;
};

export type WsEnvelope<T = unknown> = ProtocolEnvelope<T>;
