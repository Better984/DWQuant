import React, { useEffect, useMemo, useState } from 'react';
import { Dialog, useNotification } from '../ui/index.ts';
import {
  clearLocalKlineDatasets,
  deleteLocalKlineDataset,
  listLocalKlineDatasetSummaries,
  type LocalKlineDatasetSummary,
} from '../../lib/klineOfflineCacheDb';
import {
  fetchOfflinePackageManifest,
  type OfflinePackageDataset,
  type OfflinePackageManifestResponse,
} from '../../network/marketDataPackageClient';
import {
  syncOfflineKlinePackage,
  type OfflinePackageSyncFilter,
} from '../../lib/klineOfflinePackageManager';
import './KlineOfflineCacheDialog.css';

interface KlineOfflineCacheDialogProps {
  open: boolean;
  onClose: () => void;
}

const TIMEFRAME_ORDER: Record<string, number> = {
  '1m': 1,
  '3m': 2,
  '5m': 3,
  '15m': 4,
  '30m': 5,
  '1h': 6,
  '2h': 7,
  '4h': 8,
  '6h': 9,
  '8h': 10,
  '12h': 11,
  '1d': 12,
  '3d': 13,
  '1w': 14,
  '1mo': 15,
};

const formatTime = (timestamp: number | string) => {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return '-';
  }
  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
};

const formatBytes = (bytes: number) => {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return '0 B';
  }
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let index = 0;
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024;
    index += 1;
  }
  return `${value.toFixed(index === 0 ? 0 : 2)} ${units[index]}`;
};

