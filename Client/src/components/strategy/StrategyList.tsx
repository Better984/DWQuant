import React, { useCallback, useEffect, useMemo, useState } from 'react';
import StrategyItem from './StrategyItem';
import type { StrategyItemProps } from './StrategyItem.types';
import StrategyEditorFlow, { type StrategyEditorSubmitPayload } from './StrategyEditorFlow';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyShareImportDialog from './StrategyShareImportDialog';
import StrategyDetailDialog, {
  type StrategyDetailRecord,
  type StrategyPositionRecord,
  type BacktestRunPayload,
  type BacktestRunResult,
} from './StrategyDetailDialog';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import AvatarByewind from '../../assets/icons/head/AvatarByewind.svg';
import { Dialog, useNotification } from '../ui/index.ts';
import AlertDialog from '../dialogs/AlertDialog';
import { HttpClient, getToken } from '../../network/index.ts';
import './StrategyItem.css';
import './StrategyModule.css';

type StrategyListRecord = {
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description: string;
  state: string;
  versionNo: number;
  exchangeApiKeyId?: number | null;
  configJson?: StrategyConfig;
  updatedAt?: string;
  officialDefId?: number | null;
  officialVersionNo?: number | null;
  templateDefId?: number | null;
  templateVersionNo?: number | null;
  marketId?: number | null;
  marketVersionNo?: number | null;
};

type StrategyStateUpdateResponse = {
  usId?: number;
  state?: string;
  exchangeApiKeyId?: number | null;
};

type StrategyCatalogResponse = {
  usId?: number;
  defId?: number;
  versionId?: number;
};

type StrategyMarketResponse = {
  usId?: number;
  marketId?: number;
};

type StrategyUpdateResponse = {
  usId?: number;
  newVersionId?: number;
  newVersionNo?: number;
};

type StrategyImportResponse = {
  newUsId?: number;
};

type PositionListResponse = {
  items?: StrategyPositionRecord[];
};

const resolveStatus = (state: string | undefined): StrategyItemProps['status'] => {
  if (!state) {
    return 'completed';
  }
  const normalized = state.trim().toLowerCase();
  if (normalized === 'running') {
    return 'running';
  }
  if (normalized === 'paused') {
    return 'paused';
  }
  if (normalized === 'paused_open_position') {
    return 'paused_open_position';
  }
  if (normalized === 'testing') {
    return 'testing';
  }
  return 'completed';
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
  onViewDetail: (usId: number) => void,
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
  const isOfficial = Boolean(record.officialDefId);
  const isTemplate = Boolean(record.templateDefId);
  const catalogTag = isOfficial && isTemplate ? 'both' : isOfficial ? 'official' : isTemplate ? 'template' : undefined;

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
    catalogTag,
    onViewDetail,
  };
};

type StrategyListProps = {
  autoOpenImport?: boolean;
  onAutoOpenHandled?: () => void;
};

