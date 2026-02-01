import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import Notification, { type NotificationSize, type NotificationState, type NotificationVariant } from './Notification';
import './NotificationToast.css';

export interface NotificationToastOptions {
  state: NotificationState;
  message: string;
  size?: NotificationSize;
  variant?: NotificationVariant;
  durationMs?: number;
}

export interface NotificationToastContextValue {
  notify: (options: NotificationToastOptions) => void;
  success: (message: string, options?: Omit<NotificationToastOptions, 'state' | 'message'>) => void;
  error: (message: string, options?: Omit<NotificationToastOptions, 'state' | 'message'>) => void;
}

interface ActiveNotification extends NotificationToastOptions {
  id: number;
  durationMs: number;
}

const DEFAULT_DURATION_MS = 2400;

const NotificationToastContext = createContext<NotificationToastContextValue | null>(null);

export const NotificationProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [current, setCurrent] = useState<ActiveNotification | null>(null);
  const timerRef = useRef<number | null>(null);
  const idRef = useRef(0);

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const show = useCallback(
    (options: NotificationToastOptions) => {
      clearTimer();
      idRef.current += 1;
      const durationMs = options.durationMs ?? DEFAULT_DURATION_MS;
      const next: ActiveNotification = {
        ...options,
        durationMs,
        id: idRef.current,
      };
      setCurrent(next);
      const activeId = next.id;
      timerRef.current = window.setTimeout(() => {
        setCurrent((prev) => (prev && prev.id === activeId ? null : prev));
      }, durationMs);
    },
    [clearTimer],
  );

  const handleAnimationEnd = useCallback(
    (id: number) => {
      clearTimer();
      setCurrent((prev) => (prev && prev.id === id ? null : prev));
    },
    [clearTimer],
  );

  useEffect(() => () => clearTimer(), [clearTimer]);

  const contextValue = useMemo<NotificationToastContextValue>(
    () => ({
      notify: show,
      success: (message, options) => show({ state: 'success', message, ...options }),
      error: (message, options) => show({ state: 'failure', message, ...options }),
    }),
    [show],
  );

  const portalTarget = typeof document === 'undefined' ? null : document.body;

  return (
    <NotificationToastContext.Provider value={contextValue}>
      {children}
      {portalTarget && current
        ? createPortal(
            <div
              key={current.id}
              className="snowui-notification-toast"
              style={{ animationDuration: `${current.durationMs}ms` }}
              role={current.state === 'failure' ? 'alert' : 'status'}
              aria-live={current.state === 'failure' ? 'assertive' : 'polite'}
              onAnimationEnd={() => handleAnimationEnd(current.id)}
            >
              <Notification
                state={current.state}
                size={current.size ?? 'large'}
                variant={current.variant ?? 'default'}
                message={current.message}
              />
            </div>,
            portalTarget,
          )
        : null}
    </NotificationToastContext.Provider>
  );
};

export const useNotification = (): NotificationToastContextValue => {
  const context = useContext(NotificationToastContext);
  if (!context) {
    throw new Error('useNotification must be used within NotificationProvider');
  }
  return context;
};
