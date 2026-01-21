import React from 'react';
import { useNavigate } from 'react-router-dom';
import './TestPage.css';

const TestPage: React.FC = () => {
  const navigate = useNavigate();

  const handleDashboardClick = () => {
    navigate('/dashboard');
  };

  const handleSnowUIClick = () => {
    navigate('/ui-components-test');
  };

  return (
    <div className="test-page">
      {/* Main Content */}
      <div className="test-container">
        <div className="quick-actions">
          <button type="button" className="quick-action-button">
            创建策略
          </button>
          <button type="button" className="quick-action-button" onClick={handleSnowUIClick}>
            SnowUI
          </button>
          <button type="button" className="quick-action-button">
            HTTP / WebSocket 协议测试
          </button>
          <button type="button" className="quick-action-button" onClick={handleDashboardClick}>
            Dashboard 界面
          </button>
        </div>
      </div>
    </div>
  );
};

export default TestPage;
