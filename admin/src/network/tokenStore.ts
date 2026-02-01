import { notifyAuthExpired } from "./authEvents";

const FALLBACK_TOKEN_KEY = "dwq_auth_token";
const TOKEN_TTL_MS = 30 * 24 * 60 * 60 * 1000;
const JWT_EXP_SKEW_MS = 30 * 1000;

type StoredToken = {
  token: string;
  expiresAt: number;
};

let inMemoryToken: string | null = null;
let inMemoryExpiresAt: number | null = null;

export function getTokenKey(): string {
  return import.meta.env.VITE_AUTH_TOKEN_KEY ?? FALLBACK_TOKEN_KEY;
}

export function getToken(): string | null {
  const key = getTokenKey();
  try {
    const stored = localStorage.getItem(key);
    if (stored) {
      const parsed = parseStoredToken(stored);
      if (parsed) {
        if (isExpired(parsed.expiresAt)) {
          clearToken();
          notifyAuthExpired();
          return null;
        }
        inMemoryToken = parsed.token;
        inMemoryExpiresAt = parsed.expiresAt;
        return parsed.token;
      }
      inMemoryToken = stored;
      inMemoryExpiresAt = resolveTokenExpiry(stored) ?? Date.now() + TOKEN_TTL_MS;
      persistToken(inMemoryToken, inMemoryExpiresAt);
      return stored;
    }
  } catch {
    // Ignore storage failures.
  }

  if (inMemoryToken && inMemoryExpiresAt && isExpired(inMemoryExpiresAt)) {
    clearToken();
    notifyAuthExpired();
    return null;
  }

  return inMemoryToken;
}

export function setToken(token: string | null): void {
  const key = getTokenKey();
  inMemoryToken = token;
  inMemoryExpiresAt = token ? resolveTokenExpiry(token) ?? Date.now() + TOKEN_TTL_MS : null;
  try {
    if (token) {
      const payload: StoredToken = {
        token,
        expiresAt: inMemoryExpiresAt ?? Date.now() + TOKEN_TTL_MS,
      };
      localStorage.setItem(key, JSON.stringify(payload));
    } else {
      localStorage.removeItem(key);
    }
  } catch {
    // Ignore storage failures.
  }
}

export function clearToken(): void {
  setToken(null);
}

function parseStoredToken(raw: string): StoredToken | null {
  try {
    const parsed = JSON.parse(raw) as Partial<StoredToken>;
    if (typeof parsed?.token === "string" && typeof parsed?.expiresAt === "number") {
      return { token: parsed.token, expiresAt: parsed.expiresAt };
    }
  } catch {
    return null;
  }
  return null;
}

function isExpired(expiresAt: number): boolean {
  return Date.now() >= expiresAt;
}

function persistToken(token: string, expiresAt: number): void {
  const key = getTokenKey();
  try {
    localStorage.setItem(key, JSON.stringify({ token, expiresAt }));
  } catch {
    // Ignore storage failures.
  }
}

function resolveTokenExpiry(token: string): number | null {
  const expSeconds = parseJwtExp(token);
  if (!expSeconds) {
    return null;
  }
  return expSeconds * 1000 - JWT_EXP_SKEW_MS;
}

function parseJwtExp(token: string): number | null {
  const parts = token.split(".");
  if (parts.length < 2) {
    return null;
  }

  try {
    const payload = JSON.parse(atob(toBase64(parts[1]))) as { exp?: number };
    return typeof payload.exp === "number" ? payload.exp : null;
  } catch {
    return null;
  }
}

function toBase64(value: string): string {
  let base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padding = base64.length % 4;
  if (padding > 0) {
    base64 += "=".repeat(4 - padding);
  }
  return base64;
}
