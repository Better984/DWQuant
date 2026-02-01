const AUTH_EXPIRED_EVENT = "auth-expired";

export function notifyAuthExpired(): void {
  window.dispatchEvent(new CustomEvent(AUTH_EXPIRED_EVENT));
}

export function onAuthExpired(handler: () => void): () => void {
  const listener = () => handler();
  window.addEventListener(AUTH_EXPIRED_EVENT, listener);
  return () => {
    window.removeEventListener(AUTH_EXPIRED_EVENT, listener);
  };
}
