import React from 'react';

import type { ConditionSummarySection, StrategyTradeConfig, TimeframeOption, TradeOption } from './StrategyModule.types';
import { Dialog } from './ui';
import TradeConfigForm from './TradeConfigForm';

interface StrategyConfigDialogProps {
  open: boolean;
  onClose: () => void;
  configStep: number;
  onNextStep: () => void;
  onPrevStep: () => void;
  onConfirmGenerate: () => void;
  isLogicPreviewVisible: boolean;
  onToggleLogicPreview: () => void;
  logicPreview: string;
  confirmLabel?: string;
  isSubmitting?: boolean;
  disableMetaFields?: boolean;
  usedIndicatorOutputs: string[];
  conditionSummarySections: ConditionSummarySection[];
  summaryListRef: React.RefObject<HTMLDivElement>;
  summaryTrackRef: React.RefObject<HTMLDivElement>;
  summaryThumbRef: React.RefObject<HTMLDivElement>;
  codeListRef: React.RefObject<HTMLPreElement>;
  codeTrackRef: React.RefObject<HTMLDivElement>;
  codeThumbRef: React.RefObject<HTMLDivElement>;
  tradeConfigRef: React.RefObject<HTMLDivElement>;
  tradeConfig: StrategyTradeConfig;
  strategyName: string;
  strategyDescription: string;
  exchangeOptions: TradeOption[];
  symbolOptions: TradeOption[];
  positionModeOptions: TradeOption[];
  timeframeOptions: TimeframeOption[];
  leverageOptions: number[];
  exchangeApiKeyOptions: { id: number; label: string }[];
  selectedExchangeApiKeyId: number | null;
  selectedExchangeApiKeyLabel: string | null;
  showExchangeApiKeySelector: boolean;
  onExchangeApiKeySelect: (id: number) => void;
  onExchangeApiKeyBack: () => void;
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
}

