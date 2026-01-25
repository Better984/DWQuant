import React from 'react';
import arrowRise from '../assets/arrow-rise.svg';
import StrategyItem from './StrategyItem';
import type { StrategyItemProps } from './StrategyItem.types';
import AvatarByewind from '../assets/SnowUI/head/AvatarByewind.svg';
import AvatarFemale04 from '../assets/SnowUI/head/AvatarFemale04.svg';
import Avatar3d04 from '../assets/SnowUI/head/Avatar3d04.svg';
import AvatarAbstract04 from '../assets/SnowUI/head/AvatarAbstract04.svg';
import AvatarMale01 from '../assets/SnowUI/head/AvatarMale01.svg';

const HomeModule: React.FC = () => {
  const projects = [
    { name: '多维量化·现货网格', dueDate: '2026-03-01', status: '运行中', statusColor: 'purple', progress: '12 / 20', percentage: '60%', avatar: 'byewind', progressValue: 60 },
    { name: 'CTA 趋势策略', dueDate: '2026-05-15', status: '回测中', statusColor: 'blue', progress: '8 / 30', percentage: '27%', avatar: 'female04', progressValue: 27 },
    { name: '日内高频套利', dueDate: '2026-02-10', status: '待上线', statusColor: 'yellow', progress: '3 / 10', percentage: '30%', avatar: '3d04', progressValue: 30 },
    { name: '指数增强组合', dueDate: '2026-06-30', status: '运行中', statusColor: 'green', progress: '18 / 24', percentage: '75%', avatar: 'abstract04', progressValue: 75 },
  ];

  const strategies: StrategyItemProps[] = [
    {
      usId: 1,
      name: 'BTC/USDT 网格交易策略',
      currency: 'BTC',
      tradingPair: 'BTC/USDT',
      leverage: 3,
      singlePosition: '0.1 BTC',
      totalPosition: '2.5 BTC',
      profitLossRatio: '止盈 5% / 止损 3%',
      ownerAvatar: AvatarByewind,
      status: 'running',
      version: '1.2',
    },
    {
      usId: 2,
      name: 'ETH 趋势跟踪策略',
      currency: 'ETH',
      tradingPair: 'ETH/USDT',
      leverage: 5,
      singlePosition: '2 ETH',
      totalPosition: '15 ETH',
      profitLossRatio: '止盈 8% / 止损 4%',
      ownerAvatar: AvatarFemale04,
      status: 'running',
      version: '2.0',
    },
    {
      usId: 3,
      name: 'SOL 高频套利策略',
      currency: 'SOL',
      tradingPair: 'SOL/USDT',
      leverage: 10,
      singlePosition: '50 SOL',
      totalPosition: '300 SOL',
      profitLossRatio: '止盈 3% / 止损 2%',
      ownerAvatar: Avatar3d04,
      status: 'paused',
      version: '1.5',
    },
    {
      usId: 4,
      name: 'BNB 量化对冲策略',
      currency: 'BNB',
      tradingPair: 'BNB/USDT',
      leverage: 2,
      singlePosition: '10 BNB',
      totalPosition: '80 BNB',
      profitLossRatio: '止盈 6% / 止损 3.5%',
      ownerAvatar: AvatarAbstract04,
      status: 'running',
      version: '1.0',
    },
    {
      usId: 5,
      name: 'DOGE 波段交易策略',
      currency: 'DOGE',
      tradingPair: 'DOGE/USDT',
      leverage: 20,
      singlePosition: '10000 DOGE',
      totalPosition: '50000 DOGE',
      profitLossRatio: '止盈 10% / 止损 5%',
      ownerAvatar: AvatarMale01,
      status: 'stopped',
      version: '3.1',
    },
  ];

  return (
    <>
      {/* Page Title */}
      <div className="page-title">
        <h1 className="title-text">账户概览</h1>
        <span className="title-subtext">当前仅为演示数据，用于占位展示布局</span>
      </div>

      {/* Stats Cards */}
      <div className="stats-cards-container">
        <div className="stat-card stat-card-blue">
          <div className="stat-card-header">
            <span className="stat-card-title">当前运行策略</span>
            <div className="stat-card-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                <path d="M5 19.5C5 18.6716 5.67157 18 6.5 18H17.5C18.3284 18 19 18.6716 19 19.5C19 20.3284 18.3284 21 17.5 21H6.5C5.67157 21 5 20.3284 5 19.5Z" fill="currentColor" />
                <path d="M4 5.5C4 4.67157 4.67157 4 5.5 4H18.5C19.3284 4 20 4.67157 20 5.5V16.5C20 17.3284 19.3284 18 18.5 18H5.5C4.67157 18 4 17.3284 4 16.5V5.5Z" fill="currentColor" opacity="0.08" />
              </svg>
            </div>
          </div>
          <div className="stat-card-content">
            <div className="stat-card-value">4</div>
            <div className="stat-card-change stat-card-change-positive">
              <img src={arrowRise} alt="rise" className="change-icon" />
              <span>+1 本周新增</span>
            </div>
          </div>
        </div>

        <div className="stat-card stat-card-purple">
          <div className="stat-card-header">
            <span className="stat-card-title">账户权益 (模拟)</span>
            <div className="stat-card-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                <circle cx="12" cy="12" r="10" fill="currentColor" opacity="0.08" />
                <path d="M12 6V18M8 10H16M8 14H16" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
              </svg>
            </div>
          </div>
          <div className="stat-card-content">
            <div className="stat-card-value">¥ 1,250,000</div>
            <div className="stat-card-change stat-card-change-positive">
              <img src={arrowRise} alt="rise" className="change-icon" />
              <span>本月 +3.42%</span>
            </div>
          </div>
        </div>

        <div className="stat-card stat-card-blue">
          <div className="stat-card-header">
            <span className="stat-card-title">风险暴露 (模拟)</span>
            <div className="stat-card-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                <circle cx="12" cy="7" r="4" fill="currentColor" opacity="0.08" />
                <circle cx="12" cy="17" r="4" fill="currentColor" opacity="0.08" />
                <path d="M8 17C8 14.7909 9.79086 13 12 13C14.2091 13 16 14.7909 16 17" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
              </svg>
            </div>
          </div>
          <div className="stat-card-content">
            <div className="stat-card-value">中等</div>
            <div className="stat-card-change stat-card-change-positive">
              <span>最大回撤（30天）：4.8%</span>
            </div>
          </div>
        </div>
      </div>

      {/* Projects Grid */}
      <div className="projects-grid">
        {projects.map((project, index) => (
          <div key={index} className="project-card">
            <div className="project-card-header">
              <div className="project-info">
                <h3 className="project-name">{project.name}</h3>
                <span className="project-due-date">预期结束时间: {project.dueDate}</span>
              </div>
              <div className="project-icon">
                <div className="project-icon-placeholder"></div>
              </div>
            </div>
            <div className="project-card-footer">
              <div className="project-assignee">
                <div className={`project-avatar project-avatar-${project.avatar}`}></div>
                <div className={`status-badge status-badge-${project.statusColor}`}>
                  <div className="status-dot"></div>
                  <span>{project.status}</span>
                </div>
              </div>
              <div className="project-progress-bar">
                <div className="progress-bar-fill" style={{ width: `${project.progressValue}%` }}></div>
              </div>
              <div className="project-progress-text">
                <span>{project.progress} 执行进度</span>
                <span>{project.percentage}</span>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Strategy List */}
      <div className="strategy-list-section">
        <div className="page-title" style={{ marginTop: '40px', marginBottom: '20px' }}>
          <h2 className="title-text">策略列表</h2>
        </div>
        <div className="strategy-list-container">
          {strategies.map((strategy, index) => (
            <StrategyItem key={index} {...strategy} />
          ))}
        </div>
      </div>
    </>
  );
};

export default HomeModule;

