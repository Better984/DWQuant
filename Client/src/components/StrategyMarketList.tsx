import React, { useCallback, useEffect, useMemo, useState } from 'react';
import StrategyMarketItem from './StrategyMarketItem';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import { HttpClient, getToken } from '../network';
import { useNotification } from './ui';
import './StrategyCatalog.css';

type StrategyMarketRecord = {
  marketId: number;
  usId: number;
  uid: number;
  title: string;
  description: string;
  versionNo: number;
  configJson?: StrategyConfig;
  authorName?: string | null;
  updatedAt?: string;
};

const formatUpdatedAt = (value?: string) => {
  if (!value) {
    return '';
  }
  return value.replace('T', ' ').replace('Z', '');
};

const StrategyMarketList: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError } = useNotification();
  const [records, setRecords] = useState<StrategyMarketRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  const fetchRecords = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.get<StrategyMarketRecord[]>('/api/strategy/market/list');
      setRecords(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取策略广场失败';
      showError(message);
    } finally {
      setIsLoading(false);
    }
  }, [client, showError]);

  useEffect(() => {
    fetchRecords();
  }, [fetchRecords]);

  const items = useMemo(
    () =>
      records.map((record) => {
        const trade = record.configJson?.trade as StrategyTradeConfig | undefined;
        return (
          <StrategyMarketItem
            key={record.marketId}
            title={record.title}
            description={record.description}
            author={record.authorName}
            exchange={trade?.exchange ?? '-'}
            symbol={trade?.symbol ?? '-'}
            leverage={trade?.sizing?.leverage ?? 0}
            updatedAt={formatUpdatedAt(record.updatedAt)}
          />
        );
      }),
    [records],
  );

  return (
    <div className="strategy-catalog-page">
      <div className="strategy-catalog-header">
        <div className="strategy-catalog-title-group">
          <h1 className="strategy-catalog-title">策略广场</h1>
          <span className="strategy-catalog-subtitle">公开策略展示区</span>
        </div>
      </div>
      <div className="strategy-catalog-grid">
        {isLoading ? (
          <div className="strategy-catalog-empty">加载中...</div>
        ) : items.length === 0 ? (
          <div className="strategy-catalog-empty">暂无公开策略</div>
        ) : (
          items
        )}
      </div>
    </div>
  );
};

export default StrategyMarketList;
