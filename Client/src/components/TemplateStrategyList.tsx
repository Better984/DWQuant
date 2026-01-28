import React, { useCallback, useEffect, useMemo, useState } from 'react';
import StrategyTemplateItem from './StrategyTemplateItem';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import { HttpClient, getToken } from '../network';
import { useNotification } from './ui';
import './StrategyCatalog.css';

type StrategyCatalogRecord = {
  defId: number;
  name: string;
  description: string;
  versionNo: number;
  configJson?: StrategyConfig;
  updatedAt?: string;
};

const formatTimeframe = (timeframeSec?: number) => {
  if (!timeframeSec) {
    return '-';
  }
  if (timeframeSec % 3600 === 0) {
    return `${timeframeSec / 3600}H`;
  }
  if (timeframeSec % 60 === 0) {
    return `${timeframeSec / 60}M`;
  }
  return `${timeframeSec}S`;
};

const TemplateStrategyList: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError } = useNotification();
  const [records, setRecords] = useState<StrategyCatalogRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  const fetchRecords = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.get<StrategyCatalogRecord[]>('/api/strategy/template/list');
      setRecords(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取策略模板失败';
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
        const metaParts = [trade?.exchange, trade?.symbol, formatTimeframe(trade?.timeframeSec)].filter(
          Boolean,
        );
        const meta = metaParts.length > 0 ? metaParts.join(' · ') : undefined;

        return (
          <StrategyTemplateItem
            key={record.defId}
            name={record.name}
            description={record.description}
            meta={meta}
          />
        );
      }),
    [records],
  );

  return (
    <div className="strategy-catalog-page">
      <div className="strategy-catalog-header">
        <div className="strategy-catalog-title-group">
          <h1 className="strategy-catalog-title">策略模板</h1>
          <span className="strategy-catalog-subtitle">选择模板快速创建策略实例</span>
        </div>
      </div>
      <div className="strategy-catalog-grid">
        {isLoading ? (
          <div className="strategy-catalog-empty">加载中...</div>
        ) : items.length === 0 ? (
          <div className="strategy-catalog-empty">暂无策略模板</div>
        ) : (
          items
        )}
      </div>
    </div>
  );
};

export default TemplateStrategyList;
