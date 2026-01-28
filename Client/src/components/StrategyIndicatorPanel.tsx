import React from 'react';

import type { GeneratedIndicatorPayload } from './IndicatorGeneratorSelector';

interface StrategyIndicatorPanelProps {
  selectedIndicators: GeneratedIndicatorPayload[];
  onOpenIndicatorGenerator: () => void;
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string;
  formatIndicatorMeta: (indicator: GeneratedIndicatorPayload) => string;
  onEditIndicator: (indicatorId: string) => void;
  onRemoveIndicator: (indicatorId: string) => void;
}

const StrategyIndicatorPanel: React.FC<StrategyIndicatorPanelProps> = ({
  selectedIndicators,
  onOpenIndicatorGenerator,
  formatIndicatorName,
  formatIndicatorMeta,
  onEditIndicator,
  onRemoveIndicator,
}) => {
  return (
    <div className="strategy-indicator-panel">
      <div className="strategy-indicator-header">
        <div className="strategy-indicator-title">已选指标</div>
        <button className="strategy-indicator-add" onClick={onOpenIndicatorGenerator}>新建指标</button>
      </div>
      {selectedIndicators.length === 0 ? (
        <div className="strategy-indicator-empty">暂无参与指标</div>
      ) : (
        <div className="strategy-indicator-list">
          {selectedIndicators.map((indicator) => (
            <div key={indicator.id} className="strategy-indicator-item">
              <div className="strategy-indicator-info">
                <div className="strategy-indicator-name">{formatIndicatorName(indicator)}</div>
                <div className="strategy-indicator-meta">{formatIndicatorMeta(indicator)}</div>
              </div>
              <div className="strategy-indicator-actions">
                <button
                  className="strategy-indicator-action strategy-indicator-edit"
                  onClick={() => onEditIndicator(indicator.id)}
                  type="button"
                >
                  编辑
                </button>
                <button
                  className="strategy-indicator-action strategy-indicator-remove"
                  onClick={() => onRemoveIndicator(indicator.id)}
                  type="button"
                >
                  移除
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default StrategyIndicatorPanel;
