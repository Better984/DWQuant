import React, { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import { useNotification } from '../components/ui';
import './HistoricalData.css';

interface CacheSnapshot {
  exchange: string;
  symbol: string;
  timeframe: string;
  startTime: string;
  endTime: string;
  count: number;
}

const HistoricalData: React.FC = () => {
  const [snapshots, setSnapshots] = useState<CacheSnapshot[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState<string | null>(null);
  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { success, error: showError } = useNotification();

  useEffect(() => {
    loadSnapshots();
  }, []);

  const loadSnapshots = async () => {
    setLoading(true);
    try {
      // 调用管理员API获取历史行情缓存快照
      const response = await client.postProtocol<{ snapshots: CacheSnapshot[] }>(
        '/api/admin/marketdata/cache-snapshots',
        'admin.marketdata.cache-snapshots',
        {}
      );
      setSnapshots(response.snapshots || []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载失败');
    } finally {
      setLoading(false);
    }
  };

  const refreshCache = async (params: {
    exchange?: string;
    symbol?: string;
    timeframe?: string;
  }) => {
    const key = `${params.exchange || 'all'}_${params.symbol || 'all'}_${params.timeframe || 'all'}`;
    setRefreshing(key);
    try {
      await client.postProtocol(
        '/api/admin/marketdata/refresh-cache',
        'admin.marketdata.refresh-cache',
        params
      );
      success('刷新成功');
      await loadSnapshots();
    } catch (err) {
      showError(err instanceof Error ? err.message : '刷新失败');
    } finally {
      setRefreshing(null);
    }
  };

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.3 }}
      className="historical-data"
    >
      <motion.div
        initial={{ opacity: 0, y: -20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.1 }}
        className="historical-data-header"
      >
        <h2>历史行情缓存</h2>
        <motion.button
          whileHover={{ scale: 1.05 }}
          whileTap={{ scale: 0.95 }}
          onClick={loadSnapshots}
          disabled={loading}
        >
          {loading ? '加载中...' : '刷新'}
        </motion.button>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: -10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.15 }}
        className="historical-data-actions"
      >
        <motion.button
          whileHover={{ scale: 1.05 }}
          whileTap={{ scale: 0.95 }}
          onClick={() => refreshCache({})}
          disabled={refreshing !== null}
        >
          {refreshing === 'all_all_all' ? '刷新中...' : '刷新所有缓存'}
        </motion.button>
      </motion.div>

      <div className="historical-data-table">
        <table>
          <thead>
            <tr>
              <th>交易所</th>
              <th>币对</th>
              <th>周期</th>
              <th>K线数量</th>
              <th>开始时间</th>
              <th>结束时间</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {snapshots.length === 0 ? (
              <tr>
                <td colSpan={7} className="empty-cell">
                  {loading ? '加载中...' : '暂无数据'}
                </td>
              </tr>
            ) : (
              snapshots.map((snapshot, index) => (
                <tr key={`${snapshot.exchange}-${snapshot.symbol}-${snapshot.timeframe}-${index}`}>
                  <td>{snapshot.exchange}</td>
                  <td>{snapshot.symbol}</td>
                  <td>{snapshot.timeframe}</td>
                  <td>{snapshot.count}</td>
                  <td>{new Date(snapshot.startTime).toLocaleString()}</td>
                  <td>{new Date(snapshot.endTime).toLocaleString()}</td>
                  <td>
                    <div className="action-buttons">
                      <button
                        onClick={() => refreshCache({ exchange: snapshot.exchange })}
                        disabled={refreshing !== null}
                        title="刷新该交易所所有币种"
                      >
                        刷新交易所
                      </button>
                      <button
                        onClick={() =>
                          refreshCache({
                            exchange: snapshot.exchange,
                            symbol: snapshot.symbol,
                          })
                        }
                        disabled={refreshing !== null}
                        title="刷新该币种所有周期"
                      >
                        刷新币种
                      </button>
                      <button
                        onClick={() =>
                          refreshCache({
                            exchange: snapshot.exchange,
                            symbol: snapshot.symbol,
                            timeframe: snapshot.timeframe,
                          })
                        }
                        disabled={refreshing !== null}
                        title="刷新该周期"
                      >
                        刷新周期
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </motion.div>
  );
};

export default HistoricalData;
