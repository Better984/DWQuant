import React from 'react';

const IndicatorModule: React.FC = () => {
  const indicators = [
    { name: 'MACD', category: '趋势', description: '示例：用于观察价格中长期趋势变化。' },
    { name: 'RSI', category: '动量', description: '示例：用于衡量超买 / 超卖状态。' },
    { name: 'ATR', category: '波动率', description: '示例：用于评估标的波动强度。' },
    { name: '布林带', category: '通道', description: '示例：结合价格与波动的区间判断。' },
  ];

  return (
    <div className="module-container">
      <div className="page-title">
        <h1 className="title-text">指标中心</h1>
        <span className="title-subtext">这里展示一些常见技术指标的占位信息，不涉及真实计算</span>
      </div>
      <div className="module-grid">
        {indicators.map((i, index) => (
          <div key={index} className="module-card">
            <div className="module-card-header">
              <div className="module-card-title">{i.name}</div>
              <span className="module-tag">{i.category}</span>
            </div>
            <div className="module-card-body">
              <p className="module-card-text">{i.description}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default IndicatorModule;

