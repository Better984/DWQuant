import React, { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { HttpClient } from '../network/httpClient';
import { getToken, getWsClient, getWsStatus, onWsStatusChange } from '../network';
import { StaggerContainer, StaggerItem } from '../components/animations';
import { useNotification } from '../components/ui';
import './NetworkStatus.css';

interface OnlineUser {
  userId: string;
  system: string;
  connectionId: string;
  connectedAt: string;
  remoteIp?: string;
}

interface ConnectionStats {
  totalConnections: number;
  totalUsers: number;
  connectionsBySystem: Record<string, number>;
}

const NetworkStatus: React.FC = () => {
  const [stats, setStats] = useState<ConnectionStats | null>(null);
  const [onlineUsers, setOnlineUsers] = useState<OnlineUser[]>([]);
  const [wsStatus, setWsStatus] = useState(getWsStatus());
  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { error: showError } = useNotification();

  useEffect(() => {
    loadConnectionStats();
    loadOnlineUsers();
    
    const ws = getWsClient();
    
    // 监听WebSocket实时推送
    const unsubscribeStats = ws.on('admin.connection.stats', (message) => {
      const data = message.data as ConnectionStats;
      if (data) {
        setStats(data);
      }
    });

    const unsubscribeUsers = ws.on('admin.connection.users', (message) => {
      const data = message.data as { users: OnlineUser[] };
      if (data && data.users) {
        setOnlineUsers(data.users);
      }
    });

    // 定期刷新（作为备用，确保数据同步）
    const interval = setInterval(() => {
      loadConnectionStats();
      loadOnlineUsers();
    }, 30000); // 改为30秒，因为现在有实时推送

    return () => {
      unsubscribeStats();
      unsubscribeUsers();
      clearInterval(interval);
    };
  }, []);

  // 监听WebSocket连接状态变化
  useEffect(() => {
    const unsubscribe = onWsStatusChange((newStatus) => {
      setWsStatus(newStatus);
    });
    return unsubscribe;
  }, []);

  const loadConnectionStats = async () => {
    try {
      const response = await client.postProtocol<ConnectionStats>(
        '/api/admin/connection/stats',
        'admin.connection.stats',
        {}
      );
      setStats(response);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : '加载连接统计失败';
      console.error('加载连接统计失败:', err);
      // 只在首次加载失败时显示错误，后续静默失败（避免频繁提示）
      if (!stats) {
        showError(errorMessage);
      }
    }
  };

  const loadOnlineUsers = async () => {
    try {
      const response = await client.postProtocol<{ users: OnlineUser[] }>(
        '/api/admin/connection/users',
        'admin.connection.users',
        {}
      );
      setOnlineUsers(response.users || []);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : '加载在线用户失败';
      console.error('加载在线用户失败:', err);
      // 只在首次加载失败时显示错误，后续静默失败（避免频繁提示）
      if (onlineUsers.length === 0) {
        showError(errorMessage);
      }
    }
  };

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.3 }}
      className="network-status"
    >
      <motion.div
        initial={{ opacity: 0, y: -20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.1 }}
        className="network-status-header"
      >
        <h2>网络详情</h2>
        <motion.div
          initial={{ opacity: 0, x: 20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.15 }}
          className="ws-status-indicator"
        >
          <motion.span
            animate={{
              scale: wsStatus === 'connected' ? [1, 1.2, 1] : 1,
            }}
            transition={{
              duration: 2,
              repeat: wsStatus === 'connected' ? Infinity : 0,
            }}
            className={`status-dot status-${wsStatus}`}
          />
          <span>WebSocket: {wsStatus === 'connected' ? '已连接' : wsStatus === 'connecting' ? '连接中' : '未连接'}</span>
        </motion.div>
      </motion.div>

      <StaggerContainer className="network-stats">
        <StaggerItem>
          <motion.div
            whileHover={{ scale: 1.02, y: -4 }}
            className="stat-card"
          >
            <div className="stat-label">WebSocket连接数</div>
            <motion.div
              key={stats?.totalConnections ?? 0}
              initial={{ scale: 1.2 }}
              animate={{ scale: 1 }}
              transition={{ duration: 0.3 }}
              className="stat-value"
            >
              {stats?.totalConnections ?? 0}
            </motion.div>
          </motion.div>
        </StaggerItem>
        <StaggerItem>
          <motion.div
            whileHover={{ scale: 1.02, y: -4 }}
            className="stat-card"
          >
            <div className="stat-label">在线用户数</div>
            <motion.div
              key={stats?.totalUsers ?? 0}
              initial={{ scale: 1.2 }}
              animate={{ scale: 1 }}
              transition={{ duration: 0.3 }}
              className="stat-value"
            >
              {stats?.totalUsers ?? 0}
            </motion.div>
          </motion.div>
        </StaggerItem>
      </StaggerContainer>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.2 }}
        className="online-users-section"
      >
        <h3>在线用户</h3>
        <div className="online-users-table">
          <table>
            <thead>
              <tr>
                <th>用户ID</th>
                <th>系统</th>
                <th>连接ID</th>
                <th>连接时间</th>
                <th>IP地址</th>
              </tr>
            </thead>
            <tbody>
              {onlineUsers.length === 0 ? (
                <tr>
                  <td colSpan={5} className="empty-cell">暂无在线用户</td>
                </tr>
              ) : (
                <StaggerContainer>
                  {onlineUsers.map((user, index) => (
                    <StaggerItem key={`${user.userId}-${user.connectionId}-${index}`}>
                      <motion.tr
                        initial={{ opacity: 0, x: -20 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: index * 0.03 }}
                        whileHover={{ backgroundColor: 'var(--color-surface-hover)' }}
                      >
                        <td>{user.userId}</td>
                        <td>{user.system}</td>
                        <td className="connection-id">{user.connectionId}</td>
                        <td>{new Date(user.connectedAt).toLocaleString()}</td>
                        <td>{user.remoteIp || '-'}</td>
                      </motion.tr>
                    </StaggerItem>
                  ))}
                </StaggerContainer>
              )}
            </tbody>
          </table>
        </div>
      </motion.div>

    </motion.div>
  );
};

export default NetworkStatus;
