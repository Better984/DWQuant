import React from 'react';
import arrowRise from '../assets/arrow-rise.svg';

const HomeModule: React.FC = () => {
  const projects = [
    { name: '多维量化·现货网格', dueDate: '2026-03-01', status: '运行中', statusColor: 'purple', progress: '12 / 20', percentage: '60%', avatar: 'byewind', progressValue: 60 },
    { name: 'CTA 趋势策略', dueDate: '2026-05-15', status: '回测中', statusColor: 'blue', progress: '8 / 30', percentage: '27%', avatar: 'female04', progressValue: 27 },
    { name: '日内高频套利', dueDate: '2026-02-10', status: '待上线', statusColor: 'yellow', progress: '3 / 10', percentage: '30%', avatar: '3d04', progressValue: 30 },
    { name: '指数增强组合', dueDate: '2026-06-30', status: '运行中', statusColor: 'green', progress: '18 / 24', percentage: '75%', avatar: 'abstract04', progressValue: 75 },
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
    </>
  );
};

export default HomeModule;

