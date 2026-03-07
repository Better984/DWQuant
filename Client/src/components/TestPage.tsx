import React from 'react';
import { useNavigate } from 'react-router-dom';
import './TestPage.css';
import { FEATURED_UI_DEMO_SECTIONS, buildUiTestPath } from './uiDemoSections';

const TestPage: React.FC = () => {
  const navigate = useNavigate();

  const handleDashboardClick = () => {
    navigate('/dashboard');
  };

  const handleUIClick = () => {
    navigate('/ui-test');
  };

  const handleMantineClick = () => {
    navigate('/mantine-test');
  };

  const handleKlineChartsClick = () => {
    navigate('/klinecharts-demo');
  };

  const handleUiSectionClick = (sectionId: string) => {
    navigate(buildUiTestPath(sectionId));
  };

  return (
    <div className="test-page">
      {/* Main Content */}
      <div className="test-container">
        <div className="quick-actions">
          <div className="quick-actions-primary">
            <button type="button" className="quick-action-button">
              创建策略
            </button>
            <button type="button" className="quick-action-button" onClick={handleUIClick}>
              UI 组件总览
            </button>
            <button type="button" className="quick-action-button" onClick={handleMantineClick}>
              Mantine 组件库
            </button>
            <button type="button" className="quick-action-button" onClick={handleKlineChartsClick}>
              klinecharts示范
            </button>
            <button type="button" className="quick-action-button">
              HTTP / WebSocket 协议测试
            </button>
            <button type="button" className="quick-action-button" onClick={handleDashboardClick}>
              Dashboard 界面
            </button>
          </div>

          <div className="quick-actions-divider">
            <span className="quick-actions-divider-line" />
            <span className="quick-actions-divider-text">SnowUI 组件直达</span>
            <span className="quick-actions-divider-line" />
          </div>

          <p className="quick-actions-note">
            主入口现在保留库级测试页，下面的直达区仍然只保留 SnowUI 组件；Mantine 会在独立页面里单独展示。
          </p>

          <div className="quick-actions-sections">
            {FEATURED_UI_DEMO_SECTIONS.map((section) => (
              <button
                key={section.id}
                type="button"
                className="quick-action-button quick-action-button--section"
                onClick={() => {
                  handleUiSectionClick(section.id);
                }}
                title={section.summary}
              >
                <span className="quick-action-button-title">{section.homeLabel}</span>
                <span className="quick-action-button-subtitle">{section.summary}</span>
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};

export default TestPage;

