export type ApiResponse<T> = {
  success: boolean;
  message?: string | null;
  data?: T | null;
  timestamp?: string | number | null;
};

export type ErrorResponse = {
  code: string;
  message: string;
  traceId?: string | null;
};

export type WsError = {
  code: string;
  message: string;
};

export type WsEnvelope<T = unknown> = {
  type: string;
  reqId?: string | null;
  ts: number;
  payload?: T | null;
  err?: WsError | null;
};
