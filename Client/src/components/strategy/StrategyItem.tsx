import React from 'react';
import './StrategyItem.css';
import type { StrategyItemProps } from './StrategyItem.types';

const StrategyItem: React.FC<StrategyItemProps> = ({
  name,
  currency,
  tradingPair,
  leverage,
  singlePosition,
  totalPosition,
  profitLossRatio,
  ownerAvatar,
  status,
  version,
  catalogTag,
  usId,
  onViewDetail,
}) => {
  const getStatusText = () => {
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

  const getStatusColor = () => {
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

  const handleViewDetail = () => {
    if (onViewDetail) {
      onViewDetail(usId);
    }
  };

  const catalogClass =
    catalogTag === 'both'
      ? 'strategy-item--catalog-both'
      : catalogTag === 'official'
        ? 'strategy-item--catalog-official'
        : catalogTag === 'template'
          ? 'strategy-item--catalog-template'
          : '';

  return (
    <div className={`strategy-item ${catalogClass}`.trim()}>
      <div className="strategy-item-header">
        <div className="strategy-item-title-section">
          <h3 className="strategy-item-name">
            {name}
            {version && <span className="strategy-version">v{version}</span>}
          </h3>
          <div className="strategy-item-meta">
            <span className="strategy-currency">{currency}</span>
            <span className="strategy-separator">·</span>
            <span className="strategy-trading-pair">{tradingPair}</span>
            <span className="strategy-separator">·</span>
            <span className="strategy-leverage">{leverage}x 杠杆</span>
          </div>
        </div>
        <div className="strategy-item-header-right">
          <div className={`strategy-status ${getStatusColor()}`}>
            <div className="status-dot"></div>
            <span>{getStatusText()}</span>
          </div>
        </div>
      </div>

      <div className="strategy-item-content">
        <div className="strategy-item-row">
          <div className="strategy-item-label">单次开仓</div>
          <div className="strategy-item-value">{singlePosition}</div>
        </div>
        <div className="strategy-item-row">
          <div className="strategy-item-label">持仓总量</div>
          <div className="strategy-item-value">{totalPosition}</div>
        </div>
        <div className="strategy-item-row">
          <div className="strategy-item-label">止盈止损比例</div>
          <div className="strategy-item-value">{profitLossRatio}</div>
        </div>
      </div>

      <div className="strategy-item-footer">
        <div className="strategy-owner">
          <img src={ownerAvatar} alt="Owner" className="strategy-owner-avatar" />
          <span className="strategy-owner-name">策略持有人</span>
        </div>
        <div className="strategy-item-actions">
          <button
            className="strategy-action-btn strategy-action-btn--primary"
            type="button"
            onClick={handleViewDetail}
            disabled={!onViewDetail}
          >
            详情/编辑
          </button>
        </div>
      </div>
    </div>
  );
};

export default StrategyItem;
export type { StrategyItemProps } from './StrategyItem.types';





