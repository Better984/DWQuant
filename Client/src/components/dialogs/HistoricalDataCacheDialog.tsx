import React, { useEffect, useState, useMemo } from 'react';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient } from '../../network/httpClient';
import { getToken } from '../../network/index.ts';
import './HistoricalDataCacheDialog.css';

interface CacheSnapshot {
  exchange: string;
  symbol: string;
  timeframe: string;
  startTime: string;
  endTime: string;
  count: number;
}

interface HistoricalDataCacheDialogProps {
  open: boolean;
  onClose: () => void;
}

const HistoricalDataCacheDialog: React.FC<HistoricalDataCacheDialogProps> = ({ open, onClose }) => {
  const { error: showError } = useNotification();
  const [snapshots, setSnapshots] = useState<CacheSnapshot[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedExchanges, setSelectedExchanges] = useState<Set<string>>(new Set());
  const [selectedSymbols, setSelectedSymbols] = useState<Set<string>>(new Set());
  const [showExchangeFilter, setShowExchangeFilter] = useState(false);
  const [showSymbolFilter, setShowSymbolFilter] = useState(false);
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  useEffect(() => {
    if (!open) {
      return;
    }
    loadSnapshots();
    // 重置筛选条件
    setSelectedExchanges(new Set());
    setSelectedSymbols(new Set());
    setShowExchangeFilter(false);
    setShowSymbolFilter(false);
  }, [open]);

  // 点击外部关闭下拉菜单
  useEffect(() => {
    if (!open) {
      return;
    }
    const handleClickOutside = (event: MouseEvent) => {
      const target = event.target as HTMLElement;
      if (
        !target.closest('.historical-cache-filter-dropdown') &&
        (showExchangeFilter || showSymbolFilter)
      ) {
        setShowExchangeFilter(false);
        setShowSymbolFilter(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [open, showExchangeFilter, showSymbolFilter]);

  const loadSnapshots = async () => {
    setLoading(true);
    try {
      const response = await client.postProtocol<{ snapshots: CacheSnapshot[] }>(
        '/api/MarketData/cache-snapshots',
        'marketdata.cache.snapshots',
        {}
      );
      setSnapshots(response.snapshots || []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载失败');
    } finally {
      setLoading(false);
    }
  };

  // 获取所有可用的交易所和币对（用于筛选选项）
  const allExchanges = useMemo(() => {
    const exchanges = new Set<string>();
    snapshots.forEach((snapshot) => {
      exchanges.add(snapshot.exchange);
    });
    return Array.from(exchanges).sort();
  }, [snapshots]);

  const allSymbols = useMemo(() => {
    const symbols = new Set<string>();
    snapshots.forEach((snapshot) => {
      symbols.add(snapshot.symbol);
    });
    return Array.from(symbols).sort();
  }, [snapshots]);

  // 根据筛选条件过滤数据
  const filteredSnapshots = useMemo(() => {
    return snapshots.filter((snapshot) => {
      if (selectedExchanges.size > 0 && !selectedExchanges.has(snapshot.exchange)) {
        return false;
      }
      if (selectedSymbols.size > 0 && !selectedSymbols.has(snapshot.symbol)) {
        return false;
      }
      return true;
    });
  }, [snapshots, selectedExchanges, selectedSymbols]);

  // 汇总统计信息（基于筛选后的数据）
  const summary = useMemo(() => {
    const exchanges = new Set<string>();
    const timeframes = new Set<string>();
    const symbols = new Set<string>();
    let totalCount = 0;
    let earliestStart: Date | null = null;
    let latestEnd: Date | null = null;

    filteredSnapshots.forEach((snapshot) => {
      exchanges.add(snapshot.exchange);
      timeframes.add(snapshot.timeframe);
      symbols.add(snapshot.symbol);
      totalCount += snapshot.count;

      const start = new Date(snapshot.startTime);
      const end = new Date(snapshot.endTime);

      if (!earliestStart || start < earliestStart) {
        earliestStart = start;
      }
      if (!latestEnd || end > latestEnd) {
        latestEnd = end;
      }
    });

    return {
      exchanges: Array.from(exchanges).sort(),
      timeframes: Array.from(timeframes).sort(),
      symbols: Array.from(symbols).sort(),
      totalCount,
      earliestStart,
      latestEnd,
      totalSnapshots: filteredSnapshots.length,
    };
  }, [filteredSnapshots]);

  // 按交易所分组（基于筛选后的数据）
  const groupedByExchange = useMemo(() => {
    const groups = new Map<string, CacheSnapshot[]>();
    filteredSnapshots.forEach((snapshot) => {
      if (!groups.has(snapshot.exchange)) {
        groups.set(snapshot.exchange, []);
      }
      groups.get(snapshot.exchange)!.push(snapshot);
    });
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [filteredSnapshots]);

  const handleExchangeToggle = (exchange: string) => {
    setSelectedExchanges((prev) => {
      const next = new Set(prev);
      if (next.has(exchange)) {
        next.delete(exchange);
      } else {
        next.add(exchange);
      }
      return next;
    });
  };

  const handleSymbolToggle = (symbol: string) => {
    setSelectedSymbols((prev) => {
      const next = new Set(prev);
      if (next.has(symbol)) {
        next.delete(symbol);
      } else {
        next.add(symbol);
      }
      return next;
    });
  };

  const handleSelectAllExchanges = () => {
    if (selectedExchanges.size === allExchanges.length) {
      setSelectedExchanges(new Set());
    } else {
      setSelectedExchanges(new Set(allExchanges));
    }
  };

  const handleSelectAllSymbols = () => {
    if (selectedSymbols.size === allSymbols.length) {
      setSelectedSymbols(new Set());
    } else {
      setSelectedSymbols(new Set(allSymbols));
    }
  };

  const handleClearFilters = () => {
    setSelectedExchanges(new Set());
    setSelectedSymbols(new Set());
  };

  const formatDateTime = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      return date.toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      });
    } catch {
      return dateStr;
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="历史行情缓存信息"
      cancelText="关闭"
      className="historical-cache-dialog"
    >
      <div className="historical-cache-content">
        {loading ? (
          <div className="historical-cache-loading">加载中...</div>
        ) : (
          <>
            {/* 筛选区域 */}
            <div className="historical-cache-filters">
              <h3 className="historical-cache-section-title">筛选</h3>
              <div className="historical-cache-filter-row">
                <div className="historical-cache-filter-group">
                  <label className="historical-cache-filter-label">交易所</label>
                  <div className="historical-cache-filter-dropdown">
                    <button
                      type="button"
                      className="historical-cache-filter-button"
                      onClick={() => {
                        setShowExchangeFilter(!showExchangeFilter);
                        setShowSymbolFilter(false);
                      }}
                    >
                      {selectedExchanges.size === 0
                        ? '全部'
                        : selectedExchanges.size === allExchanges.length
                        ? '全部'
                        : `已选 ${selectedExchanges.size} 项`}
                      <svg
                        width="12"
                        height="12"
                        viewBox="0 0 12 12"
                        fill="none"
                        style={{
                          transform: showExchangeFilter ? 'rotate(180deg)' : 'none',
                          transition: 'transform 0.2s ease',
                        }}
                      >
                        <path
                          d="M3 4.5L6 7.5L9 4.5"
                          stroke="currentColor"
                          strokeWidth="1.5"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        />
                      </svg>
                    </button>
                    {showExchangeFilter && (
                      <div className="historical-cache-filter-menu">
                        <div className="historical-cache-filter-menu-header">
                          <button
                            type="button"
                            className="historical-cache-filter-menu-action"
                            onClick={handleSelectAllExchanges}
                          >
                            {selectedExchanges.size === allExchanges.length ? '取消全选' : '全选'}
                          </button>
                          {selectedExchanges.size > 0 && (
                            <button
                              type="button"
                              className="historical-cache-filter-menu-action"
                              onClick={() => setSelectedExchanges(new Set())}
                            >
                              清空
                            </button>
                          )}
                        </div>
                        <div className="historical-cache-filter-menu-items">
                          {allExchanges.map((exchange) => (
                            <label
                              key={exchange}
                              className="historical-cache-filter-menu-item"
                            >
                              <input
                                type="checkbox"
                                checked={selectedExchanges.has(exchange)}
                                onChange={() => handleExchangeToggle(exchange)}
                              />
                              <span>{exchange}</span>
                            </label>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </div>

                <div className="historical-cache-filter-group">
                  <label className="historical-cache-filter-label">币种</label>
                  <div className="historical-cache-filter-dropdown">
                    <button
                      type="button"
                      className="historical-cache-filter-button"
                      onClick={() => {
                        setShowSymbolFilter(!showSymbolFilter);
                        setShowExchangeFilter(false);
                      }}
                    >
                      {selectedSymbols.size === 0
                        ? '全部'
                        : selectedSymbols.size === allSymbols.length
                        ? '全部'
                        : `已选 ${selectedSymbols.size} 项`}
                      <svg
                        width="12"
                        height="12"
                        viewBox="0 0 12 12"
                        fill="none"
                        style={{
                          transform: showSymbolFilter ? 'rotate(180deg)' : 'none',
                          transition: 'transform 0.2s ease',
                        }}
                      >
                        <path
                          d="M3 4.5L6 7.5L9 4.5"
                          stroke="currentColor"
                          strokeWidth="1.5"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        />
                      </svg>
                    </button>
                    {showSymbolFilter && (
                      <div className="historical-cache-filter-menu">
                        <div className="historical-cache-filter-menu-header">
                          <button
                            type="button"
                            className="historical-cache-filter-menu-action"
                            onClick={handleSelectAllSymbols}
                          >
                            {selectedSymbols.size === allSymbols.length ? '取消全选' : '全选'}
                          </button>
                          {selectedSymbols.size > 0 && (
                            <button
                              type="button"
                              className="historical-cache-filter-menu-action"
                              onClick={() => setSelectedSymbols(new Set())}
                            >
                              清空
                            </button>
                          )}
                        </div>
                        <div className="historical-cache-filter-menu-items">
                          {allSymbols.map((symbol) => (
                            <label
                              key={symbol}
                              className="historical-cache-filter-menu-item"
                            >
                              <input
                                type="checkbox"
                                checked={selectedSymbols.has(symbol)}
                                onChange={() => handleSymbolToggle(symbol)}
                              />
                              <span>{symbol}</span>
                            </label>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </div>

                {(selectedExchanges.size > 0 || selectedSymbols.size > 0) && (
                  <button
                    type="button"
                    className="historical-cache-filter-clear"
                    onClick={handleClearFilters}
                  >
                    清除筛选
                  </button>
                )}
              </div>
            </div>

            {/* 汇总信息 */}
            <div className="historical-cache-summary">
              <h3 className="historical-cache-section-title">数据汇总</h3>
              <div className="historical-cache-summary-grid">
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">支持的交易所</div>
                  <div className="historical-cache-summary-value">
                    {summary.exchanges.length > 0 ? summary.exchanges.join(', ') : '无'}
                  </div>
                </div>
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">支持的周期</div>
                  <div className="historical-cache-summary-value">
                    {summary.timeframes.length > 0 ? summary.timeframes.join(', ') : '无'}
                  </div>
                </div>
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">支持的币对</div>
                  <div className="historical-cache-summary-value">
                    {summary.symbols.length > 0 ? summary.symbols.join(', ') : '无'}
                  </div>
                </div>
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">缓存条目数</div>
                  <div className="historical-cache-summary-value">{summary.totalSnapshots}</div>
                </div>
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">总K线数量</div>
                  <div className="historical-cache-summary-value">{summary.totalCount.toLocaleString()}</div>
                </div>
                <div className="historical-cache-summary-item">
                  <div className="historical-cache-summary-label">缓存时间范围</div>
                  <div className="historical-cache-summary-value">
                    {summary.earliestStart && summary.latestEnd
                      ? `${formatDateTime(summary.earliestStart.toISOString())} ~ ${formatDateTime(summary.latestEnd.toISOString())}`
                      : '无数据'}
                  </div>
                </div>
              </div>
            </div>

            {/* 详细列表 */}
            {filteredSnapshots.length > 0 ? (
              <div className="historical-cache-details">
                <h3 className="historical-cache-section-title">详细缓存信息</h3>
                <div className="historical-cache-list">
                  {groupedByExchange.map(([exchange, items]) => (
                    <div key={exchange} className="historical-cache-exchange-group">
                      <div className="historical-cache-exchange-header">
                        <span className="historical-cache-exchange-name">{exchange}</span>
                        <span className="historical-cache-exchange-count">{items.length} 项</span>
                      </div>
                      <div className="historical-cache-table-wrapper">
                        <table className="historical-cache-table">
                          <thead>
                            <tr>
                              <th>币对</th>
                              <th>周期</th>
                              <th>开始时间</th>
                              <th>结束时间</th>
                              <th>K线数量</th>
                            </tr>
                          </thead>
                          <tbody>
                            {items
                              .sort((a, b) => {
                                if (a.symbol !== b.symbol) return a.symbol.localeCompare(b.symbol);
                                return a.timeframe.localeCompare(b.timeframe);
                              })
                              .map((item, index) => (
                                <tr key={`${item.exchange}-${item.symbol}-${item.timeframe}-${index}`}>
                                  <td>{item.symbol}</td>
                                  <td>{item.timeframe}</td>
                                  <td>{formatDateTime(item.startTime)}</td>
                                  <td>{formatDateTime(item.endTime)}</td>
                                  <td>{item.count.toLocaleString()}</td>
                                </tr>
                              ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <div className="historical-cache-empty">暂无缓存数据</div>
            )}
          </>
        )}
      </div>
    </Dialog>
  );
};

export default HistoricalDataCacheDialog;

