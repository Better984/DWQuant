import React from 'react';

import type {
  StrategyRuntimeConfig,
  StrategyRuntimeTemplateConfig,
  StrategyTradeConfig,
  TimeframeOption,
  TradeOption,
} from './StrategyModule.types';
import StrategyRuntimeConfigForm from './StrategyRuntimeConfigForm';

interface TradeConfigFormProps {
  configStep: number;
  tradeConfig: StrategyTradeConfig;
  runtimeConfig: StrategyRuntimeConfig;
  runtimeTemplateOptions: StrategyRuntimeTemplateConfig[];
  runtimeTimezoneOptions: { value: string; label: string }[];
  onRuntimeConfigChange: (config: StrategyRuntimeConfig) => void;
  strategyName: string;
  strategyDescription: string;
  exchangeOptions: TradeOption[];
  exchangeApiKeyOptions: { id: number; label: string }[];
  selectedExchangeApiKeyId: number | null;
  selectedExchangeApiKeyLabel: string | null;
  showExchangeApiKeySelector: boolean;
  onExchangeApiKeySelect: (id: number) => void;
  onExchangeApiKeyBack: () => void;
  symbolOptions: TradeOption[];
  positionModeOptions: TradeOption[];
  timeframeOptions: TimeframeOption[];
  leverageOptions: number[];
  onStrategyNameChange: (value: string) => void;
  onStrategyDescriptionChange: (value: string) => void;
  onExchangeChange: (value: string) => void;
  onSymbolChange: (value: string) => void;
  onPositionModeChange: (value: string) => void;
  onTimeframeChange: (value: number) => void;
  updateTradeSizing: (key: keyof StrategyTradeConfig['sizing'], value: number) => void;
  updateTradeRisk: (key: keyof StrategyTradeConfig['risk'], value: number) => void;
  updateTrailingRisk: (
    key: keyof StrategyTradeConfig['risk']['trailing'],
    value: number | boolean,
  ) => void;
  tradeConfigRef: React.RefObject<HTMLDivElement>;
  disableMetaFields?: boolean;
}

interface TradeOptionGridProps {
  options: TradeOption[];
  selectedValue: string;
  onSelect: (value: string) => void;
}

const TradeOptionGrid: React.FC<TradeOptionGridProps> = ({ options, selectedValue, onSelect }) => {
  return (
    <div className="trade-option-grid">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className={`trade-option-card ${selectedValue === option.value ? 'active' : ''}`}
          onClick={() => onSelect(option.value)}
        >
          {option.icon ? (
            <img className="trade-option-icon" src={option.icon} alt={option.label} />
          ) : (
            <div className="trade-option-icon-text">{option.label}</div>
          )}
          <span>{option.label}</span>
        </button>
      ))}
    </div>
  );
};