const KlineOfflineCacheDialog: React.FC<KlineOfflineCacheDialogProps> = ({ open, onClose }) => {
  const { success, error } = useNotification();
  const [loading, setLoading] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [syncText, setSyncText] = useState('');
  const [serverInfo, setServerInfo] = useState<OfflinePackageManifestResponse | null>(null);
  const [localSummaries, setLocalSummaries] = useState<LocalKlineDatasetSummary[]>([]);
  const [selectedExchanges, setSelectedExchanges] = useState<string[]>([]);
  const [selectedSymbols, setSelectedSymbols] = useState<string[]>([]);
  const [selectedTimeframes, setSelectedTimeframes] = useState<string[]>([]);

  const refreshData = async () => {
    setLoading(true);
    try {
      const [manifest, localData] = await Promise.all([
        fetchOfflinePackageManifest(),
        listLocalKlineDatasetSummaries(),
      ]);
      setServerInfo(manifest);
      setLocalSummaries(localData);
    } catch (ex) {
      error(ex instanceof Error ? ex.message : '加载离线缓存信息失败');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!open) {
      return;
    }
    refreshData().catch(() => {
      // 错误已在 refreshData 内处理。
    });
  }, [open]);

  const retentionEntries = useMemo(() => {
    const source = serverInfo?.retentionDaysByTimeframe || {};
    return Object.entries(source).sort((a, b) => {
      const ao = TIMEFRAME_ORDER[a[0]] ?? 999;
      const bo = TIMEFRAME_ORDER[b[0]] ?? 999;
      return ao - bo;
    });
  }, [serverInfo?.retentionDaysByTimeframe]);

  const manifestDatasets = useMemo<OfflinePackageDataset[]>(() => {
    const datasets = serverInfo?.latestManifest?.datasets;
    return Array.isArray(datasets) ? datasets : [];
  }, [serverInfo?.latestManifest?.datasets]);

  const availableExchanges = useMemo(() => {
    return Array.from(new Set(manifestDatasets.map((item) => item.exchange))).sort((a, b) => a.localeCompare(b));
  }, [manifestDatasets]);

  const availableSymbols = useMemo(() => {
    return Array.from(new Set(manifestDatasets.map((item) => item.symbol))).sort((a, b) => a.localeCompare(b));
  }, [manifestDatasets]);

  const availableTimeframes = useMemo(() => {
    return Array.from(new Set(manifestDatasets.map((item) => item.timeframe))).sort((a, b) => {
      const ao = TIMEFRAME_ORDER[a] ?? 999;
      const bo = TIMEFRAME_ORDER[b] ?? 999;
      if (ao !== bo) {
        return ao - bo;
      }
      return a.localeCompare(b);
    });
  }, [manifestDatasets]);

  useEffect(() => {
    setSelectedExchanges((prev) => prev.filter((item) => availableExchanges.includes(item)));
    setSelectedSymbols((prev) => prev.filter((item) => availableSymbols.includes(item)));
    setSelectedTimeframes((prev) => prev.filter((item) => availableTimeframes.includes(item)));
  }, [availableExchanges, availableSymbols, availableTimeframes]);

  const syncFilter = useMemo<OfflinePackageSyncFilter>(() => {
    const filter: OfflinePackageSyncFilter = {};
    if (selectedExchanges.length > 0) {
      filter.exchanges = selectedExchanges;
    }
    if (selectedSymbols.length > 0) {
      filter.symbols = selectedSymbols;
    }
    if (selectedTimeframes.length > 0) {
      filter.timeframes = selectedTimeframes;
    }
    return filter;
  }, [selectedExchanges, selectedSymbols, selectedTimeframes]);

  const selectedDatasetCount = useMemo(() => {
    return manifestDatasets.filter((dataset) => {
      if (syncFilter.exchanges && !syncFilter.exchanges.includes(dataset.exchange)) {
        return false;
      }
      if (syncFilter.symbols && !syncFilter.symbols.includes(dataset.symbol)) {
        return false;
      }
      if (syncFilter.timeframes && !syncFilter.timeframes.includes(dataset.timeframe)) {
        return false;
      }
      return true;
    }).length;
  }, [manifestDatasets, syncFilter]);

  const localStats = useMemo(() => {
    let bars = 0;
    let compressedBytes = 0;
    for (const item of localSummaries) {
      bars += item.count || 0;
      compressedBytes += item.compressedBytes || 0;
    }
    return {
      datasetCount: localSummaries.length,
      bars,
      compressedBytes,
    };
  }, [localSummaries]);

  const toggleSelectValue = (
    value: string,
    setSelected: React.Dispatch<React.SetStateAction<string[]>>,
  ) => {
    setSelected((prev) => {
      if (prev.includes(value)) {
        return prev.filter((item) => item !== value);
      }
      return [...prev, value];
    });
  };

  const clearSyncFilter = () => {
    setSelectedExchanges([]);
    setSelectedSymbols([]);
    setSelectedTimeframes([]);
  };

  const handleSync = async () => {
    const manifest = serverInfo?.latestManifest;
    if (!manifest || !Array.isArray(manifest.datasets) || manifest.datasets.length <= 0) {
      error('当前没有可下载的离线包');
      return;
    }
    if (selectedDatasetCount <= 0) {
      error('当前筛选条件下没有可下载的数据分片');
      return;
    }

    setSyncing(true);
    setSyncText('准备下载离线包...');
    try {
      const result = await syncOfflineKlinePackage(
        manifest,
        (progress) => {
          setSyncText(`下载中 ${progress.current}/${progress.total}: ${progress.symbol} ${progress.timeframe}`);
        },
        { filter: syncFilter },
      );

      if (result.failedCount > 0) {
        error(`同步完成，成功 ${result.successCount}，失败 ${result.failedCount}`);
      } else {
        success(`同步完成，共导入 ${result.successCount} 个数据分片`);
      }

      if (result.errors.length > 0) {
        console.warn('离线包同步错误', result.errors);
      }

      await refreshData();
    } catch (ex) {
      error(ex instanceof Error ? ex.message : '同步离线包失败');
    } finally {
      setSyncing(false);
      setSyncText('');
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteLocalKlineDataset(id);
      success('已删除本地缓存');
      await refreshData();
    } catch (ex) {
      error(ex instanceof Error ? ex.message : '删除失败');
    }
  };

  const handleClearAll = async () => {
    if (!window.confirm('确认清空全部本地K线缓存吗？')) {
      return;
    }
    try {
      await clearLocalKlineDatasets();
      success('已清空本地缓存');
      await refreshData();
    } catch (ex) {
      error(ex instanceof Error ? ex.message : '清空失败');
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="本地K线离线缓存"
      cancelText="关闭"
      className="kline-offline-cache-dialog"
    >
      <div className="kline-offline-cache-content ui-scrollable">
        <div className="kline-offline-cache-actions">
          <button
            type="button"
            className="kline-offline-cache-btn"
            disabled={loading || syncing}
            onClick={() => void refreshData()}
          >
            刷新
          </button>
          <button
            type="button"
            className="kline-offline-cache-btn is-primary"
            disabled={loading || syncing || selectedDatasetCount <= 0}
            onClick={() => void handleSync()}
          >
            下载并更新本地缓存
          </button>
          <button
            type="button"
            className="kline-offline-cache-btn is-danger"
            disabled={loading || syncing || localSummaries.length === 0}
            onClick={() => void handleClearAll()}
          >
            清空本地缓存
          </button>
        </div>

        {syncing && <div className="kline-offline-cache-sync-tip">{syncText}</div>}

        <div className="kline-offline-cache-section">
          <div className="kline-offline-cache-section-title">下载筛选（可选）</div>
          <div className="kline-offline-cache-filter-meta">
            当前将下载 {selectedDatasetCount} 个分片
            {manifestDatasets.length > 0 ? ` / 总计 ${manifestDatasets.length} 个` : ''}
          </div>
          <div className="kline-offline-cache-filter-actions">
            <button
              type="button"
              className="kline-offline-cache-btn"
              disabled={loading || syncing}
              onClick={clearSyncFilter}
            >
              清空筛选（下载全部）
            </button>
          </div>
          <div className="kline-offline-cache-filter-block">
            <div className="kline-offline-cache-filter-label">交易所</div>
            <div className="kline-offline-cache-filter-chip-list">
              {availableExchanges.length <= 0 ? (
                <span className="kline-offline-cache-empty">无可选项</span>
              ) : (
                availableExchanges.map((exchange) => {
                  const active = selectedExchanges.includes(exchange);
                  return (
                    <button
                      key={exchange}
                      type="button"
                      className={`kline-offline-cache-filter-chip ${active ? 'is-active' : ''}`}
                      disabled={loading || syncing}
                      onClick={() => toggleSelectValue(exchange, setSelectedExchanges)}
                    >
                      {exchange}
                    </button>
                  );
                })
              )}
            </div>
          </div>
          <div className="kline-offline-cache-filter-block">
            <div className="kline-offline-cache-filter-label">币对</div>
            <div className="kline-offline-cache-filter-chip-list">
              {availableSymbols.length <= 0 ? (
                <span className="kline-offline-cache-empty">无可选项</span>
              ) : (
                availableSymbols.map((symbol) => {
                  const active = selectedSymbols.includes(symbol);
                  return (
                    <button
                      key={symbol}
                      type="button"
                      className={`kline-offline-cache-filter-chip ${active ? 'is-active' : ''}`}
                      disabled={loading || syncing}
                      onClick={() => toggleSelectValue(symbol, setSelectedSymbols)}
                    >
                      {symbol}
                    </button>
                  );
                })
              )}
            </div>
          </div>
          <div className="kline-offline-cache-filter-block">
            <div className="kline-offline-cache-filter-label">周期</div>
            <div className="kline-offline-cache-filter-chip-list">
              {availableTimeframes.length <= 0 ? (
                <span className="kline-offline-cache-empty">无可选项</span>
              ) : (
                availableTimeframes.map((timeframe) => {
                  const active = selectedTimeframes.includes(timeframe);
                  return (
                    <button
                      key={timeframe}
                      type="button"
                      className={`kline-offline-cache-filter-chip ${active ? 'is-active' : ''}`}
                      disabled={loading || syncing}
                      onClick={() => toggleSelectValue(timeframe, setSelectedTimeframes)}
                    >
                      {timeframe}
                    </button>
                  );
                })
              )}
            </div>
          </div>
        </div>

        <div className="kline-offline-cache-section">
          <div className="kline-offline-cache-section-title">服务端配置</div>
          <div className="kline-offline-cache-kv-grid">
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">离线包任务</div>
              <div className="kline-offline-cache-kv-value">{serverInfo?.enabled ? '已启用' : '未启用'}</div>
            </div>
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">更新间隔</div>
              <div className="kline-offline-cache-kv-value">{serverInfo?.updateIntervalMinutes || 0} 分钟</div>
            </div>
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">最新版本</div>
              <div className="kline-offline-cache-kv-value">{serverInfo?.latestManifest?.version || '-'}</div>
            </div>
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">版本生成时间</div>
              <div className="kline-offline-cache-kv-value">
                {serverInfo?.latestManifest?.generatedAtUtc
                  ? formatTime(serverInfo.latestManifest.generatedAtUtc)
                  : '-'}
              </div>
            </div>
          </div>
          <div className="kline-offline-cache-retention">
            <div className="kline-offline-cache-retention-title">周期保留天数</div>
            <div className="kline-offline-cache-retention-list">
              {retentionEntries.length === 0 ? (
                <span className="kline-offline-cache-empty">无配置</span>
              ) : (
                retentionEntries.map(([timeframe, days]) => (
                  <span key={timeframe} className="kline-offline-cache-chip">
                    {timeframe}: {days}天
                  </span>
                ))
              )}
            </div>
          </div>
        </div>

        <div className="kline-offline-cache-section">
          <div className="kline-offline-cache-section-title">本地缓存统计</div>
          <div className="kline-offline-cache-kv-grid">
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">分片数量</div>
              <div className="kline-offline-cache-kv-value">{localStats.datasetCount}</div>
            </div>
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">总K线数量</div>
              <div className="kline-offline-cache-kv-value">{localStats.bars.toLocaleString()}</div>
            </div>
            <div className="kline-offline-cache-kv-item">
              <div className="kline-offline-cache-kv-label">压缩体积</div>
              <div className="kline-offline-cache-kv-value">{formatBytes(localStats.compressedBytes)}</div>
            </div>
          </div>
        </div>

        <div className="kline-offline-cache-section">
          <div className="kline-offline-cache-section-title">本地数据分片</div>
          {loading ? (
            <div className="kline-offline-cache-loading">加载中...</div>
          ) : localSummaries.length <= 0 ? (
            <div className="kline-offline-cache-empty">当前没有本地缓存数据</div>
          ) : (
            <div className="kline-offline-cache-table-wrapper ui-scrollable">
              <table className="kline-offline-cache-table">
                <thead>
                  <tr>
                    <th>交易所</th>
                    <th>币对</th>
                    <th>周期</th>
                    <th>K线数量</th>
                    <th>时间范围</th>
                    <th>版本</th>
                    <th>更新时间</th>
                    <th>操作</th>
                  </tr>
                </thead>
                <tbody>
                  {localSummaries.map((item) => (
                    <tr key={item.id}>
                      <td>{item.exchange}</td>
                      <td>{item.symbol}</td>
                      <td>{item.timeframe}</td>
                      <td>{item.count.toLocaleString()}</td>
                      <td>{`${formatTime(item.startTime)} ~ ${formatTime(item.endTime)}`}</td>
                      <td>{item.sourceVersion}</td>
                      <td>{formatTime(item.updatedAt)}</td>
                      <td>
                        <button
                          type="button"
                          className="kline-offline-cache-row-btn"
                          disabled={syncing}
                          onClick={() => void handleDelete(item.id)}
                        >
                          删除
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </Dialog>
  );
};

export default KlineOfflineCacheDialog;
