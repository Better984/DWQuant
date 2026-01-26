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
  status: 'running' | 'stopped' | 'paused' | 'error';
  version?: string | number;
  onViewDetail?: (usId: number) => void;
};