const StrategyList: React.FC<StrategyListProps> = ({ autoOpenImport, onAutoOpenHandled }) => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError, success: showSuccess } = useNotification();
  const [records, setRecords] = useState<StrategyListRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [activeStrategy, setActiveStrategy] = useState<StrategyListRecord | null>(null);
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editorMode, setEditorMode] = useState<'createVersion' | 'edit'>('createVersion');
  const [historyStrategy, setHistoryStrategy] = useState<StrategyListRecord | null>(null);
  const [historyVersions, setHistoryVersions] = useState<StrategyHistoryVersion[]>([]);
  const [selectedHistoryVersionId, setSelectedHistoryVersionId] = useState<number | null>(null);
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);
  const [isHistoryLoading, setIsHistoryLoading] = useState(false);
  const [shareTarget, setShareTarget] = useState<StrategyListRecord | null>(null);
  const [isShareDialogOpen, setIsShareDialogOpen] = useState(false);
  const [isImportDialogOpen, setIsImportDialogOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<StrategyListRecord | null>(null);
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [detailTarget, setDetailTarget] = useState<StrategyListRecord | null>(null);
  const [isDetailDialogOpen, setIsDetailDialogOpen] = useState(false);

  const fetchStrategies = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await client.postProtocol<StrategyListRecord[]>('/api/strategy/list', 'strategy.list');
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
    const handler = (event: Event) => {
      const customEvent = event as CustomEvent<{ skipReload?: boolean }>;
      if (customEvent.detail?.skipReload) {
        return;
      }
      fetchStrategies();
    };
    window.addEventListener('strategy:changed', handler);
    return () => {
      window.removeEventListener('strategy:changed', handler);
    };
  }, [fetchStrategies]);

  useEffect(() => {
    if (!autoOpenImport) {
      return;
    }
    // 先完成路由/菜单切换渲染，再异步打开导入弹窗，避免同步跳转带来的视觉抖动
    const timerId = window.setTimeout(() => {
      openImportDialog();
      if (onAutoOpenHandled) {
        onAutoOpenHandled();
      }
    }, 0);
    return () => {
      window.clearTimeout(timerId);
    };
  }, [autoOpenImport, onAutoOpenHandled]);

  // 统一做单条策略局部更新，避免每次操作后全量重拉列表
  const patchRecordByUsId = useCallback(
    (usId: number, updater: (record: StrategyListRecord) => StrategyListRecord | null) => {
      setRecords((prev) => {
        const next: StrategyListRecord[] = [];
        prev.forEach((record) => {
          if (record.usId !== usId) {
            next.push(record);
            return;
          }
          const updated = updater(record);
          if (updated) {
            next.push(updated);
          }
        });
        return next;
      });
      const patchStandaloneState = (record: StrategyListRecord | null) => {
        if (!record || record.usId !== usId) {
          return record;
        }
        return updater(record);
      };
      setActiveStrategy(patchStandaloneState);
      setHistoryStrategy(patchStandaloneState);
      setShareTarget(patchStandaloneState);
      setDeleteTarget(patchStandaloneState);
      setDetailTarget(patchStandaloneState);
    },
    [],
  );

  const mergeRecordByUsId = useCallback(
    (usId: number, patch: Partial<StrategyListRecord>) => {
      patchRecordByUsId(usId, (record) => ({ ...record, ...patch }));
    },
    [patchRecordByUsId],
  );

  const removeRecordByUsId = useCallback(
    (usId: number) => {
      patchRecordByUsId(usId, () => null);
    },
    [patchRecordByUsId],
  );

  const handleCreateVersion = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setActiveStrategy(target);
    setEditorMode('createVersion');
    setIsEditorOpen(true);
  };

  const handleEditStrategy = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setActiveStrategy(target);
    setEditorMode('edit');
    setIsEditorOpen(true);
  };

  const fetchOpenPositionsCount = async (usId: number) => {
    const data = await client.postProtocol<PositionListResponse>('/api/positions/by-strategy', 'position.list.by_strategy', {
      usId,
      status: 'Open',
    });
    const items = Array.isArray(data?.items) ? data.items : [];
    return items.length;
  };

  const fetchStrategyPositions = async (usId: number) => {
    const data = await client.postProtocol<PositionListResponse>('/api/positions/by-strategy', 'position.list.by_strategy', {
      usId,
      status: 'all',
    });
    return Array.isArray(data?.items) ? data.items : [];
  };

  const closeStrategyPositions = async (usId: number) => {
    await client.postProtocol('/api/positions/close-by-strategy', 'position.close.by_strategy', { usId });
  };

  const closeStrategyPosition = async (positionId: number) => {
    await client.postProtocol('/api/positions/close-by-id', 'position.close.by_id', { positionId });
  };

  const runBacktest = async (payload: BacktestRunPayload, reqId?: string) => {
    const data = await client.postProtocol<BacktestRunResult>('/api/backtest/run', 'backtest.run', payload, {
      timeoutMs: 120000,
      reqId,
    });
    return data;
  };

  const closeShareDialog = () => {
    setIsShareDialogOpen(false);
    setShareTarget(null);
  };

  const handleCreateShare = async (usId: number, payload: SharePolicyPayload) => {
    const data = await client.postProtocol<{ shareCode: string }>('/api/strategy/share/create-code', 'strategy.share.create', {
      usId,
      policy: payload,
    });
    return data.shareCode;
  };

  const handleUpdateStatus = async (
    usId: number,
    status: 'running' | 'paused' | 'paused_open_position' | 'testing' | 'completed',
  ) => {
    const data = await client.postProtocol<StrategyStateUpdateResponse>('/api/strategy/instances/state', 'strategy.instance.state.update', {
      id: usId,
      state: status,
    });
    const patch: Partial<StrategyListRecord> = {
      state: data?.state || status,
    };
    if (data && Object.prototype.hasOwnProperty.call(data, 'exchangeApiKeyId')) {
      patch.exchangeApiKeyId = data.exchangeApiKeyId ?? null;
    }
    mergeRecordByUsId(usId, patch);
  };

  const handlePublishOfficial = async (usId: number) => {
    const data = await client.postProtocol<StrategyCatalogResponse>('/api/strategy/publish/official', 'strategy.official.publish', { usId });
    patchRecordByUsId(usId, (record) => ({
      ...record,
      officialDefId: data?.defId ?? record.officialDefId ?? -1,
    }));
  };

  const handlePublishTemplate = async (usId: number) => {
    const data = await client.postProtocol<StrategyCatalogResponse>('/api/strategy/publish/template', 'strategy.template.publish', { usId });
    patchRecordByUsId(usId, (record) => ({
      ...record,
      templateDefId: data?.defId ?? record.templateDefId ?? -1,
    }));
  };

  const handlePublishMarket = async (usId: number) => {
    const data = await client.postProtocol<StrategyMarketResponse>('/api/strategy/market/publish', 'strategy.market.publish', { usId });
    patchRecordByUsId(usId, (record) => ({
      ...record,
      marketId: data?.marketId ?? record.marketId ?? -1,
    }));
  };

  const handleSyncOfficial = async (usId: number) => {
    const data = await client.postProtocol<StrategyCatalogResponse>('/api/strategy/official/sync', 'strategy.official.sync', { usId });
    if (typeof data?.defId === 'number') {
      mergeRecordByUsId(usId, { officialDefId: data.defId });
    }
  };

  const handleSyncTemplate = async (usId: number) => {
    const data = await client.postProtocol<StrategyCatalogResponse>('/api/strategy/template/sync', 'strategy.template.sync', { usId });
    if (typeof data?.defId === 'number') {
      mergeRecordByUsId(usId, { templateDefId: data.defId });
    }
  };

  const handleSyncMarket = async (usId: number) => {
    const data = await client.postProtocol<StrategyMarketResponse>('/api/strategy/market/sync', 'strategy.market.sync', { usId });
    if (typeof data?.marketId === 'number') {
      mergeRecordByUsId(usId, { marketId: data.marketId });
      return;
    }
    patchRecordByUsId(usId, (record) => ({
      ...record,
      marketId: record.marketId ?? -1,
    }));
  };

  const handleRemoveOfficial = async (usId: number) => {
    await client.postProtocol('/api/strategy/official/remove', 'strategy.official.remove', { usId });
    mergeRecordByUsId(usId, {
      officialDefId: null,
      officialVersionNo: null,
    });
  };

  const handleRemoveTemplate = async (usId: number) => {
    await client.postProtocol('/api/strategy/template/remove', 'strategy.template.remove', { usId });
    mergeRecordByUsId(usId, {
      templateDefId: null,
      templateVersionNo: null,
    });
  };

  const handleViewDetail = useCallback((usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setDetailTarget(target);
    setIsDetailDialogOpen(true);
  }, [records]);

  const closeDetailDialog = () => {
    setIsDetailDialogOpen(false);
    setDetailTarget(null);
  };

  function openImportDialog() {
    setIsImportDialogOpen(true);
  }

  const closeImportDialog = () => {
    setIsImportDialogOpen(false);
  };

  const handleImportShare = async (payload: { shareCode: string; aliasName?: string }) => {
    await client.postProtocol<StrategyImportResponse>('/api/strategy/import/share-code', 'strategy.share.import', payload);
    await fetchStrategies();
  };

  const closeHistory = () => {
    setIsHistoryOpen(false);
    setHistoryStrategy(null);
    setHistoryVersions([]);
    setSelectedHistoryVersionId(null);
    setIsHistoryLoading(false);
  };

  const handleDelete = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setDeleteTarget(target);
    setIsDeleteDialogOpen(true);
  };

  const closeDeleteDialog = () => {
    setIsDeleteDialogOpen(false);
    setDeleteTarget(null);
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget || isDeleting) {
      return;
    }
    setIsDeleting(true);
    try {
      await client.postProtocol('/api/strategy/delete', 'strategy.delete', { usId: deleteTarget.usId });
      showSuccess('策略删除成功');
      if (historyStrategy?.usId === deleteTarget.usId) {
        closeHistory();
      }
      if (detailTarget?.usId === deleteTarget.usId) {
        closeDetailDialog();
      }
      closeDeleteDialog();
      removeRecordByUsId(deleteTarget.usId);
    } catch (err) {
      const message = err instanceof Error ? err.message : '删除策略失败';
      showError(message);
    } finally {
      setIsDeleting(false);
    }
  };

  const closeEditor = () => {
    setIsEditorOpen(false);
    setActiveStrategy(null);
  };

  const handleSubmitUpdate = async (payload: StrategyEditorSubmitPayload) => {
    if (!activeStrategy) {
      throw new Error('未选择策略');
    }
    const data = await client.postProtocol<StrategyUpdateResponse>('/api/strategy/update', 'strategy.update', {
      usId: activeStrategy.usId,
      configJson: payload.configJson,
      changelog: '',
      exchangeApiKeyId: payload.exchangeApiKeyId,
    });
    const patch: Partial<StrategyListRecord> = {
      configJson: payload.configJson,
    };
    if (typeof data?.newVersionNo === 'number' && data.newVersionNo > 0) {
      patch.versionNo = data.newVersionNo;
    }
    if (Object.prototype.hasOwnProperty.call(payload, 'exchangeApiKeyId')) {
      patch.exchangeApiKeyId = payload.exchangeApiKeyId ?? null;
    }
    mergeRecordByUsId(activeStrategy.usId, patch);
  };

  const strategyItems = useMemo(
    () => records.map((record) => buildStrategyItem(record, handleViewDetail)),
    [records, handleViewDetail],
  );

  const initialTradeConfig = activeStrategy?.configJson?.trade as StrategyTradeConfig | undefined;

  return (
    <div className="strategy-list-page">
      <div className="page-title strategy-list-header">
        <div className="strategy-list-title">
          <h1 className="title-text">策略列表</h1>
          <span className="title-subtext">查看和管理您的交易策略</span>
        </div>
        <button className="strategy-import-btn" type="button" onClick={openImportDialog}>
          分享码导入
        </button>
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
            submitLabel={editorMode === 'edit' ? '保存修改' : '创建新版本'}
            successMessage={editorMode === 'edit' ? '策略修改成功' : '新版本创建成功'}
            errorMessage={editorMode === 'edit' ? '修改失败，请稍后重试' : '创建新版本失败，请稍后重试'}
            initialName={activeStrategy.aliasName || activeStrategy.defName}
            initialDescription={activeStrategy.description}
            initialTradeConfig={initialTradeConfig}
            initialConfig={activeStrategy.configJson}
            initialExchangeApiKeyId={activeStrategy.exchangeApiKeyId ?? null}
            disableMetaFields={true}
            openConfigDirectly={editorMode === 'edit'}
          />
        )}
      </Dialog>
      <Dialog
        open={isShareDialogOpen}
        onClose={closeShareDialog}
        showCloseButton={false}
        cancelText=""
        confirmText=""
      >
        {shareTarget && (
          <StrategyShareDialog
            key={shareTarget.usId}
            strategyName={shareTarget.aliasName || shareTarget.defName}
            onCreateShare={(payload) => handleCreateShare(shareTarget.usId, payload)}
            onClose={closeShareDialog}
          />
        )}
      </Dialog>
      <Dialog
        open={isImportDialogOpen}
        onClose={closeImportDialog}
        showCloseButton={false}
        cancelText=""
        confirmText=""
      >
        <StrategyShareImportDialog onImportShare={handleImportShare} onClose={closeImportDialog} />
      </Dialog>
      <Dialog
        open={isHistoryOpen}
        onClose={closeHistory}
        showCloseButton={false}
        cancelText=""
        confirmText=""
        className="strategy-history-dialog"
      >
        <StrategyHistoryDialog
          versions={historyVersions}
          selectedVersionId={selectedHistoryVersionId}
          onSelectVersion={setSelectedHistoryVersionId}
          onClose={closeHistory}
          isLoading={isHistoryLoading}
        />
      </Dialog>
      <AlertDialog
        open={isDeleteDialogOpen}
        title="删除策略"
        description={`确定删除策略"${deleteTarget?.aliasName || deleteTarget?.defName || ''}"吗？`}
        helperText="删除后将无法恢复，请谨慎操作。"
        cancelText="取消"
        confirmText={isDeleting ? '删除中...' : '删除'}
        danger={true}
        onCancel={closeDeleteDialog}
        onClose={closeDeleteDialog}
        onConfirm={handleConfirmDelete}
      />
      <Dialog
        open={isDetailDialogOpen}
        onClose={closeDetailDialog}
        showCloseButton={false}
        cancelText=""
        confirmText=""
        className="strategy-detail-dialog"
      >
        {detailTarget && (
          <StrategyDetailDialog
            key={detailTarget.usId}
            strategy={detailTarget as StrategyDetailRecord}
            onClose={closeDetailDialog}
            onCreateVersion={handleCreateVersion}
            onViewHistory={async (usId: number) => {
              const data = await client.postProtocol<StrategyHistoryVersion[]>('/api/strategy/versions', 'strategy.versions', { usId });
              return Array.isArray(data) ? data : [];
            }}
            onCreateShare={handleCreateShare}
            onUpdateStatus={handleUpdateStatus}
            onDelete={handleDelete}
            onEditStrategy={handleEditStrategy}
            onFetchOpenPositionsCount={fetchOpenPositionsCount}
            onFetchPositions={fetchStrategyPositions}
            onClosePositions={closeStrategyPositions}
            onClosePosition={closeStrategyPosition}
            onPublishOfficial={handlePublishOfficial}
            onPublishTemplate={handlePublishTemplate}
            onPublishMarket={handlePublishMarket}
            onSyncOfficial={handleSyncOfficial}
            onSyncTemplate={handleSyncTemplate}
            onSyncMarket={handleSyncMarket}
            onRemoveOfficial={handleRemoveOfficial}
            onRemoveTemplate={handleRemoveTemplate}
            onRunBacktest={runBacktest}
          />
        )}
      </Dialog>
    </div>
  );
};

export default StrategyList;


