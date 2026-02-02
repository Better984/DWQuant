import React, { useEffect, useRef, useState, useMemo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Checkbox, AutoComplete, Select, Badge, Modal } from 'antd';
import { getWsClient, onWsStatusChange, getWsStatus, ensureWsConnected } from '../network';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import './LogConsole.css';

export type LogLevel = 'TRACE' | 'DEBUG' | 'INFO' | 'INFORMATION' | 'WARNING' | 'ERROR' | 'CRITICAL' | 'NONE';

export interface LogEntry {
  level: LogLevel;
  message: string;
  timestamp: number;
  category?: string;
  nodeId?: string;
}

interface ServerNode {
  nodeId: string;
  machineName: string;
  status: string;
}

const LOG_LEVELS: LogLevel[] = ['TRACE', 'DEBUG', 'INFO', 'INFORMATION', 'WARNING', 'ERROR', 'CRITICAL', 'NONE'];
const DEFAULT_SELECTED_LEVELS: LogLevel[] = ['INFO', 'INFORMATION', 'WARNING', 'ERROR', 'CRITICAL'];
const FILTER_HISTORY_KEY = 'log-console-filter-history';
const MAX_HISTORY_COUNT = 20;

const LogConsole: React.FC = () => {
  const [allLogs, setAllLogs] = useState<LogEntry[]>([]);
  const [autoScroll, setAutoScroll] = useState(true);
  const [selectedLevels, setSelectedLevels] = useState<LogLevel[]>(DEFAULT_SELECTED_LEVELS);
  const [textFilter, setTextFilter] = useState<string>('');
  const [filterHistory, setFilterHistory] = useState<string[]>([]);
  const [servers, setServers] = useState<ServerNode[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<string>('all');
  const [wsStatus, setWsStatus] = useState<'disconnected' | 'connecting' | 'connected' | 'error'>('disconnected');
  const [detailVisible, setDetailVisible] = useState(false);
  const [detailLog, setDetailLog] = useState<LogEntry | null>(null);
  const logContainerRef = useRef<HTMLDivElement>(null);
  const client = new HttpClient();
  client.setTokenProvider(getToken);

  // 从localStorage加载历史记录
  useEffect(() => {
    try {
      const saved = localStorage.getItem(FILTER_HISTORY_KEY);
      if (saved) {
        const history = JSON.parse(saved);
        if (Array.isArray(history)) {
          setFilterHistory(history);
        }
      }
    } catch (error) {
      console.error('加载筛选历史记录失败:', error);
    }
  }, []);

  // 监听WebSocket连接状态
  useEffect(() => {
    setWsStatus(getWsStatus());
    const unsubscribe = onWsStatusChange((status) => {
      setWsStatus(status);
    });
    return unsubscribe;
  }, []);

  // 当选择服务器时，自动确保WebSocket连接
  useEffect(() => {
    // 如果选择了具体服务器（不是"全部服务器"）且WebSocket未连接，则自动连接
    if (selectedServerId !== 'all' && wsStatus !== 'connected' && wsStatus !== 'connecting') {
      console.log('[LogConsole] 选择服务器，自动连接WebSocket...');
      ensureWsConnected().catch((error) => {
        console.error('[LogConsole] WebSocket连接失败:', error);
      });
    }
  }, [selectedServerId, wsStatus]);

  // 加载服务器列表
  useEffect(() => {
    const loadServers = async () => {
      try {
        const response = await client.postProtocol<{ servers: ServerNode[] }>(
          '/api/admin/server/list',
          'admin.server.list',
          {}
        );
        const serverList = response.servers || [];
        setServers(serverList);
      } catch (error) {
        console.error('加载服务器列表失败:', error);
      }
    };

    loadServers();
    // 每30秒刷新一次服务器列表
    const interval = setInterval(loadServers, 30000);
    return () => clearInterval(interval);
  }, []);

  // 保存历史记录到localStorage
  const saveToHistory = (text: string) => {
    if (!text.trim()) return;
    
    setFilterHistory((prev) => {
      const newHistory = [text.trim(), ...prev.filter((item) => item !== text.trim())]
        .slice(0, MAX_HISTORY_COUNT);
      try {
        localStorage.setItem(FILTER_HISTORY_KEY, JSON.stringify(newHistory));
      } catch (error) {
        console.error('保存筛选历史记录失败:', error);
      }
      return newHistory;
    });
  };

  // 根据筛选条件过滤日志
  const filteredLogs = useMemo(() => {
    const result = allLogs.filter((log) => {
      // 服务器筛选
      if (selectedServerId !== 'all' && log.nodeId !== selectedServerId) {
        return false;
      }
      
      // 级别筛选
      if (!selectedLevels.includes(log.level)) {
        return false;
      }
      
      // 文字筛选
      if (textFilter.trim()) {
        const searchText = textFilter.toLowerCase();
        const messageMatch = log.message.toLowerCase().includes(searchText);
        const categoryMatch = log.category?.toLowerCase().includes(searchText);
        const levelMatch = log.level.toLowerCase().includes(searchText);
        
        if (!messageMatch && !categoryMatch && !levelMatch) {
          return false;
        }
      }
      
      return true;
    });
    
    // 调试日志：仅在筛选条件变化时输出
    console.log('[LogConsole] 筛选结果:', {
      totalLogs: allLogs.length,
      filteredLogs: result.length,
      selectedLevels,
      selectedServerId,
      textFilter,
      levelDistribution: allLogs.reduce((acc, log) => {
        acc[log.level] = (acc[log.level] || 0) + 1;
        return acc;
      }, {} as Record<LogLevel, number>),
    });
    
    return result;
  }, [allLogs, selectedLevels, textFilter, selectedServerId]);

  useEffect(() => {
    const ws = getWsClient();
    let unsubscribe: (() => void) | null = null;
    
    // 设置消息监听器
    const setupListener = () => {
      if (!ws.isConnected()) {
        console.log('[LogConsole] WebSocket未连接，跳过设置监听器');
        return;
      }
      
      console.log('[LogConsole] 设置WebSocket消息监听器');
      
      // 监听所有消息，筛选日志相关
      unsubscribe = ws.onAny((message) => {
        // 调试：打印所有消息类型
        if (message.type === 'admin.log' || message.type === 'log' || message.type === 'server.log') {
          console.log('[LogConsole] 收到日志消息:', message);
        }
        
        // 检查是否是日志消息
        if (message.type === 'admin.log' || message.type === 'log' || message.type === 'server.log') {
          const data = message.data as { level?: string; message?: string; category?: string; timestamp?: number; nodeId?: string };
          
          if (!data) {
            console.warn('[LogConsole] 日志消息data为空:', message);
            return;
          }
          
          if (!data.message) {
            console.warn('[LogConsole] 日志消息message字段为空:', message);
            return;
          }
          
          // 从消息中提取级别，支持多种格式
          let level: LogLevel = 'INFO';
          const levelStr = (data.level || '').toUpperCase();
          
          // 按优先级从高到低检查（CRITICAL > ERROR > WARNING > INFORMATION > INFO > DEBUG > TRACE > NONE）
          if (levelStr.includes('CRITICAL') || levelStr.includes('CRIT') || levelStr.includes('FATAL')) {
            level = 'CRITICAL';
          } else if (levelStr.includes('ERROR') || levelStr.includes('ERR')) {
            level = 'ERROR';
          } else if (levelStr.includes('WARNING') || levelStr.includes('WARN')) {
            level = 'WARNING';
          } else if (levelStr.includes('INFORMATION')) {
            // 先检查完整的 INFORMATION
            level = 'INFORMATION';
          } else if (levelStr === 'INFO' || levelStr.includes('INFO')) {
            // 然后检查 INFO
            level = 'INFO';
          } else if (levelStr.includes('DEBUG') || levelStr.includes('DBG')) {
            level = 'DEBUG';
          } else if (levelStr.includes('TRACE') || levelStr.includes('TRC')) {
            level = 'TRACE';
          } else if (levelStr.includes('NONE') || levelStr === '') {
            level = 'NONE';
          }
          
          // 保存所有级别的日志（不再在这里过滤）
          const entry: LogEntry = {
            level,
            message: data.message,
            timestamp: data.timestamp || Date.now(),
            category: data.category,
            nodeId: data.nodeId,
          };
          
          console.log('[LogConsole] 添加日志条目:', entry);
          
          setAllLogs((prev) => {
            const newLogs = [...prev, entry];
            // 限制最多保留1000条日志
            return newLogs.slice(-1000);
          });
        }
        
        // 也检查错误响应中的日志信息
        if (message.type === 'error' && message.code && message.code >= 500) {
          const entry: LogEntry = {
            level: 'ERROR',
            message: message.msg || '服务器错误',
            timestamp: Date.now(),
            category: 'System',
          };
          setAllLogs((prev) => {
            const newLogs = [...prev, entry];
            return newLogs.slice(-1000);
          });
        }
      });
    };
    
    // 如果已连接，立即设置监听器
    if (ws.isConnected()) {
      setupListener();
    }
    
    // 监听WebSocket连接状态变化
    const unsubscribeStatus = onWsStatusChange((status) => {
      console.log('[LogConsole] WebSocket状态变化:', status);
      if (status === 'connected' && !unsubscribe) {
        setupListener();
      } else if (status !== 'connected' && unsubscribe) {
        // 断开连接时清理监听器
        unsubscribe();
        unsubscribe = null;
      }
    });
    
    return () => {
      if (unsubscribe) {
        unsubscribe();
      }
      unsubscribeStatus();
    };
  }, []);

  useEffect(() => {
    if (autoScroll && logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    }
  }, [filteredLogs, autoScroll]);

  const clearLogs = () => {
    setAllLogs([]);
  };

  const handleLevelChange = (checkedValues: (string | number | boolean)[]) => {
    // 确保类型转换正确，过滤掉无效值
    const validLevels = checkedValues
      .map((v) => String(v).toUpperCase())
      .filter((v): v is LogLevel => LOG_LEVELS.includes(v as LogLevel)) as LogLevel[];
    
    console.log('[LogConsole] 级别筛选变更:', { checkedValues, validLevels });
    setSelectedLevels(validLevels.length > 0 ? validLevels : DEFAULT_SELECTED_LEVELS);
  };

  const handleTextFilterChange = (value: string) => {
    setTextFilter(value);
  };

  const handleTextFilterSearch = (value: string) => {
    setTextFilter(value);
    if (value.trim()) {
      saveToHistory(value);
    }
  };

  const handleSelectAll = () => {
    if (selectedLevels.length === LOG_LEVELS.length) {
      setSelectedLevels(DEFAULT_SELECTED_LEVELS);
    } else {
      setSelectedLevels([...LOG_LEVELS]);
    }
  };

  const openLogDetail = (log: LogEntry) => {
    // 点击日志弹窗查看完整内容
    setDetailLog(log);
    setDetailVisible(true);
  };

  const closeLogDetail = () => {
    setDetailVisible(false);
  };

  const resolveNodeName = (nodeId?: string) => {
    if (!nodeId) {
      return '';
    }
    const server = servers.find((item) => item.nodeId === nodeId);
    return server?.machineName || nodeId.split('-')[0] || '未知';
  };

  const getLevelClass = (level: LogLevel) => {
    switch (level) {
      case 'TRACE':
        return 'log-trace';
      case 'DEBUG':
        return 'log-debug';
      case 'INFO':
        return 'log-info';
      case 'INFORMATION':
        return 'log-information';
      case 'WARNING':
        return 'log-warning';
      case 'ERROR':
        return 'log-error';
      case 'CRITICAL':
        return 'log-critical';
      case 'NONE':
        return 'log-none';
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
        <div className="log-console-header-top">
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <h3>日志控制台</h3>
            <Badge 
              status={wsStatus === 'connected' ? 'success' : wsStatus === 'connecting' ? 'processing' : 'error'} 
              text={
                wsStatus === 'connected' ? '已连接' : 
                wsStatus === 'connecting' ? '连接中' : 
                wsStatus === 'error' ? '连接错误' : 
                '未连接'
              }
              style={{ fontSize: '12px' }}
            />
          </div>
          <div className="log-console-actions">
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
        </div>
        <div className="log-console-controls">
          <div className="log-server-filter">
            <span className="filter-label">服务器：</span>
            <Select
              value={selectedServerId}
              onChange={(value) => {
                setSelectedServerId(value);
                // 选择服务器时，如果WebSocket未连接，立即尝试连接
                if (value !== 'all' && wsStatus !== 'connected' && wsStatus !== 'connecting') {
                  ensureWsConnected().catch((error) => {
                    console.error('[LogConsole] WebSocket连接失败:', error);
                  });
                }
              }}
              style={{ width: 250 }}
              size="small"
              allowClear={false}
            >
              <Select.Option value="all">全部服务器</Select.Option>
              {servers.map((server) => {
                const isWsConnected = wsStatus === 'connected';
                const isWsConnecting = wsStatus === 'connecting';
                // 服务器心跳状态（来自后端）
                const serverHeartbeatStatus = server.status;
                // 显示状态：如果WebSocket未连接，显示"离线"（因为无法接收日志）
                // 如果WebSocket已连接，显示服务器实际心跳状态
                const displayStatus = isWsConnected ? serverHeartbeatStatus : 'Offline';
                const statusText = displayStatus === 'Online' ? '在线' : displayStatus === 'Warning' ? '警告' : '离线';
                const wsStatusText = isWsConnecting ? '连接中' : isWsConnected ? '已连接' : '未连接';
                return (
                  <Select.Option key={server.nodeId} value={server.nodeId}>
                    {server.machineName} ({statusText}, WS: {wsStatusText})
                  </Select.Option>
                );
              })}
            </Select>
          </div>
          <div className="log-level-filter">
            <span className="filter-label">级别：</span>
            <Checkbox.Group
              value={selectedLevels}
              onChange={handleLevelChange}
              options={LOG_LEVELS.map((level) => ({
                label: level,
                value: level,
              }))}
            />
            <motion.button
              className="select-all-btn"
              whileHover={{ scale: 1.05 }}
              whileTap={{ scale: 0.95 }}
              onClick={handleSelectAll}
            >
              {selectedLevels.length === LOG_LEVELS.length ? '恢复默认' : '全选'}
            </motion.button>
          </div>
          <div className="log-text-filter">
            <span className="filter-label">文字：</span>
            <AutoComplete
              value={textFilter}
              onChange={handleTextFilterChange}
              onSearch={handleTextFilterChange}
              onSelect={handleTextFilterSearch}
              onPressEnter={(e) => {
                const value = (e.target as HTMLInputElement).value;
                handleTextFilterSearch(value);
              }}
              placeholder="关键词筛选..."
              options={filterHistory
                .filter((item) => 
                  !textFilter || item.toLowerCase().includes(textFilter.toLowerCase())
                )
                .map((item) => ({ value: item, label: item }))}
              allowClear
              style={{ width: 200 }}
              size="small"
            />
          </div>
        </div>
      </motion.div>
      <div className="log-console-content" ref={logContainerRef}>
        {filteredLogs.length === 0 ? (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            className="log-empty"
          >
            {allLogs.length === 0 ? (
              <div>
                <div>暂无日志</div>
                <div style={{ marginTop: '8px', fontSize: '12px', color: '#999' }}>
                  {wsStatus === 'connected' 
                    ? 'WebSocket已连接，等待日志消息...' 
                    : wsStatus === 'connecting' 
                    ? 'WebSocket连接中...' 
                    : 'WebSocket未连接，请检查连接状态'}
                </div>
                {allLogs.length === 0 && wsStatus === 'connected' && (
                  <div style={{ marginTop: '8px', fontSize: '12px', color: '#999' }}>
                    提示：后端当前仅推送 INFORMATION 及以上级别日志，TRACE/DEBUG 不会推送
                  </div>
                )}
              </div>
            ) : '没有匹配的日志'}
          </motion.div>
        ) : (
          <AnimatePresence>
            {filteredLogs.map((log, index) => (
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
                role="button"
                tabIndex={0}
                onClick={() => openLogDetail(log)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openLogDetail(log);
                  }
                }}
              >
                <div className="log-entry-meta">
                  <span className="log-time">
                    {new Date(log.timestamp).toLocaleTimeString()}
                  </span>
                  <span className="log-level">[{log.level}]</span>
                  {log.nodeId && (
                    <span className="log-node-id" title={log.nodeId}>
                      [{resolveNodeName(log.nodeId)}]
                    </span>
                  )}
                  {log.category && (
                    <span className="log-category" title={log.category}>
                      [{log.category}]
                    </span>
                  )}
                </div>
                <div className="log-entry-message">{log.message}</div>
              </motion.div>
            ))}
          </AnimatePresence>
        )}
      </div>
      <Modal
        open={detailVisible}
        title="日志详情"
        onCancel={closeLogDetail}
        footer={null}
        width={760}
        className="log-detail-modal"
      >
        {detailLog && (
          <div className="log-detail-body">
            <div className="log-detail-grid">
              <div className="log-detail-label">时间</div>
              <div className="log-detail-value">
                {new Date(detailLog.timestamp).toLocaleString()}
              </div>
              <div className="log-detail-label">级别</div>
              <div className="log-detail-value">{detailLog.level}</div>
              <div className="log-detail-label">节点</div>
              <div className="log-detail-value">
                {detailLog.nodeId ? `${resolveNodeName(detailLog.nodeId)} (${detailLog.nodeId})` : '-'}
              </div>
              <div className="log-detail-label">分类</div>
              <div className="log-detail-value">{detailLog.category || '-'}</div>
            </div>
            <div className="log-detail-message">{detailLog.message}</div>
          </div>
        )}
      </Modal>
    </motion.div>
  );
};

export default LogConsole;
