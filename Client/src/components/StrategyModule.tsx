import React from 'react';

const StrategyModule: React.FC = () => {
  const strategies = [
    { name: '震荡区间网格', type: '现货 · 网格', mode: '模拟', status: '运行中', cycle: '7x24', note: '用于演示的占位策略，不产生真实交易。' },
    { name: '期货对冲套利', type: '合约 · 套利', mode: '模拟', status: '待上线', cycle: '工作日', note: '示例：多交易所基差套利。' },
    { name: '指数增强 Alpha', type: '股票 · 指数增强', mode: '回测', status: '回测中', cycle: '日频', note: '示例：多因子选股 + 指数增强。' },
  ];

  return (
    <div className="module-container">
      <div className="page-title">
        <h1 className="title-text">策略管理</h1>
        <span className="title-subtext">下面内容均为示例，用来占位展示策略列表布局</span>
      </div>
      <div className="module-list">
        {strategies.map((s, index) => (
          <div key={index} className="module-card">
            <div className="module-card-header">
              <div>
                <div className="module-card-title">{s.name}</div>
                <div className="module-card-subtitle">
                  {s.type} · 运行周期：{s.cycle} · 模式：{s.mode}
                </div>
              </div>
              <div className="status-badge status-badge-blue">
                <div className="status-dot"></div>
                <span>{s.status}</span>
              </div>
            </div>
            <div className="module-card-body">
              <p className="module-card-text">{s.note}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default StrategyModule;

