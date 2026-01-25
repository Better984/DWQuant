import React, { useCallback, useEffect, useMemo, useState } from 'react';
import StrategyItem from './StrategyItem';
import type { StrategyItemProps } from './StrategyItem.types';
import StrategyEditorFlow, { type StrategyEditorSubmitPayload } from './StrategyEditorFlow';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import AvatarByewind from '../assets/SnowUI/head/AvatarByewind.svg';
import { Dialog, useNotification } from './ui';
import { HttpClient, getToken } from '../network';
import './StrategyItem.css';

type StrategyListRecord = {
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description: string;
  state: string;
  versionNo: number;
  configJson?: StrategyConfig;
  updatedAt?: string;
};

const resolveStatus = (state: string | undefined): StrategyItemProps['status'] => {
  if (!state) {
    return 'stopped';
  }
  const normalized = state.trim().toLowerCase();
  if (normalized === 'running') {
    return 'running';
  }
  if (normalized === 'paused') {
    return 'paused';
  }
  return 'stopped';
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

const buildStrategyItem = (
  record: StrategyListRecord,
  onCreateVersion: (usId: number) => void,
): StrategyItemProps => {
  const trade = record.configJson?.trade as StrategyTradeConfig | undefined;
  const risk = trade?.risk;
  const { currency, pair } = resolveSymbol(trade?.symbol);
  const takeProfit = formatPercent(risk?.takeProfitPct);
  const stopLoss = formatPercent(risk?.stopLossPct);
  const profitLossRatio =
    takeProfit === '-' && stopLoss === '-'
      ? '-'
      : `止盈 ${takeProfit} / 止损 ${stopLoss}`;

  return {
    usId: record.usId,
    name: record.aliasName || record.defName,
    currency,
    tradingPair: pair,
    leverage: trade?.sizing?.leverage ?? 0,
    singlePosition: formatQuantity(trade?.sizing?.orderQty, currency),
    totalPosition: formatQuantity(trade?.sizing?.maxPositionQty, currency),
    profitLossRatio,
    ownerAvatar: AvatarByewind,
    status: resolveStatus(record.state),
    version: record.versionNo,
    onCreateVersion,
  };
};

const StrategyList: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError } = useNotification();
  const [records, setRecords] = useState<StrategyListRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [activeStrategy, setActiveStrategy] = useState<StrategyListRecord | null>(null);
  const [isEditorOpen, setIsEditorOpen] = useState(false);

  const fetchStrategies = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.get<StrategyListRecord[]>('/api/strategy/list');
      setRecords(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取策略列表失败';
      showError(message);
    } finally {
      setIsLoading(false);
    }
  }, [client, showError]);

  useEffect(() => {
    fetchStrategies();
    const handler = () => fetchStrategies();
    window.addEventListener('strategy:changed', handler);
    return () => {
      window.removeEventListener('strategy:changed', handler);
    };
  }, [fetchStrategies]);

  const handleCreateVersion = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setActiveStrategy(target);
    setIsEditorOpen(true);
  };

  const closeEditor = () => {
    setIsEditorOpen(false);
    setActiveStrategy(null);
  };

  const handleSubmitUpdate = async (payload: StrategyEditorSubmitPayload) => {
    if (!activeStrategy) {
      throw new Error('未选择策略');
    }
    await client.post('/api/strategy/update', {
      usId: activeStrategy.usId,
      configJson: payload.configJson,
      changelog: '',
    });
  };

  const strategyItems = useMemo(
    () => records.map((record) => buildStrategyItem(record, handleCreateVersion)),
    [records],
  );

  const initialTradeConfig = activeStrategy?.configJson?.trade as StrategyTradeConfig | undefined;

  return (
    <div className="strategy-list-page">
      <div className="page-title">
        <h1 className="title-text">策略列表</h1>
        <span className="title-subtext">查看和管理您的交易策略</span>
      </div>
      <div className="strategy-list-container">
        {isLoading ? (
          <div className="strategy-list-empty">加载中...</div>
        ) : strategyItems.length === 0 ? (
          <div className="strategy-list-empty">暂无策略记录</div>
        ) : (
          strategyItems.map((strategy) => <StrategyItem key={strategy.usId} {...strategy} />)
        )}
      </div>
      <Dialog
        open={isEditorOpen}
        onClose={closeEditor}
        showCloseButton={false}
        cancelText=""
        confirmText=""
        className="strategy-editor-dialog"
      >
        {activeStrategy && (
          <StrategyEditorFlow
            key={activeStrategy.usId}
            onClose={closeEditor}
            onSubmit={handleSubmitUpdate}
            submitLabel="创建新版本"
            successMessage="新版本创建成功"
            errorMessage="创建新版本失败，请稍后重试"
            initialName={activeStrategy.aliasName || activeStrategy.defName}
            initialDescription={activeStrategy.description}
            initialTradeConfig={initialTradeConfig}
            initialConfig={activeStrategy.configJson}
            disableMetaFields={true}
          />
        )}
      </Dialog>
    </div>
  );
};

export default StrategyList;
