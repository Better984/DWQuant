import React from 'react';
import MarketChart from './MarketChart';

type MarketModuleProps = {
  chartSymbol?: string;
};

const MarketModule: React.FC<MarketModuleProps> = ({ chartSymbol }) => {
  return (
    <div className="market-module">
      <MarketChart height="100%" symbol={chartSymbol} />
    </div>
  );
};

export default MarketModule;

