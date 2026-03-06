import type { StrategyConfig } from './StrategyModule.types';

export type AiStrategySource = {
  conversationId?: number;
  conversationTitle?: string;
  messageId?: number;
  messageTime?: string;
  replyText?: string;
  strategyJson: string;
  strategyConfig: StrategyConfig;
};

export type AiStrategyEventDetail = {
  source: AiStrategySource;
};

export const AI_STRATEGY_OPEN_WORKBENCH_EVENT = 'strategy:ai-open-workbench';
export const AI_STRATEGY_OPEN_SAVE_EVENT = 'strategy:ai-open-save';

export function openStrategyWorkbenchFromAi(source: AiStrategySource): void {
  window.dispatchEvent(
    new CustomEvent<AiStrategyEventDetail>(AI_STRATEGY_OPEN_WORKBENCH_EVENT, {
      detail: { source },
    }),
  );
}

export function openStrategySaveDialogFromAi(source: AiStrategySource): void {
  window.dispatchEvent(
    new CustomEvent<AiStrategyEventDetail>(AI_STRATEGY_OPEN_SAVE_EVENT, {
      detail: { source },
    }),
  );
}