const StrategyConfigDialog: React.FC<StrategyConfigDialogProps> = ({
  open,
  onClose,
  configStep,
  onNextStep,
  onPrevStep,
  onConfirmGenerate,
  isLogicPreviewVisible,
  onToggleLogicPreview,
  logicPreview,
  confirmLabel = '复制配置',
  isSubmitting = false,
  disableMetaFields = false,
  usedIndicatorOutputs,
  conditionSummarySections,
  summaryListRef,
  summaryTrackRef,
  summaryThumbRef,
  codeListRef,
  codeTrackRef,
  codeThumbRef,
  tradeConfigRef,
  tradeConfig,
  strategyName,
  strategyDescription,
  exchangeOptions,
  symbolOptions,
  positionModeOptions,
  timeframeOptions,
  leverageOptions,
  exchangeApiKeyOptions,
  selectedExchangeApiKeyId,
  selectedExchangeApiKeyLabel,
  showExchangeApiKeySelector,
  onExchangeApiKeySelect,
  onExchangeApiKeyBack,
  onStrategyNameChange,
  onStrategyDescriptionChange,
  onExchangeChange,
  onSymbolChange,
  onPositionModeChange,
  onTimeframeChange,
  updateTradeSizing,
  updateTradeRisk,
  updateTrailingRisk,
}) => {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="生成策略配置"
      cancelText={configStep === 1 ? undefined : '取消'}
      confirmText={configStep === 1 ? confirmLabel : undefined}
      onConfirm={configStep === 1 ? onConfirmGenerate : undefined}
      onCancel={configStep === 0 ? onClose : undefined}
      className="strategy-config-dialog"
      footer={
        <div className="strategy-config-footer">
          {configStep === 0 ? (
            <>
              <button
                className="snowui-dialog__button snowui-dialog__button--cancel"
                onClick={onClose}
              >
                取消
              </button>
              <button
                className="snowui-dialog__button snowui-dialog__button--confirm"
                onClick={onNextStep}
              >
                下一步
              </button>
            </>
          ) : (
            <>
              <button
                className="snowui-dialog__button snowui-dialog__button--cancel"
                onClick={onPrevStep}
              >
                上一步
              </button>
              <button
                className="snowui-dialog__button snowui-dialog__button--confirm"
                onClick={onConfirmGenerate}
                disabled={isSubmitting}
              >
                {isSubmitting ? '处理中...' : confirmLabel}
              </button>
            </>
          )}
        </div>
      }
    >
      <div className={`strategy-config-dialog-body strategy-config-step-${configStep}`}>
        <div className="strategy-config-progress">
          <div className="strategy-config-progress-item">
            <div className={`strategy-config-progress-dot ${configStep >= 0 ? 'active' : ''}`} />
            <div className={`strategy-config-progress-line ${configStep >= 1 ? 'active' : ''}`} />
          </div>
          <div className="strategy-config-progress-item">
            <div className={`strategy-config-progress-dot ${configStep >= 1 ? 'active' : ''}`} />
          </div>
        </div>
        <div className="strategy-config-preview">
          <div className="strategy-config-preview-header">
            <div className="strategy-config-preview-title">
              {isLogicPreviewVisible ? '逻辑配置' : '条件概览'}
            </div>
            <button type="button" className="strategy-config-toggle" onClick={onToggleLogicPreview}>
              {isLogicPreviewVisible ? '查看概览' : '查看 JSON'}
            </button>
          </div>
          {isLogicPreviewVisible ? (
            <div className="strategy-config-code-wrapper">
              <pre className="strategy-config-code" ref={codeListRef}>
                {logicPreview}
              </pre>
              <div className="strategy-config-scrollbar" ref={codeTrackRef}>
                <div className="strategy-config-scrollbar-thumb" ref={codeThumbRef} />
              </div>
            </div>
          ) : (
            <>
              <div className="strategy-config-indicator-summary">
                <div className="strategy-config-indicator-title">当前参与指标数量：{usedIndicatorOutputs.length}
                </div>
                {usedIndicatorOutputs.length === 0 ? (
                  <div className="strategy-config-indicator-empty">暂无参与指标</div>
                ) : (
                  <div className="strategy-config-indicator-list">
                    {usedIndicatorOutputs.map((label) => (
                      <div key={label} className="strategy-config-indicator-item">
                        {label}
                      </div>
                    ))}
                  </div>
                )}
              </div>
              <div className="strategy-config-summary-wrapper">
                <div className="strategy-config-summary" ref={summaryListRef}>
                  {conditionSummarySections.map((section) => (
                    <div key={section.title} className="strategy-config-summary-section">
                      <div className="strategy-config-summary-title">{section.title}</div>
                      {section.groups.map((group) => (
                        <div
                          key={`${section.title}-${group.title}`}
                          className="strategy-config-summary-group"
                        >
                          <div className="strategy-config-summary-group-title">{group.title}</div>
                          {group.conditions.length > 0 && (
                            <div className="strategy-config-summary-list">
                              {group.conditions.map((line, index) => (
                                <div
                                  key={`${group.title}-${index}`}
                                  className="strategy-config-summary-item"
                                >
                                  {line}
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
                <div className="strategy-config-scrollbar" ref={summaryTrackRef}>
                  <div className="strategy-config-scrollbar-thumb" ref={summaryThumbRef} />
                </div>
              </div>
            </>
          )}
        </div>
        <TradeConfigForm
          configStep={configStep}
          tradeConfig={tradeConfig}
          strategyName={strategyName}
          strategyDescription={strategyDescription}
          exchangeOptions={exchangeOptions}
          exchangeApiKeyOptions={exchangeApiKeyOptions}
          selectedExchangeApiKeyId={selectedExchangeApiKeyId}
          selectedExchangeApiKeyLabel={selectedExchangeApiKeyLabel}
          showExchangeApiKeySelector={showExchangeApiKeySelector}
          onExchangeApiKeySelect={onExchangeApiKeySelect}
          onExchangeApiKeyBack={onExchangeApiKeyBack}
          symbolOptions={symbolOptions}
          positionModeOptions={positionModeOptions}
          timeframeOptions={timeframeOptions}
          leverageOptions={leverageOptions}
          onStrategyNameChange={onStrategyNameChange}
          onStrategyDescriptionChange={onStrategyDescriptionChange}
          onExchangeChange={onExchangeChange}
          onSymbolChange={onSymbolChange}
          onPositionModeChange={onPositionModeChange}
          onTimeframeChange={onTimeframeChange}
          updateTradeSizing={updateTradeSizing}
          updateTradeRisk={updateTradeRisk}
          updateTrailingRisk={updateTrailingRisk}
          tradeConfigRef={tradeConfigRef}
          disableMetaFields={disableMetaFields}
        />
      </div>
    </Dialog>
  );
};

export default StrategyConfigDialog;
