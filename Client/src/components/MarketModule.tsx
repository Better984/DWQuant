import React from 'react';
import MarketChart from './MarketChart';

type MarketModuleProps = {
  chartSymbol?: string;
};

const MarketModule: React.FC<MarketModuleProps> = ({ chartSymbol }) => {
  return (
    <div className="module-container">
      <MarketChart height={420} symbol={chartSymbol} />
    </div>
  );
};

export default MarketModule;

