import React, { useEffect, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { getWsClient } from '../network';
import './LogConsole.css';

export type LogLevel = 'INFO' | 'WARNING' | 'ERROR' | 'CRITICAL';

export interface LogEntry {
  level: LogLevel;
  message: string;
  timestamp: number;
  category?: string;
}

const LogConsole: React.FC = () => {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [autoScroll, setAutoScroll] = useState(true);
  const logContainerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const ws = getWsClient();
    if (!ws.isConnected()) {
      return;
    }
    
    // 监听所有消息，筛选日志相关
    const unsubscribe = ws.onAny((message) => {
      // 检查是否是日志消息
      if (message.type === 'admin.log' || message.type === 'log' || message.type === 'server.log') {
        const data = message.data as { level?: string; message?: string; category?: string; timestamp?: number };
        if (data && data.message) {
          // 从消息中提取级别，支持多种格式
          let level: LogLevel = 'INFO';
          const levelStr = (data.level || '').toUpperCase();
          
          if (levelStr.includes('WARNING') || levelStr.includes('WARN')) {
            level = 'WARNING';
          } else if (levelStr.includes('ERROR') || levelStr.includes('ERR')) {
            level = 'ERROR';
          } else if (levelStr.includes('CRITICAL') || levelStr.includes('CRIT') || levelStr.includes('FATAL')) {
            level = 'CRITICAL';
          }
          
          // 只显示WARNING、ERROR、CRITICAL级别的日志
          if (['WARNING', 'ERROR', 'CRITICAL'].includes(level)) {
            const entry: LogEntry = {
              level,
              message: data.message,
              timestamp: data.timestamp || Date.now(),
              category: data.category,
            };
            setLogs((prev) => {
              const newLogs = [...prev, entry];
              // 限制最多保留1000条日志
              return newLogs.slice(-1000);
            });
          }
        }
      }
      
      // 也检查错误响应中的日志信息
      if (message.type === 'error' && message.code && message.code >= 500) {
        const entry: LogEntry = {
          level: 'ERROR',
          message: message.msg || '服务器错误',
          timestamp: Date.now(),
          category: 'System',
        };
        setLogs((prev) => {
          const newLogs = [...prev, entry];
          return newLogs.slice(-1000);
        });
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  useEffect(() => {
    if (autoScroll && logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    }
  }, [logs, autoScroll]);

  const clearLogs = () => {
    setLogs([]);
  };

  const getLevelClass = (level: LogLevel) => {
    switch (level) {
      case 'WARNING':
        return 'log-warning';
      case 'ERROR':
        return 'log-error';
      case 'CRITICAL':
        return 'log-critical';
      default:
        return '';
    }
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3 }}
      className="log-console"
    >
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.1 }}
        className="log-console-header"
      >
        <h3>日志控制台</h3>
        <div className="log-console-controls">
          <label>
            <input
              type="checkbox"
              checked={autoScroll}
              onChange={(e) => setAutoScroll(e.target.checked)}
            />
            自动滚动
          </label>
          <motion.button
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={clearLogs}
          >
            清空
          </motion.button>
        </div>
      </motion.div>
      <div className="log-console-content" ref={logContainerRef}>
        {logs.length === 0 ? (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="log-empty"
          >
            暂无日志
          </motion.div>
        ) : (
          <AnimatePresence>
            {logs.map((log, index) => (
              <motion.div
                key={`${log.timestamp}-${index}`}
                initial={{ opacity: 0, x: -20, height: 0 }}
                animate={{ opacity: 1, x: 0, height: 'auto' }}
                exit={{ opacity: 0, x: 20, height: 0 }}
                transition={{
                  duration: 0.3,
                  ease: [0.4, 0, 0.2, 1],
                }}
                className={`log-entry ${getLevelClass(log.level)}`}
              >
                <span className="log-time">
                  {new Date(log.timestamp).toLocaleTimeString()}
                </span>
                <span className="log-level">[{log.level}]</span>
                {log.category && <span className="log-category">[{log.category}]</span>}
                <span className="log-message">{log.message}</span>
              </motion.div>
            ))}
          </AnimatePresence>
        )}
      </div>
    </motion.div>
  );
};

export default LogConsole;
