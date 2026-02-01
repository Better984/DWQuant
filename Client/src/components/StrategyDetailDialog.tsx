import React, { useState, useMemo, useEffect } from 'react';
import { useNotification } from './ui';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import AlertDialog from './AlertDialog';
import { getAuthProfile } from '../auth/profileStore';
import './StrategyDetailDialog.css';

export type StrategyDetailRecord = {
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description: string;
  state: string;
  versionNo: number;
  exchangeApiKeyId?: number | null;
  configJson?: any;
  updatedAt?: string;
  officialDefId?: number | null;
  officialVersionNo?: number | null;
  templateDefId?: number | null;
  templateVersionNo?: number | null;
  marketId?: number | null;
  marketVersionNo?: number | null;
};

export type StrategyPositionRecord = {
  positionId?: number;
  uid?: number;
  usId?: number;
  exchange?: string;
  symbol?: string;
  side?: string;
  status?: string;
  entryPrice?: number;
  qty?: number;
  stopLossPrice?: number;
  takeProfitPrice?: number;
  trailingEnabled?: boolean;
  trailingTriggered?: boolean;
  trailingStopPrice?: number;
  closeReason?: string | null;
  openedAt?: string;
  closedAt?: string | null;
};

type StrategyDetailDialogProps = {
  strategy: StrategyDetailRecord | null;
  onClose: () => void;
  onCreateVersion: (usId: number) => void;
  onViewHistory: (usId: number) => Promise<StrategyHistoryVersion[]>;
  onCreateShare: (usId: number, payload: SharePolicyPayload) => Promise<string>;
  onUpdateStatus: (usId: number, status: 'running' | 'paused' | 'paused_open_position' | 'completed') => Promise<void>;
  onDelete: (usId: number) => void;
  onEditStrategy: (usId: number) => void;
  onFetchOpenPositionsCount: (usId: number) => Promise<number>;
  onFetchPositions: (usId: number) => Promise<StrategyPositionRecord[]>;
  onClosePositions: (usId: number) => Promise<void>;
  onPublishOfficial: (usId: number) => Promise<void>;
  onPublishTemplate: (usId: number) => Promise<void>;
  onPublishMarket: (usId: number) => Promise<void>;
  onSyncOfficial: (usId: number) => Promise<void>;
  onSyncTemplate: (usId: number) => Promise<void>;
  onSyncMarket: (usId: number) => Promise<void>;
  onRemoveOfficial: (usId: number) => Promise<void>;
  onRemoveTemplate: (usId: number) => Promise<void>;
};

type TabType = 'info' | 'share' | 'history' | 'positions';

