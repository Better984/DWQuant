import React from 'react';
import './OfficialStrategyItem.css';

export type OfficialStrategyItemProps = {
  name: string;
  description: string;
  exchange: string;
  tradingPair: string;
  leverage: number;
  singlePosition: string;
  totalPosition: string;
  profitLossRatio: string;
  version?: number;
  onUse?: () => void;
};

const OfficialStrategyItem: React.FC<OfficialStrategyItemProps> = ({
  name,
  description,
  exchange,
  tradingPair,
  leverage,
  singlePosition,
  totalPosition,
  profitLossRatio,
  version,
  onUse,
}) => {
  return (
    <div className="official-strategy-item">
      <div className="official-strategy-header">
        <div className="official-strategy-title-section">
          <div className="official-strategy-title-row">
            <h3 className="official-strategy-name">{name}</h3>
            <span className="official-strategy-badge">官方</span>
            {version ? <span className="official-strategy-version">v{version}</span> : null}
          </div>
          {description ? (
            <p className="official-strategy-desc">{description}</p>
          ) : null}
          <div className="official-strategy-meta">
            <span>{exchange || '-'}</span>
            <span className="official-strategy-separator">·</span>
            <span>{tradingPair || '-'}</span>
            <span className="official-strategy-separator">·</span>
            <span>{leverage}x 杠杆</span>
          </div>
        </div>
        <div className="official-strategy-actions">
          {onUse && (
            <button className="official-strategy-action-btn" type="button" onClick={onUse}>
              详情与运用
            </button>
          )}
        </div>
      </div>

      <div className="official-strategy-content">
        <div className="official-strategy-row">
          <div className="official-strategy-label">单次开仓</div>
          <div className="official-strategy-value">{singlePosition}</div>
        </div>
        <div className="official-strategy-row">
          <div className="official-strategy-label">持仓总量</div>
          <div className="official-strategy-value">{totalPosition}</div>
        </div>
        <div className="official-strategy-row">
          <div className="official-strategy-label">止盈止损比例</div>
          <div className="official-strategy-value">{profitLossRatio}</div>
        </div>
      </div>
    </div>
  );
};

export default OfficialStrategyItem;
