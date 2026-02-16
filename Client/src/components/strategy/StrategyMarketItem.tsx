import React from 'react';
import './StrategyMarketItem.css';

export type StrategyMarketItemProps = {
  title: string;
  description: string;
  author?: string | null;
  exchange: string;
  symbol: string;
  leverage: number;
  updatedAt?: string;
};

const StrategyMarketItem: React.FC<StrategyMarketItemProps> = ({
  title,
  description,
  author,
  exchange,
  symbol,
  leverage,
  updatedAt,
}) => {
  return (
    <div className="strategy-market-item">
      <div className="strategy-market-header">
        <div className="strategy-market-title">{title}</div>
        <div className="strategy-market-meta">
          <span>{exchange || '-'}</span>
          <span className="strategy-market-sep">·</span>
          <span>{symbol || '-'}</span>
          <span className="strategy-market-sep">·</span>
          <span>{leverage}x 杠杆</span>
        </div>
      </div>
      {description ? <div className="strategy-market-desc">{description}</div> : null}
      <div className="strategy-market-footer">
        <span className="strategy-market-author">{author || '匿名用户'}</span>
        {updatedAt ? <span className="strategy-market-time">更新于 {updatedAt}</span> : null}
      </div>
    </div>
  );
};

export default StrategyMarketItem;