const StrategyDetailDialog: React.FC<StrategyDetailDialogProps> = ({
  strategy,
  onClose,
  onCreateVersion,
  onViewHistory,
  onCreateShare,
  onUpdateStatus,
  onDelete,
  onEditStrategy,
  onFetchOpenPositionsCount,
  onFetchPositions,
  onClosePositions,
  onPublishOfficial,
  onPublishTemplate,
  onPublishMarket,
  onSyncOfficial,
  onSyncTemplate,
  onSyncMarket,
  onRemoveOfficial,
  onRemoveTemplate,
}) => {
  const { success, error } = useNotification();
  const [activeTab, setActiveTab] = useState<TabType>('info');
  const [currentStatus, setCurrentStatus] = useState<'running' | 'paused' | 'paused_open_position' | 'completed'>('completed');
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);
  const [historyVersions, setHistoryVersions] = useState<StrategyHistoryVersion[]>([]);
  const [selectedHistoryVersionId, setSelectedHistoryVersionId] = useState<number | null>(null);
  const [isHistoryLoading, setIsHistoryLoading] = useState(false);
  const [shareCode, setShareCode] = useState<string | null>(null);
  const [isShareLoading, setIsShareLoading] = useState(false);
  const [publishTarget, setPublishTarget] = useState<'official' | 'template' | null>(null);
  const [isPublishing, setIsPublishing] = useState(false);
  const [isMarketPublishing, setIsMarketPublishing] = useState(false);
  const [isMarketConfirmOpen, setIsMarketConfirmOpen] = useState(false);
  const [isEditConfirmOpen, setIsEditConfirmOpen] = useState(false);
  const [openPositionCount, setOpenPositionCount] = useState(0);
  const [isCheckingPositions, setIsCheckingPositions] = useState(false);
  const [syncTarget, setSyncTarget] = useState<'official' | 'template' | 'market' | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<'official' | 'template' | null>(null);
  const [isRemoving, setIsRemoving] = useState(false);
  const [positions, setPositions] = useState<StrategyPositionRecord[]>([]);
  const [isPositionsLoading, setIsPositionsLoading] = useState(false);
  const [hasLoadedPositions, setHasLoadedPositions] = useState(false);
  const [isClosePositionsConfirmOpen, setIsClosePositionsConfirmOpen] = useState(false);
  const [isClosingPositions, setIsClosingPositions] = useState(false);

  const profile = useMemo(() => getAuthProfile(), []);
  const canPublish = profile?.role === 255;

  useEffect(() => {
    if (strategy) {
      const status = strategy.state?.trim().toLowerCase();
      if (status === 'running') {
        setCurrentStatus('running');
      } else if (status === 'paused') {
        setCurrentStatus('paused');
      } else if (status === 'paused_open_position') {
        setCurrentStatus('paused_open_position');
      } else {
        setCurrentStatus('completed');
      }
    }
  }, [strategy]);

  const handleUpdateStatus = async (newStatus: 'running' | 'paused' | 'paused_open_position' | 'completed') => {
    if (!strategy || isUpdatingStatus) {
      return;
    }
    setIsUpdatingStatus(true);
    try {
      await onUpdateStatus(strategy.usId, newStatus);
      setCurrentStatus(newStatus);
      success('策略状态已更新');
    } catch (err) {
      const message = err instanceof Error ? err.message : '更新策略状态失败';
      error(message);
    } finally {
      setIsUpdatingStatus(false);
    }
  };

  const handleLoadHistory = async () => {
    if (!strategy || isHistoryLoading) {
      return;
    }
    setIsHistoryLoading(true);
    try {
      const versions = await onViewHistory(strategy.usId);
      setHistoryVersions(versions);
      const pinnedVersion = versions.find((item) => item.isPinned);
      const fallbackVersion = pinnedVersion ?? versions[versions.length - 1];
      setSelectedHistoryVersionId(fallbackVersion ? fallbackVersion.versionId : null);
    } catch (err) {
      const message = err instanceof Error ? err.message : '加载历史版本失败';
      error(message);
    } finally {
      setIsHistoryLoading(false);
    }
  };

  const handleCreateShare = async (payload: SharePolicyPayload) => {
    if (!strategy || isShareLoading) {
      throw new Error('策略未选择');
    }
    setIsShareLoading(true);
    try {
      const code = await onCreateShare(strategy.usId, payload);
      setShareCode(code);
      return code;
    } finally {
      setIsShareLoading(false);
    }
  };

  const handleTabChange = (tab: TabType) => {
    setActiveTab(tab);
    if (tab === 'history' && historyVersions.length === 0) {
      handleLoadHistory();
    }
    if (tab === 'positions' && !hasLoadedPositions) {
      handleLoadPositions(false);
    }
  };

  const handleCreateVersion = () => {
    if (strategy) {
      onCreateVersion(strategy.usId);
      onClose();
    }
  };

  const handleEditStrategy = async () => {
    if (!strategy || isCheckingPositions) {
      return;
    }
    setIsCheckingPositions(true);
    try {
      const count = await onFetchOpenPositionsCount(strategy.usId);
      if (count > 0) {
        setOpenPositionCount(count);
        setIsEditConfirmOpen(true);
        return;
      }
      onEditStrategy(strategy.usId);
      onClose();
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取仓位信息失败';
      error(message);
    } finally {
      setIsCheckingPositions(false);
    }
  };

  const handleCloseAllPositions = async () => {
    if (!strategy || isClosingPositions) {
      return;
    }
    setIsClosingPositions(true);
    try {
      await onUpdateStatus(strategy.usId, 'paused');
      await onClosePositions(strategy.usId);
      success('已发起一键平仓');
      await handleLoadPositions(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : '一键平仓失败';
      error(message);
    } finally {
      setIsClosingPositions(false);
      setIsClosePositionsConfirmOpen(false);
    }
  };

  const handleLoadPositions = async (forceReload: boolean) => {
    if (!strategy || isPositionsLoading) {
      return;
    }
    if (!forceReload && hasLoadedPositions) {
      return;
    }
    setIsPositionsLoading(true);
    try {
      const items = await onFetchPositions(strategy.usId);
      setPositions(items);
      setHasLoadedPositions(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取仓位历史失败';
      error(message);
    } finally {
      setIsPositionsLoading(false);
    }
  };

  const handlePublish = async (target: 'official' | 'template') => {
    if (!strategy || isPublishing) {
      return;
    }
    setIsPublishing(true);
    try {
      if (target === 'official') {
        await onPublishOfficial(strategy.usId);
        success('已发布到官方策略库');
      } else {
        await onPublishTemplate(strategy.usId);
        success('已发布到策略模板库');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '发布失败';
      error(message);
    } finally {
      setIsPublishing(false);
      setPublishTarget(null);
    }
  };

  const handlePublishMarket = async () => {
    if (!strategy || isMarketPublishing) {
      return;
    }
    setIsMarketPublishing(true);
    try {
      await onPublishMarket(strategy.usId);
      success('已公开到策略广场');
    } catch (err) {
      const message = err instanceof Error ? err.message : '公开失败，请稍后重试';
      error(message);
    } finally {
      setIsMarketPublishing(false);
      setIsMarketConfirmOpen(false);
    }
  };

  const handleSync = async (target: 'official' | 'template' | 'market') => {
    if (!strategy || isSyncing) {
      return;
    }
    setIsSyncing(true);
    try {
      if (target === 'official') {
        await onSyncOfficial(strategy.usId);
        success('已发布最新版本到官方策略库');
      } else if (target === 'template') {
        await onSyncTemplate(strategy.usId);
        success('已发布最新版本到策略模板库');
      } else {
        await onSyncMarket(strategy.usId);
        success('已发布最新版本到策略广场');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '发布最新版本失败';
      error(message);
    } finally {
      setIsSyncing(false);
      setSyncTarget(null);
    }
  };

  const handleRemove = async (target: 'official' | 'template') => {
    if (!strategy || isRemoving) {
      return;
    }
    setIsRemoving(true);
    try {
      if (target === 'official') {
        await onRemoveOfficial(strategy.usId);
        success('已从官方策略库移除');
      } else {
        await onRemoveTemplate(strategy.usId);
        success('已从策略模板库移除');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '移除失败';
      error(message);
    } finally {
      setIsRemoving(false);
      setRemoveTarget(null);
    }
  };

  const getStatusText = (status: string) => {
    switch (status) {
      case 'running':
        return '运行中';
      case 'paused':
        return '已暂停';
      case 'paused_open_position':
        return '暂停开新仓';
      case 'completed':
        return '完成';
      case 'error':
        return '错误';
      default:
        return '完成';
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'running':
        return 'status-running';
      case 'paused':
        return 'status-paused';
      case 'paused_open_position':
        return 'status-paused-open-position';
      case 'completed':
        return 'status-completed';
      case 'error':
        return 'status-error';
      default:
        return 'status-completed';
    }
  };

  const formatNumber = (value?: number) => {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '-';
    }
    return value.toFixed(4).replace(/\.?0+$/, '');
  };

  const formatStatus = (status?: string) => {
    if (!status) {
      return '-';
    }
    const normalized = status.toLowerCase();
    if (normalized === 'open') {
      return '未平仓';
    }
    if (normalized === 'closed') {
      return '已平仓';
    }
    return status;
  };

  const formatSide = (side?: string) => {
    if (!side) {
      return '-';
    }
    const normalized = side.toLowerCase();
    if (normalized === 'long') {
      return '多';
    }
    if (normalized === 'short') {
      return '空';
    }
    return side;
  };

  const formatBoolean = (value?: boolean) => {
    if (value === null || value === undefined) {
      return '-';
    }
    return value ? '是' : '否';
  };

  const formatCloseReason = (value?: string | null) => {
    if (!value) {
      return '-';
    }
    const normalized = value.toLowerCase();
    if (normalized === 'manual') {
      return '手动平仓';
    }
    if (normalized === 'stoploss') {
      return '止损';
    }
    if (normalized === 'takeprofit') {
      return '止盈';
    }
    if (normalized === 'trailingstop') {
      return '移动止盈';
    }
    return value;
  };

  const formatDateTimeLocal = (value?: string | null) => {
    if (!value) {
      return '-';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  };

  if (!strategy) {
    return null;
  }

  const officialPublished = Boolean(strategy.officialDefId);
  const templatePublished = Boolean(strategy.templateDefId);
  const marketPublished = Boolean(strategy.marketId);
  const officialVersionNo = strategy.officialVersionNo ?? 0;
  const templateVersionNo = strategy.templateVersionNo ?? 0;
  const marketVersionNo = strategy.marketVersionNo ?? 0;
  const officialOutdated = officialPublished && strategy.versionNo > officialVersionNo;
  const templateOutdated = templatePublished && strategy.versionNo > templateVersionNo;
  const marketOutdated = marketPublished && strategy.versionNo > marketVersionNo;

  return (
    <div className="strategy-detail-dialog">
      <div className="strategy-detail-header">
        <div className="strategy-detail-title-section">
          <h2 className="strategy-detail-title">
            {strategy.aliasName || strategy.defName}
            {strategy.versionNo && <span className="strategy-detail-version">v{strategy.versionNo}</span>}
          </h2>
          <div className={`strategy-detail-status ${getStatusColor(currentStatus)}`}>
            <div className="status-dot"></div>
            <span>{getStatusText(currentStatus)}</span>
          </div>
        </div>
        <button className="strategy-detail-close" type="button" onClick={onClose} aria-label="关闭">
          <svg width={20} height={20} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
              d="M18 6L6 18M6 6L18 18"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      </div>

      <div className="strategy-detail-tabs">
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'info' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('info')}
        >
          基本信息
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'share' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('share')}
        >
          分享码
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'history' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('history')}
        >
          历史版本
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'positions' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('positions')}
        >
          仓位
        </button>
      </div>

      <div className="strategy-detail-content">
        {activeTab === 'info' && (
          <div className="strategy-detail-info">
            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略状态</h3>
              <div className="strategy-detail-status-controls">
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'running' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('running')}
                  disabled={isUpdatingStatus || currentStatus === 'running'}
                >
                  {isUpdatingStatus && currentStatus !== 'running' ? '更新中...' : '运行中'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'paused' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('paused')}
                  disabled={isUpdatingStatus || currentStatus === 'paused'}
                >
                  {isUpdatingStatus && currentStatus !== 'paused' ? '更新中...' : '已暂停'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'paused_open_position' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('paused_open_position')}
                  disabled={isUpdatingStatus || currentStatus === 'paused_open_position'}
                >
                  {isUpdatingStatus && currentStatus !== 'paused_open_position' ? '更新中...' : '暂停开新仓'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'completed' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('completed')}
                  disabled={isUpdatingStatus || currentStatus === 'completed'}
                >
                  {isUpdatingStatus && currentStatus !== 'completed' ? '更新中...' : '完成'}
                </button>
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略信息</h3>
              <div className="strategy-detail-info-grid">
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">策略名称</span>
                  <span className="strategy-detail-info-value">{strategy.aliasName || strategy.defName}</span>
                </div>
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">版本号</span>
                  <span className="strategy-detail-info-value">v{strategy.versionNo}</span>
                </div>
                {strategy.description && (
                  <div className="strategy-detail-info-item strategy-detail-info-item--full">
                    <span className="strategy-detail-info-label">描述</span>
                    <span className="strategy-detail-info-value">{strategy.description}</span>
                  </div>
                )}
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">操作</h3>
              <div className="strategy-detail-actions">
                <button
                  type="button"
                  className="strategy-detail-action-btn strategy-detail-action-btn--primary"
                  onClick={handleCreateVersion}
                >
                  创建新版本
                </button>
                {!marketPublished && (
                  <button
                    type="button"
                    className="strategy-detail-action-btn"
                    onClick={() => setIsMarketConfirmOpen(true)}
                  >
                    公开到策略广场
                  </button>
                )}
                {marketPublished && marketOutdated && (
                  <button
                    type="button"
                    className="strategy-detail-action-btn"
                    onClick={() => setSyncTarget('market')}
                  >
                    发布最新版本到广场
                  </button>
                )}
                {canPublish && (
                  <>
                    {!officialPublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setPublishTarget('official')}
                      >
                        发布到官方
                      </button>
                    )}
                    {officialPublished && officialOutdated && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setSyncTarget('official')}
                      >
                        发布最新版本到官方
                      </button>
                    )}
                    {officialPublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                        onClick={() => setRemoveTarget('official')}
                      >
                        从官方策略中移除
                      </button>
                    )}
                    {!templatePublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setPublishTarget('template')}
                      >
                        发布到模板
                      </button>
                    )}
                    {templatePublished && templateOutdated && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setSyncTarget('template')}
                      >
                        发布最新版本到模板
                      </button>
                    )}
                    {templatePublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                        onClick={() => setRemoveTarget('template')}
                      >
                        从策略模板中移除
                      </button>
                    )}
                  </>
                )}
                <button
                  type="button"
                  className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                  onClick={() => onDelete(strategy.usId)}
                >
                  删除策略
                </button>
              </div>
              <div className="strategy-detail-actions strategy-detail-actions--secondary">
                <button
                  type="button"
                  className="strategy-detail-action-btn"
                  onClick={handleEditStrategy}
                  disabled={isCheckingPositions}
                >
                  {isCheckingPositions ? '检查仓位中...' : '修改策略'}
                </button>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'share' && (
          <div className="strategy-detail-share">
            <div className="strategy-detail-share-wrapper">
              <StrategyShareDialog
                strategyName={strategy.aliasName || strategy.defName}
                onCreateShare={handleCreateShare}
                onClose={() => {}}
              />
            </div>
          </div>
        )}

        {activeTab === 'history' && (
          <div className="strategy-detail-history">
            <StrategyHistoryDialog
              versions={historyVersions}
              selectedVersionId={selectedHistoryVersionId}
              onSelectVersion={setSelectedHistoryVersionId}
              onClose={() => {}}
              isLoading={isHistoryLoading}
            />
          </div>
        )}

        {activeTab === 'positions' && (
          <div className="strategy-detail-positions">
            <div className="strategy-detail-positions-card">
              <div className="strategy-detail-positions-header">
                <div>
                  <div className="strategy-detail-positions-title">仓位历史</div>
                  <div className="strategy-detail-positions-hint">
                    协议请求：GET /api/positions/by-strategy?usId={strategy.usId}&status=all
                  </div>
                </div>
                <div className="strategy-detail-positions-actions">
                  <button
                    type="button"
                    className="strategy-detail-positions-action strategy-detail-positions-action--danger"
                    onClick={() => setIsClosePositionsConfirmOpen(true)}
                    disabled={isPositionsLoading || isClosingPositions}
                  >
                    一键平仓
                  </button>
                  <button
                    type="button"
                    className="strategy-detail-positions-action"
                    onClick={() => handleLoadPositions(true)}
                    disabled={isPositionsLoading}
                  >
                    {isPositionsLoading ? '加载中...' : '刷新'}
                  </button>
                </div>
              </div>
              {isPositionsLoading ? (
                <div className="strategy-detail-empty">加载中...</div>
              ) : positions.length === 0 ? (
                <div className="strategy-detail-empty">暂无仓位记录</div>
              ) : (
                <div className="strategy-detail-positions-table">
                  <div className="positions-table-header">
                    <div className="positions-table-cell">仓位ID</div>
                    <div className="positions-table-cell">交易所</div>
                    <div className="positions-table-cell">交易对</div>
                    <div className="positions-table-cell">方向</div>
                    <div className="positions-table-cell">状态</div>
                    <div className="positions-table-cell">开仓价</div>
                    <div className="positions-table-cell">数量</div>
                    <div className="positions-table-cell">止损价</div>
                    <div className="positions-table-cell">止盈价</div>
                    <div className="positions-table-cell">启用移动止盈</div>
                    <div className="positions-table-cell">已触发</div>
                    <div className="positions-table-cell">移动止损价</div>
                    <div className="positions-table-cell">平仓原因</div>
                    <div className="positions-table-cell">开仓时间</div>
                    <div className="positions-table-cell">平仓时间</div>
                  </div>
                  <div className="positions-table-body">
                    {positions.map((position, index) => (
                      <div
                        className="positions-table-row"
                        key={position.positionId ?? `${position.openedAt ?? 'pos'}-${index}`}
                      >
                        <div className="positions-table-cell">{position.positionId ?? '-'}</div>
                        <div className="positions-table-cell">{position.exchange ?? '-'}</div>
                        <div className="positions-table-cell">{position.symbol ?? '-'}</div>
                        <div className="positions-table-cell">{formatSide(position.side)}</div>
                        <div className="positions-table-cell">{formatStatus(position.status)}</div>
                        <div className="positions-table-cell">{formatNumber(position.entryPrice)}</div>
                        <div className="positions-table-cell">{formatNumber(position.qty)}</div>
                        <div className="positions-table-cell">{formatNumber(position.stopLossPrice)}</div>
                        <div className="positions-table-cell">{formatNumber(position.takeProfitPrice)}</div>
                        <div className="positions-table-cell">{formatBoolean(position.trailingEnabled)}</div>
                        <div className="positions-table-cell">{formatBoolean(position.trailingTriggered)}</div>
                        <div className="positions-table-cell">{formatNumber(position.trailingStopPrice)}</div>
                        <div className="positions-table-cell">{formatCloseReason(position.closeReason)}</div>
                        <div className="positions-table-cell">{formatDateTimeLocal(position.openedAt)}</div>
                        <div className="positions-table-cell">{formatDateTimeLocal(position.closedAt)}</div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>
      <AlertDialog
        open={publishTarget !== null}
        title={publishTarget === 'official' ? '发布到官方策略库' : '发布到策略模板库'}
        description={publishTarget === 'official' ? '确定要发布此策略到官方策略库吗？发布后其他用户可以使用此策略。' : '确定要发布此策略到策略模板库吗？发布后其他用户可以使用此策略模板。'}
        helperText="发布后无法撤销，请谨慎操作"
        cancelText="取消"
        confirmText={isPublishing ? '发布中...' : '确认发布'}
        onCancel={() => setPublishTarget(null)}
        onClose={() => setPublishTarget(null)}
        onConfirm={() => {
          if (publishTarget) {
            handlePublish(publishTarget);
          }
        }}
      />
      <AlertDialog
        open={syncTarget !== null}
        title={
          syncTarget === 'official'
            ? '发布最新版本到官方策略库'
            : syncTarget === 'template'
              ? '发布最新版本到策略模板库'
              : '发布最新版本到策略广场'
        }
        description={
          syncTarget === 'official'
            ? '确认将最新版本同步到官方策略库吗？'
            : syncTarget === 'template'
              ? '确认将最新版本同步到策略模板库吗？'
              : '确认将最新版本同步到策略广场吗？'
        }
        helperText="同步后会覆盖公开版本，请谨慎操作"
        cancelText="取消"
        confirmText={isSyncing ? '发布中...' : '确认发布'}
        onCancel={() => setSyncTarget(null)}
        onClose={() => setSyncTarget(null)}
        onConfirm={() => {
          if (syncTarget) {
            handleSync(syncTarget);
          }
        }}
      />
      <AlertDialog
        open={removeTarget !== null}
        title={removeTarget === 'official' ? '从官方策略中移除' : '从策略模板中移除'}
        description={
          removeTarget === 'official'
            ? '确认将该策略从官方策略库移除吗？'
            : '确认将该策略从策略模板库移除吗？'
        }
        helperText="移除后其他用户将无法继续使用该发布记录。"
        cancelText="取消"
        confirmText={isRemoving ? '移除中...' : '确认移除'}
        danger={true}
        onCancel={() => setRemoveTarget(null)}
        onClose={() => setRemoveTarget(null)}
        onConfirm={() => {
          if (removeTarget) {
            handleRemove(removeTarget);
          }
        }}
      />
      <AlertDialog
        open={isMarketConfirmOpen}
        title="公开到策略广场"
        description="确认将该策略公开到策略广场吗？公开后所有用户都可查看。"
        helperText="公开后可继续更新版本。"
        cancelText="取消"
        confirmText={isMarketPublishing ? '公开中...' : '确认公开'}
        onCancel={() => setIsMarketConfirmOpen(false)}
        onClose={() => setIsMarketConfirmOpen(false)}
        onConfirm={handlePublishMarket}
      />
      <AlertDialog
        open={isEditConfirmOpen}
        title="提示"
        description={`当前有${openPositionCount}个仓位未平仓，是否一键平仓后前往编辑？`}
        cancelText="取消"
        confirmText="前往编辑"
        onCancel={() => setIsEditConfirmOpen(false)}
        onClose={() => setIsEditConfirmOpen(false)}
        onConfirm={() => {
          if (strategy) {
            setIsEditConfirmOpen(false);
            onEditStrategy(strategy.usId);
            onClose();
          }
        }}
      />
      <AlertDialog
        open={isClosePositionsConfirmOpen}
        title="一键平仓"
        description="确认将该策略暂停并平掉所有仓位吗？系统将分多空两次平仓。"
        helperText="该操作为人工平仓，将记录为手动平仓。"
        cancelText="取消"
        confirmText={isClosingPositions ? '处理中...' : '确认平仓'}
        danger={true}
        onCancel={() => setIsClosePositionsConfirmOpen(false)}
        onClose={() => setIsClosePositionsConfirmOpen(false)}
        onConfirm={handleCloseAllPositions}
      />
    </div>
  );
};

export default StrategyDetailDialog;



