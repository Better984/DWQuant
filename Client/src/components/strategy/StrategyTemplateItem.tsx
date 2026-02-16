import React from 'react';
import './StrategyTemplateItem.css';

export type StrategyTemplateItemProps = {
  name: string;
  description: string;
  meta?: string;
  onUse?: () => void;
};

const StrategyTemplateItem: React.FC<StrategyTemplateItemProps> = ({ name, description, meta, onUse }) => {
  return (
    <div className="template-strategy-item">
      <div className="template-strategy-main">
        <div className="template-strategy-info">
          <h3 className="template-strategy-name">{name}</h3>
          {description ? <p className="template-strategy-desc">{description}</p> : null}
          {meta ? <div className="template-strategy-meta">{meta}</div> : null}
        </div>
        <button className="template-strategy-action" type="button" onClick={onUse}>
          运用
        </button>
      </div>
    </div>
  );
};

export default StrategyTemplateItem;
