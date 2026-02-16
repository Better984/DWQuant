import React from 'react';
import Avatar, { type AvatarSize } from './Avatar';
import './PeopleList.css';

export interface PeopleListItem {
  id?: string;
  src?: string;
  alt?: string;
  name: string;
  description?: string;
  onClick?: () => void;
}

export interface PeopleListProps {
  items: PeopleListItem[];
  avatarSize?: AvatarSize;
  className?: string;
}

const PeopleList: React.FC<PeopleListProps> = ({
  items,
  avatarSize = 'medium',
  className = '',
}) => {
  const listClasses = [
    'ui-people-list',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div className={listClasses}>
      {items.map((item, index) => (
        <div
          key={item.id || index}
          className="ui-people-list__item"
          onClick={item.onClick}
          role={item.onClick ? 'button' : undefined}
          tabIndex={item.onClick ? 0 : undefined}
        >
          <Avatar
            src={item.src}
            alt={item.alt}
            name={item.name}
            size={avatarSize}
          />
          <div className="ui-people-list__content">
            <div className="ui-people-list__name">{item.name}</div>
            {item.description && (
              <div className="ui-people-list__description">{item.description}</div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};

export default PeopleList;
