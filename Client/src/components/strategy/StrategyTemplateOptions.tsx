import React from 'react';

import FilePlusIcon from '../../assets/icons/icon/FilePlus.svg';
import FileTextIcon from '../../assets/icons/icon/FileText.svg';
import ShareNetworkIcon from '../../assets/icons/icon/ShareNetwork.svg';

interface StrategyTemplateOptionsProps {
  onCustomCreate: () => void;
}

const StrategyTemplateOptions: React.FC<StrategyTemplateOptionsProps> = ({ onCustomCreate }) => {
  return (
    <div className="strategy-template-options">
      <button className="strategy-option-button" onClick={onCustomCreate}>
        <div className="strategy-option-icon">
          <img src={FilePlusIcon} alt="自定义创建" />
        </div>
        <div className="strategy-option-content">
          <div className="strategy-option-title">自定义创建</div>
          <div className="strategy-option-desc">从零开始创建您的专属策略</div>
        </div>
      </button>
      <button className="strategy-option-button">
        <div className="strategy-option-icon">
          <img src={FileTextIcon} alt="模板创建" />
        </div>
        <div className="strategy-option-content">
          <div className="strategy-option-title">模板创建</div>
          <div className="strategy-option-desc">基于预设模板快速创建策略</div>
        </div>
      </button>
      <button className="strategy-option-button">
        <div className="strategy-option-icon">
          <img src={ShareNetworkIcon} alt="分享码导入" />
        </div>
        <div className="strategy-option-content">
          <div className="strategy-option-title">分享码导入</div>
          <div className="strategy-option-desc">通过分享码快速导入策略配置</div>
        </div>
      </button>
    </div>
  );
};

export default StrategyTemplateOptions;

