import React, { useState } from 'react';
import './StrategyModule.css';

// 导入图标
import PencilSimpleLineIcon from '../assets/SnowUI/icon/PencilSimpleLine.svg';
import TrayIcon from '../assets/SnowUI/icon/Tray.svg';
import PaperPlaneRightIcon from '../assets/SnowUI/icon/PaperPlaneRight.svg';
import FileTextIcon from '../assets/SnowUI/icon/FileText.svg';
import WarningOctagonIcon from '../assets/SnowUI/icon/WarningOctagon.svg';
import TrashIcon from '../assets/SnowUI/icon/Trash.svg';
import ArchiveIcon from '../assets/SnowUI/icon/Archive.svg';

interface MenuItem {
  id: string;
  label: string;
  icon: string;
}

const StrategyModule: React.FC = () => {
  const [activeMenuId, setActiveMenuId] = useState<string>('compose');

  const menuItems: MenuItem[] = [
    { id: 'compose', label: 'Compose', icon: PencilSimpleLineIcon },
    { id: 'inbox', label: 'Inbox', icon: TrayIcon },
    { id: 'sent', label: 'Sent', icon: PaperPlaneRightIcon },
    { id: 'draft', label: 'Draft', icon: FileTextIcon },
    { id: 'spam', label: 'Spam', icon: WarningOctagonIcon },
    { id: 'trash', label: 'Trash', icon: TrashIcon },
    { id: 'archive', label: 'Archive', icon: ArchiveIcon },
  ];

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
          {/* 这里可以放置策略列表等内容 */}
        </div>
      </main>
    </div>
  );
};

export default StrategyModule;
