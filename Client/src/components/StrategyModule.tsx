import React, { useMemo, useState } from 'react';
import './StrategyModule.css';

import StarIcon from '../assets/SnowUI/icon/Star.svg';
import StorefrontIcon from '../assets/SnowUI/icon/Storefront.svg';
import GridFourIcon from '../assets/SnowUI/icon/GridFour.svg';
import PlusIcon from '../assets/SnowUI/icon/Plus.svg';
import StrategyEditorFlow, { type StrategyEditorSubmitPayload } from './StrategyEditorFlow';
import StrategyTemplateOptions from './StrategyTemplateOptions';
import type { MenuItem } from './StrategyModule.types';
import { HttpClient, getToken } from '../network';

const StrategyModule: React.FC = () => {
  const [activeMenuId, setActiveMenuId] = useState<string>('create');
  const [isStrategyEditorOpen, setIsStrategyEditorOpen] = useState(false);
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  const menuItems: MenuItem[] = [
    { id: 'official', label: '官方策略', icon: StarIcon },
    { id: 'market', label: '策略市场', icon: StorefrontIcon },
    { id: 'template', label: '策略模板', icon: GridFourIcon },
    { id: 'create', label: '创建策略', icon: PlusIcon },
  ];

  const openStrategyEditor = () => {
    setIsStrategyEditorOpen(true);
  };

  const closeStrategyEditor = () => {
    setIsStrategyEditorOpen(false);
  };

  const handleCreateStrategy = async (payload: StrategyEditorSubmitPayload) => {
    await client.post('/api/strategy/create', {
      name: payload.name,
      description: payload.description,
      aliasName: payload.name,
      configJson: payload.configJson,
    });
  };

  return (
    <div className="strategy-module-container">
      {/* 左侧导航栏 */}
      <aside className="strategy-sidebar">
        <div className="strategy-menu-group">
          {menuItems.map((item) => (
            <button
              key={item.id}
              className={`strategy-menu-item ${activeMenuId === item.id ? 'active' : ''}`}
              onClick={() => setActiveMenuId(item.id)}
            >
              <div className="strategy-menu-icon">
                <img src={item.icon} alt={item.label} />
              </div>
              <span className="strategy-menu-text">{item.label}</span>
            </button>
          ))}
        </div>
      </aside>

      {/* 右侧内容区域 */}
      <main className="strategy-content">
        <div className="strategy-content-inner">
          {activeMenuId === 'create' && !isStrategyEditorOpen && (
            <StrategyTemplateOptions onCustomCreate={openStrategyEditor} />
          )}
          {activeMenuId === 'create' && isStrategyEditorOpen && (
            <StrategyEditorFlow
              onClose={closeStrategyEditor}
              onSubmit={handleCreateStrategy}
              submitLabel="创建策略"
              successMessage="策略创建成功"
              errorMessage="创建失败，请稍后重试"
            />
          )}
        </div>
      </main>
    </div>
  );
};

export default StrategyModule;