const TradeConfigForm: React.FC<TradeConfigFormProps> = ({
  configStep,
  tradeConfig,
  runtimeConfig,
  runtimeTemplateOptions,
  runtimeTimezoneOptions,
  onRuntimeConfigChange,
  strategyName,
  strategyDescription,
  exchangeOptions,
  symbolOptions,
  positionModeOptions,
  timeframeOptions,
  leverageOptions,
  onStrategyNameChange,
  onStrategyDescriptionChange,
  onExchangeChange,
  onSymbolChange,
  onPositionModeChange,
  onTimeframeChange,
  updateTradeSizing,
  updateTradeRisk,
  updateTrailingRisk,
  tradeConfigRef,
  disableMetaFields = false,
  exchangeApiKeyOptions,
  selectedExchangeApiKeyId,
  selectedExchangeApiKeyLabel,
  showExchangeApiKeySelector,
  onExchangeApiKeySelect,
  onExchangeApiKeyBack,
}) => {
  return (
    <div className={`strategy-config-trade strategy-config-trade-step-${configStep}`}>
      <div className="strategy-config-trade-title">交易规则</div>
      <div className="strategy-config-trade-scroll" ref={tradeConfigRef}>
        {configStep === 0 ? (
        <>
          <div className="trade-form-section">
            <div className="trade-form-label">策略名称</div>
            <input
              className="trade-input trade-input-full"
              type="text"
              placeholder="请输入策略名称"
              value={strategyName}
              onChange={(e) => onStrategyNameChange(e.target.value)}
              disabled={disableMetaFields}
            />
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">策略描述</div>
            <textarea
              className="trade-textarea"
              placeholder="请输入策略描述"
              value={strategyDescription}
              onChange={(e) => onStrategyDescriptionChange(e.target.value)}
              rows={4}
              disabled={disableMetaFields}
            />
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">目标交易所</div>
            {showExchangeApiKeySelector ? (
              <div className="trade-api-key-panel">
                <button
                  type="button"
                  className="trade-api-key-back"
                  onClick={onExchangeApiKeyBack}
                >
                  返回选择交易所
                </button>
                <div className="trade-api-key-list">
                  {exchangeApiKeyOptions.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      className={`trade-api-key-item ${selectedExchangeApiKeyId === item.id ? 'active' : ''}`}
                      onClick={() => onExchangeApiKeySelect(item.id)}
                    >
                      {item.label || '未命名API'}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <TradeOptionGrid
                options={exchangeOptions}
                selectedValue={tradeConfig.exchange}
                onSelect={onExchangeChange}
              />
            )}
            {selectedExchangeApiKeyLabel && (
              <div className="trade-api-key-summary">已选择API：{selectedExchangeApiKeyLabel}</div>
            )}
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">交易对</div>
            <TradeOptionGrid
              options={symbolOptions}
              selectedValue={tradeConfig.symbol}
              onSelect={onSymbolChange}
            />
          </div>
        </>
      ) : (
        <>
          <div className="trade-form-section">
            <div className="trade-form-label">策略运行时间</div>
            <StrategyRuntimeConfigForm
              config={runtimeConfig}
              templateOptions={runtimeTemplateOptions}
              timezoneOptions={runtimeTimezoneOptions}
              onChange={onRuntimeConfigChange}
            />
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">交易周期</div>
            <div className="trade-chip-group">
              {timeframeOptions.map((option) => (
                <button
                  key={option.label}
                  type="button"
                  className={`trade-chip ${tradeConfig.timeframeSec === option.value ? 'active' : ''}`}
                  onClick={() => onTimeframeChange(option.value)}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>
          <div className="trade-form-row">
            <div className="trade-form-field">
              <div className="trade-form-label">单次开仓数量</div>
              <div className="trade-input-group">
                <input
                  className="trade-input"
                  type="number"
                  min={0}
                  step="0.0001"
                  value={tradeConfig.sizing.orderQty}
                  onChange={(event) => updateTradeSizing('orderQty', Number(event.target.value) || 0)}
                />
                <span className="trade-input-suffix">张</span>
              </div>
            </div>
            <div className="trade-form-field">
              <div className="trade-form-label">最大持仓数量</div>
              <div className="trade-input-group">
                <input
                  className="trade-input"
                  type="number"
                  min={0}
                  step="0.0001"
                  value={tradeConfig.sizing.maxPositionQty}
                  onChange={(event) =>
                    updateTradeSizing('maxPositionQty', Number(event.target.value) || 0)
                  }
                />
                <span className="trade-input-suffix">张</span>
              </div>
            </div>
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">杠杆</div>
            <div className="trade-chip-group">
              {leverageOptions.map((value) => (
                <button
                  key={value}
                  type="button"
                  className={`trade-chip ${tradeConfig.sizing.leverage === value ? 'active' : ''}`}
                  onClick={() => updateTradeSizing('leverage', value)}
                >
                  {value}x
                </button>
              ))}
            </div>
          </div>
          <div className="trade-form-section">
            <div className="trade-form-label">仓位模式</div>
            <div className="trade-chip-group">
              {positionModeOptions.map((option) => (
                <button
                  key={option.value}
                  type="button"
                  className={`trade-chip ${tradeConfig.positionMode === option.value ? 'active' : ''}`}
                  onClick={() => onPositionModeChange(option.value)}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>
          <div className="trade-form-row">
            <div className="trade-form-field">
              <div className="trade-form-label">止盈比例</div>
              <div className="trade-input-group">
                <input
                  className="trade-input"
                  type="number"
                  min={0}
                  step="0.1"
                  value={tradeConfig.risk.takeProfitPct * 100}
                  onChange={(event) =>
                    updateTradeRisk('takeProfitPct', (Number(event.target.value) || 0) / 100)
                  }
                />
                <span className="trade-input-suffix">%</span>
              </div>
            </div>
            <div className="trade-form-field">
              <div className="trade-form-label">止损比例</div>
              <div className="trade-input-group">
                <input
                  className="trade-input"
                  type="number"
                  min={0}
                  step="0.1"
                  value={tradeConfig.risk.stopLossPct * 100}
                  onChange={(event) =>
                    updateTradeRisk('stopLossPct', (Number(event.target.value) || 0) / 100)
                  }
                />
                <span className="trade-input-suffix">%</span>
              </div>
            </div>
          </div>
          <div className="trade-form-section">
            <label className="trade-toggle">
              <input
                type="checkbox"
                checked={tradeConfig.risk.trailing.enabled}
                onChange={() => updateTrailingRisk('enabled', !tradeConfig.risk.trailing.enabled)}
              />
              <span className="trade-toggle-indicator" />
              <span className="trade-toggle-label">启用移动止盈止损</span>
            </label>
            {tradeConfig.risk.trailing.enabled && (
              <div className="trade-trailing-panel">
                <div className="trade-form-row">
                  <div className="trade-form-field">
                    <div className="trade-form-label">触发收益阈值</div>
                    <div className="trade-input-group">
                      <input
                        className="trade-input"
                        type="number"
                        min={0}
                        step="0.1"
                        value={tradeConfig.risk.trailing.activationProfitPct * 100}
                        onChange={(event) =>
                          updateTrailingRisk(
                            'activationProfitPct',
                            (Number(event.target.value) || 0) / 100,
                          )
                        }
                      />
                      <span className="trade-input-suffix">%</span>
                    </div>
                  </div>
                  <div className="trade-form-field">
                    <div className="trade-form-label">回撤触发比例</div>
                    <div className="trade-input-group">
                      <input
                        className="trade-input"
                        type="number"
                        min={0}
                        step="0.1"
                        value={tradeConfig.risk.trailing.closeOnDrawdownPct * 100}
                        onChange={(event) =>
                          updateTrailingRisk(
                            'closeOnDrawdownPct',
                            (Number(event.target.value) || 0) / 100,
                          )
                        }
                      />
                      <span className="trade-input-suffix">%</span>
                    </div>
                  </div>
                </div>
                <div className="trade-form-hint">触发后按回撤比例自动止盈。</div>
              </div>
            )}
          </div>
        </>
      )}
      </div>
    </div>
  );
};

export default TradeConfigForm;
