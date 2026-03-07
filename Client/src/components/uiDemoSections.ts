export interface UiDemoSection {
  id: string;
  title: string;
  navLabel: string;
  homeLabel: string;
  summary: string;
}

export const UI_DEMO_SECTIONS: UiDemoSection[] = [
  {
    id: 'buttons',
    title: '🔘 按钮组件',
    navLabel: '按钮',
    homeLabel: 'Buttons',
    summary: '按钮尺寸、样式和禁用态示范。',
  },
  {
    id: 'slider',
    title: '🎚️ 滑块组件',
    navLabel: '滑块',
    homeLabel: 'Slider',
    summary: '滑块进度状态、交互和范围设置示范。',
  },
  {
    id: 'notification',
    title: '🔔 通知组件',
    navLabel: '通知',
    homeLabel: 'Notification',
    summary: '通知条和 Toast 触发示范。',
  },
  {
    id: 'status-badge',
    title: '🏷️ 状态标签组件',
    navLabel: '状态标签',
    homeLabel: 'StatusBadge',
    summary: '状态标签颜色、尺寸和样式示范。',
  },
  {
    id: 'search-input',
    title: '🔍 搜索输入框组件',
    navLabel: '搜索框',
    homeLabel: 'SearchInput',
    summary: '搜索输入框布局、状态和交互示范。',
  },
  {
    id: 'text-input',
    title: '📝 文本输入框组件',
    navLabel: '文本输入',
    homeLabel: 'TextInput',
    summary: '文本输入框布局、标签和状态示范。',
  },
  {
    id: 'select-card',
    title: '🎯 选择卡片组件',
    navLabel: '选择卡片',
    homeLabel: 'SelectCard',
    summary: '卡片式选项展示和状态切换示范。',
  },
  {
    id: 'avatar',
    title: '👤 头像组件',
    navLabel: '头像',
    homeLabel: 'Avatar',
    summary: '头像、头像组和多人列表示范。',
  },
  {
    id: 'dialog',
    title: '🔔 弹窗组件',
    navLabel: '弹窗',
    homeLabel: 'Dialog',
    summary: '确认、危险操作和自定义内容弹窗示范。',
  },
  {
    id: 'select',
    title: '📋 下拉选择框组件',
    navLabel: '下拉选择',
    homeLabel: 'Select',
    summary: 'Select 与 SelectItem 的状态和交互示范。',
  },
];

export const FEATURED_UI_DEMO_SECTIONS = UI_DEMO_SECTIONS;

export function buildUiTestPath(sectionId?: string): string {
  if (!sectionId) {
    return '/ui-test';
  }

  return `/ui-test?section=${encodeURIComponent(sectionId)}`;
}

export function getUiDemoSectionDomId(sectionId: string): string {
  return `ui-demo-${sectionId}`;
}
