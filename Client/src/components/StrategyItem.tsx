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
  usId,
  onCreateVersion,
}) => {
  const getStatusText = () => {
    switch (status) {
      case 'running':
        return '运行中';
      case 'stopped':
        return '已停止';
      case 'paused':
        return '已暂停';
      case 'error':
        return '错误';
      default:
        return '未知';
    }
  };

  const getStatusColor = () => {
    switch (status) {
      case 'running':
        return 'status-running';
      case 'stopped':
        return 'status-stopped';
      case 'paused':
        return 'status-paused';
      case 'error':
        return 'status-error';
      default:
        return 'status-stopped';
    }
  };

  const handleCreateVersion = () => {
    if (onCreateVersion) {
      onCreateVersion(usId);
    }
  };

  return (
    <div className="strategy-item">
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
        <div className={`strategy-status ${getStatusColor()}`}>
          <div className="status-dot"></div>
          <span>{getStatusText()}</span>
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
        <button
          className="strategy-create-version-btn"
          type="button"
          onClick={handleCreateVersion}
          disabled={!onCreateVersion}
        >
          创建新版本
        </button>
      </div>
    </div>
  );
};

export default StrategyItem;
export type { StrategyItemProps } from './StrategyItem.types';
