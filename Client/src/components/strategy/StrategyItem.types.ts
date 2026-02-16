export type StrategyItemProps = {
  usId: number;
  name: string;
  currency: string;
  tradingPair: string;
  leverage: number;
  singlePosition: string;
  totalPosition: string;
  profitLossRatio: string;
  ownerAvatar: string;
  status: 'running' | 'paused' | 'paused_open_position' | 'completed' | 'error';
  version?: string | number;
  catalogTag?: 'official' | 'template' | 'both';
  onViewDetail?: (usId: number) => void;
};
