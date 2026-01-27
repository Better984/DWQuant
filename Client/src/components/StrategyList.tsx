import React, { useCallback, useEffect, useMemo, useState } from 'react';
import StrategyItem from './StrategyItem';
import type { StrategyItemProps } from './StrategyItem.types';
import StrategyEditorFlow, { type StrategyEditorSubmitPayload } from './StrategyEditorFlow';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyShareImportDialog from './StrategyShareImportDialog';
import StrategyDetailDialog, { type StrategyDetailRecord } from './StrategyDetailDialog';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import AvatarByewind from '../assets/SnowUI/head/AvatarByewind.svg';
import { Dialog, useNotification } from './ui';
import AlertDialog from './AlertDialog';
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
    onViewDetail,
  };
};

const StrategyList: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError, success: showSuccess } = useNotification();
  const [records, setRecords] = useState<StrategyListRecord[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [activeStrategy, setActiveStrategy] = useState<StrategyListRecord | null>(null);
  const [isEditorOpen, setIsEditorOpen] = useState(false);
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

  const handleViewHistory = async (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setHistoryStrategy(target);
    setIsHistoryOpen(true);
    setIsHistoryLoading(true);

    try {
      const data = await client.get<StrategyHistoryVersion[]>('/api/strategy/versions', { usId });
      const versions = Array.isArray(data) ? data : [];
      setHistoryVersions(versions);
      const pinnedVersion = versions.find((item) => item.isPinned);
      const fallbackVersion = pinnedVersion ?? versions[versions.length - 1];
      setSelectedHistoryVersionId(fallbackVersion ? fallbackVersion.versionId : null);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取历史版本失败';
      showError(message);
    } finally {
      setIsHistoryLoading(false);
    }
  };

  const handleOpenShare = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setShareTarget(target);
    setIsShareDialogOpen(true);
  };

  const closeShareDialog = () => {
    setIsShareDialogOpen(false);
    setShareTarget(null);
  };

  const handleCreateShare = async (usId: number, payload: SharePolicyPayload) => {
    const data = await client.post<{ shareCode: string }>('/api/strategy/share/create-code', {
      usId,
      policy: payload,
    });
    return data.shareCode;
  };

  const handleUpdateStatus = async (
    usId: number,
    status: 'running' | 'paused' | 'paused_open_position' | 'completed',
  ) => {
    await client.request({
      method: 'PATCH',
      path: `/api/strategy/instances/${usId}/state`,
      body: { state: status },
    });
    await fetchStrategies();
  };

  const handleViewDetail = (usId: number) => {
    const target = records.find((item) => item.usId === usId) ?? null;
    if (!target) {
      return;
    }
    setDetailTarget(target);
    setIsDetailDialogOpen(true);
  };

  const closeDetailDialog = () => {
    setIsDetailDialogOpen(false);
    setDetailTarget(null);
  };

  const openImportDialog = () => {
    setIsImportDialogOpen(true);
  };

  const closeImportDialog = () => {
    setIsImportDialogOpen(false);
  };

  const handleImportShare = async (payload: { shareCode: string; aliasName?: string }) => {
    await client.post('/api/strategy/import/share-code', payload);
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
      await client.post('/api/strategy/delete', { usId: deleteTarget.usId });
      showSuccess('策略删除成功');
      if (historyStrategy?.usId === deleteTarget.usId) {
        closeHistory();
      }
      closeDeleteDialog();
      await fetchStrategies();
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
    await client.post('/api/strategy/update', {
      usId: activeStrategy.usId,
      configJson: payload.configJson,
      changelog: '',
    });
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
            onCreateShare={handleCreateShare}
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
              const data = await client.get<StrategyHistoryVersion[]>('/api/strategy/versions', { usId });
              return Array.isArray(data) ? data : [];
            }}
            onCreateShare={handleCreateShare}
            onUpdateStatus={handleUpdateStatus}
            onDelete={handleDelete}
          />
        )}
      </Dialog>
    </div>
  );
};

export default StrategyList;
