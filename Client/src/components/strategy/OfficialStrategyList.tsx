import React, { useCallback, useEffect, useMemo, useState } from 'react';
import OfficialStrategyItem from './OfficialStrategyItem';
import OfficialStrategyDetailDialog, { type OfficialStrategyDetailRecord } from './OfficialStrategyDetailDialog';
import type { StrategyHistoryVersion } from './StrategyHistoryDialog';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import { HttpClient, getToken } from '../../network/index.ts';
import { Dialog, useNotification } from '../ui/index.ts';
import './StrategyCatalog.css';

type StrategyCatalogRecord = {
  defId: number;
  name: string;
  description: string;
  versionNo: number;
  configJson?: StrategyConfig;
  updatedAt?: string;
};

const resolveSymbol = (symbol?: string) => {
  if (!symbol) {
    return { currency: '-', pair: '-' };
  }
  const parts = symbol.split('/');
  return {
    currency: parts[0] || symbol,
    pair: symbol,
  };
};

const formatPercent = (value?: number) => {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }
  const normalized = value > 1 ? value : value * 100;
  const text = normalized.toFixed(2).replace(/\.00$/, '');
  return `${text}%`;
};

const formatQuantity = (value: number | undefined, currency: string) => {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }
  return `${value} ${currency}`;
};

const OfficialStrategyList: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError } = useNotification();
  const [records, setRecords] = useState<StrategyCatalogRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [detailTarget, setDetailTarget] = useState<OfficialStrategyDetailRecord | null>(null);
  const [isDetailDialogOpen, setIsDetailDialogOpen] = useState(false);

  const fetchRecords = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.postProtocol<StrategyCatalogRecord[]>('/api/strategy/official/list', 'strategy.official.list');
      setRecords(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取官方策略失败';
      showError(message);
    } finally {
      setIsLoading(false);
    }
  }, [client, showError]);

  useEffect(() => {
    fetchRecords();
  }, [fetchRecords]);

  const handleViewDetail = (record: StrategyCatalogRecord) => {
    setDetailTarget({
      defId: record.defId,
      name: record.name,
      description: record.description,
      versionNo: record.versionNo,
    });
    setIsDetailDialogOpen(true);
  };

  const closeDetailDialog = () => {
    setIsDetailDialogOpen(false);
    setDetailTarget(null);
  };

  const handleViewHistory = async (defId: number) => {
    const data = await client.postProtocol<StrategyHistoryVersion[]>('/api/strategy/official/versions', 'strategy.official.versions', { defId });
    return Array.isArray(data) ? data : [];
  };

  const items = useMemo(
    () =>
      records.map((record) => {
        const trade = record.configJson?.trade as StrategyTradeConfig | undefined;
        const risk = trade?.risk;
        const { currency, pair } = resolveSymbol(trade?.symbol);
        const takeProfit = formatPercent(risk?.takeProfitPct);
        const stopLoss = formatPercent(risk?.stopLossPct);
        const profitLossRatio =
          takeProfit === '-' && stopLoss === '-'
            ? '-'
            : `止盈 ${takeProfit} / 止损 ${stopLoss}`;

        return (
          <OfficialStrategyItem
            key={record.defId}
            name={record.name}
            description={record.description}
            exchange={trade?.exchange ?? '-'}
            tradingPair={pair}
            leverage={trade?.sizing?.leverage ?? 0}
            singlePosition={formatQuantity(trade?.sizing?.orderQty, currency)}
            totalPosition={formatQuantity(trade?.sizing?.maxPositionQty, currency)}
            profitLossRatio={profitLossRatio}
            version={record.versionNo}
            onUse={() => handleViewDetail(record)}
          />
        );
      }),
    [records],
  );

  return (
    <div className="strategy-catalog-page">
      <div className="strategy-catalog-header">
        <div className="strategy-catalog-title-group">
          <h1 className="strategy-catalog-title">官方策略</h1>
          <span className="strategy-catalog-subtitle">由官方维护的高质量策略</span>
        </div>
      </div>
      <div className="strategy-catalog-grid">
        {isLoading ? (
          <div className="strategy-catalog-empty">加载中...</div>
        ) : items.length === 0 ? (
          <div className="strategy-catalog-empty">暂无官方策略</div>
        ) : (
          items
        )}
      </div>
      <Dialog
        open={isDetailDialogOpen}
        onClose={closeDetailDialog}
        showCloseButton={false}
        cancelText=""
        confirmText=""
        className="official-strategy-detail-dialog"
      >
        {detailTarget && (
          <OfficialStrategyDetailDialog
            strategy={detailTarget}
            onClose={closeDetailDialog}
            onViewHistory={handleViewHistory}
          />
        )}
      </Dialog>
    </div>
  );
};

export default OfficialStrategyList;


