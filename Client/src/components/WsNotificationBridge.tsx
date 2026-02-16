import React, { useEffect } from 'react';
import { useNotification } from './ui/index.ts';

type WsPopupDetail =
  | { kind: 'reconnect_attempt'; attempt: number; maxAttempts: number }
  | { kind: 'reconnect_success' }
  | { kind: 'reconnect_exhausted'; attempt: number; maxAttempts: number }
  | { kind: 'connect_failed'; reason: string };

const WsNotificationBridge: React.FC = () => {
  const { success, error } = useNotification();

  useEffect(() => {
    const handleWsPopup = (event: Event) => {
      const custom = event as CustomEvent<WsPopupDetail>;
      const detail = custom.detail;
      if (!detail) return;

      switch (detail.kind) {
        case 'reconnect_attempt': {
          const { attempt, maxAttempts } = detail;
          error(`WebSocket 连接中... 第 ${attempt}/${maxAttempts} 次重试`, { durationMs: 3000 });
          break;
        }
        case 'reconnect_success': {
          success('WebSocket 重连成功', { durationMs: 2000 });
          break;
        }
        case 'reconnect_exhausted': {
          const { maxAttempts } = detail;
          error(`WebSocket 重连失败（已重试 ${maxAttempts} 次），请刷新页面后重试`, { durationMs: 5000 });
          break;
        }
        case 'connect_failed': {
          error(`WebSocket 连接失败：${detail.reason}`, { durationMs: 5000 });
          break;
        }
        default:
          break;
      }
    };

    const handleOffline = () => {
      error('网络已断开，请检查网络连接', { durationMs: 4000 });
    };

    window.addEventListener('ws-popup', handleWsPopup as EventListener);
    window.addEventListener('offline', handleOffline);

    return () => {
      window.removeEventListener('ws-popup', handleWsPopup as EventListener);
      window.removeEventListener('offline', handleOffline);
    };
  }, [success, error]);

  return null;
};

export default WsNotificationBridge;

