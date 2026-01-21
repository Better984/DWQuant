import React from 'react';

const DiscoverModule: React.FC = () => {
  const ideas = [
    { title: '量化选股 · 因子轮动', desc: '占位文案：根据不同市场环境动态切换主导因子组合。' },
    { title: '多品种套保组合', desc: '占位文案：通过指数期货 + 行业 ETF 构建对冲组合。' },
    { title: '情绪指标驱动的短线策略', desc: '占位文案：结合成交量、换手率和涨跌停统计做情绪分层。' },
  ];

  return (
    <div className="module-container">
      <div className="page-title">
        <h1 className="title-text">发现</h1>
        <span className="title-subtext">这里是想法 / 灵感占位区，用来展示不同策略思路</span>
      </div>
      <div className="module-list">
        {ideas.map((idea, index) => (
          <div key={index} className="module-card">
            <div className="module-card-header">
              <div className="module-card-title">{idea.title}</div>
            </div>
            <div className="module-card-body">
              <p className="module-card-text">{idea.desc}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default DiscoverModule;

