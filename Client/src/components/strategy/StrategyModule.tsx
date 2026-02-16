import React, { useEffect, useMemo, useState } from 'react';
import './StrategyModule.css';

import StarIcon from '../../assets/icons/icon/Star.svg';
import StorefrontIcon from '../../assets/icons/icon/Storefront.svg';
import GridFourIcon from '../../assets/icons/icon/GridFour.svg';
import PlusIcon from '../../assets/icons/icon/Plus.svg';
import FunnelIcon from '../../assets/icons/icon/Funnel.svg';
import StrategyEditorFlow, { type StrategyEditorSubmitPayload } from './StrategyEditorFlow';
import StrategyTemplateOptions from './StrategyTemplateOptions';
import OfficialStrategyList from './OfficialStrategyList';
import TemplateStrategyList from './TemplateStrategyList';
import StrategyMarketList from './StrategyMarketList';
import StrategyRecommend from './StrategyRecommend';
import type { MenuItem } from './StrategyModule.types';
import { HttpClient, getToken } from '../../network/index.ts';

type StrategyModuleProps = {
  initialMenuId?: string;
};

const StrategyModule: React.FC<StrategyModuleProps> = ({ initialMenuId }) => {
  const [activeMenuId, setActiveMenuId] = useState<string>(initialMenuId ?? 'recommend');
  const [isStrategyEditorOpen, setIsStrategyEditorOpen] = useState(false);
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  useEffect(() => {
    if (initialMenuId) {
      setActiveMenuId(initialMenuId);
    }
  }, [initialMenuId]);

  const menuItems: MenuItem[] = [
    { id: 'recommend', label: '推荐', icon: StarIcon },
    { id: 'official', label: '官方策略', icon: StarIcon },
    { id: 'market', label: '策略广场', icon: StorefrontIcon },
    { id: 'template', label: '策略模板', icon: GridFourIcon },
    { id: 'filter-template', label: '筛选器模板', icon: FunnelIcon },
    { id: 'create', label: '创建策略', icon: PlusIcon },
  ];

  const openStrategyEditor = () => {
    setIsStrategyEditorOpen(true);
  };

  const closeStrategyEditor = () => {
    setIsStrategyEditorOpen(false);
  };

  const handleCreateStrategy = async (payload: StrategyEditorSubmitPayload) => {
    await client.postProtocol('/api/strategy/create', 'strategy.create', {
      name: payload.name,
      description: payload.description,
      aliasName: payload.name,
      configJson: payload.configJson,
      exchangeApiKeyId: payload.exchangeApiKeyId,
    });
  };

  return (
    <div className="strategy-module-container">
      {/* 顶部导航栏 - 根据Figma设计一比一复刻 */}
      <div className="strategy-top-tab">
        {/* 左侧标签页组 */}
        <div className="strategy-tab-group">
          {menuItems.map((item) => (
            <button
              key={item.id}
              className={`strategy-tab-item ${activeMenuId === item.id ? 'active' : ''}`}
              onClick={() => setActiveMenuId(item.id)}
            >
              <span className="strategy-tab-text">{item.label}</span>
              {activeMenuId === item.id && <div className="strategy-tab-line" />}
            </button>
          ))}
        </div>

        {/* 右侧筛选器 */}
        <div className="strategy-filter-group">
          <button className="strategy-filter-button" type="button" aria-label="筛选">
            <img src={FunnelIcon} alt="筛选" className="strategy-filter-icon" />
            <span className="strategy-filter-text">筛选</span>
          </button>
        </div>
      </div>

      {/* 内容区域 */}
      <main className="strategy-content">
        <div className="strategy-content-inner">
          {activeMenuId === 'recommend' && <StrategyRecommend />}
          {activeMenuId === 'official' && <OfficialStrategyList />}
          {activeMenuId === 'market' && <StrategyMarketList />}
          {activeMenuId === 'template' && <TemplateStrategyList />}
          {activeMenuId === 'filter-template' && (
            <div style={{ padding: '20px', color: 'rgba(0, 0, 0, 0.4)' }}>
              筛选器模板内容区域（待实现）
            </div>
          )}
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


