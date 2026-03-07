import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import './UIComponentsTest.css';
import { Button, Slider, Notification, StatusBadge, SearchInput, TextInput, SelectCard, Dialog, Avatar, AvatarGroup, PeopleList, Select, SelectItem, useNotification } from './ui/index.ts';
import { FEATURED_UI_DEMO_SECTIONS, buildUiTestPath, getUiDemoSectionDomId } from './uiDemoSections';

const UIComponentsTest: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [snowSelectValue, setSnowSelectValue] = useState<string>('');
  const [snowSelectValue2, setSnowSelectValue2] = useState<string>('');
  const [progress, setProgress] = useState(45);
  const [alertOpen, setAlertOpen] = useState(false);
  const [alertType, setAlertType] = useState<'default' | 'danger' | 'custom'>('default');
  const { success, error } = useNotification();
  const currentSectionId = new URLSearchParams(location.search).get('section')?.trim() ?? '';

  useEffect(() => {
    if (!currentSectionId) {
      return;
    }

    const frameId = window.requestAnimationFrame(() => {
      document
        .getElementById(getUiDemoSectionDomId(currentSectionId))
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    return () => {
      window.cancelAnimationFrame(frameId);
    };
  }, [currentSectionId]);

  const handleSectionClick = (sectionId: string) => {
    if (currentSectionId === sectionId) {
      document
        .getElementById(getUiDemoSectionDomId(sectionId))
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      return;
    }

    navigate(buildUiTestPath(sectionId));
  };

  return (
    <div className="ui-test-page">
      <div className="ui-test-container">
        <div className="ui-test-header">
          <button className="back-button" onClick={() => navigate('/')}>
            ← 返回登录页
          </button>
          <h1>UI 组件测试</h1>
          <p className="subtitle">仅展示 SnowUI 风格组件</p>
        </div>

        <section className="ui-test-overview">
          <div className="ui-test-overview-card">
            <div className="ui-test-overview-eyebrow">SnowUI</div>
            <h2>仅保留 SnowUI 组件</h2>
            <p className="ui-test-overview-text">
              这个页面现在只保留 SnowUI 风格组件示范，所有明显不是 SnowUI 视觉语言的原生控件和资源展示区都已移除。
            </p>
            <p className="ui-test-overview-text">
              下面只提供 SnowUI 组件分区导航，点击后会滚动到对应示范区。
            </p>
            <div className="ui-test-nav-group">
              <div className="ui-test-nav-group-title">组件导航</div>
              <div className="ui-test-nav-grid">
                {FEATURED_UI_DEMO_SECTIONS.map((section) => (
                  <button
                    key={section.id}
                    type="button"
                    className={`ui-test-nav-button${currentSectionId === section.id ? ' is-active' : ''}`}
                    onClick={() => {
                      handleSectionClick(section.id);
                    }}
                    title={section.summary}
                  >
                    <span className="ui-test-nav-button-title">{section.navLabel}</span>
                    <span className="ui-test-nav-button-summary">{section.summary}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>
        </section>

        {/* 按钮组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('buttons')}>
          <h2>🔘 按钮组件 (SnowUI Buttons)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的按钮组件，完全符合 Figma 设计稿规范</p>
          
          {/* Large Size Buttons */}
          <div className="button-variant-group">
            <h3 className="variant-title">Large Size (大尺寸)</h3>
            <div className="button-showcase">
              <div className="button-showcase-row">
                <div className="button-showcase-item">
                  <h4>Borderless</h4>
                  <div className="button-showcase-buttons">
                    <Button size="large" style="borderless">Button</Button>
                    <Button size="large" style="borderless" disabled>Button</Button>
            </div>
            </div>
                <div className="button-showcase-item">
                  <h4>Gray</h4>
                  <div className="button-showcase-buttons">
                    <Button size="large" style="gray">Button</Button>
                    <Button size="large" style="gray" disabled>Button</Button>
            </div>
            </div>
                <div className="button-showcase-item">
                  <h4>Outline</h4>
                  <div className="button-showcase-buttons">
                    <Button size="large" style="outline">Button</Button>
                    <Button size="large" style="outline" disabled>Button</Button>
            </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Filled</h4>
                  <div className="button-showcase-buttons">
                    <Button size="large" style="filled">Button</Button>
                    <Button size="large" style="filled" disabled>Button</Button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Medium Size Buttons */}
          <div className="button-variant-group">
            <h3 className="variant-title">Medium Size (中尺寸)</h3>
            <div className="button-showcase">
              <div className="button-showcase-row">
                <div className="button-showcase-item">
                  <h4>Borderless</h4>
                  <div className="button-showcase-buttons">
                    <Button size="medium" style="borderless">Button</Button>
                    <Button size="medium" style="borderless" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Gray</h4>
                  <div className="button-showcase-buttons">
                    <Button size="medium" style="gray">Button</Button>
                    <Button size="medium" style="gray" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Outline</h4>
                  <div className="button-showcase-buttons">
                    <Button size="medium" style="outline">Button</Button>
                    <Button size="medium" style="outline" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Filled</h4>
                  <div className="button-showcase-buttons">
                    <Button size="medium" style="filled">Button</Button>
                    <Button size="medium" style="filled" disabled>Button</Button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Small Size Buttons */}
          <div className="button-variant-group">
            <h3 className="variant-title">Small Size (小尺寸)</h3>
            <div className="button-showcase">
              <div className="button-showcase-row">
                <div className="button-showcase-item">
                  <h4>Borderless</h4>
                  <div className="button-showcase-buttons">
                    <Button size="small" style="borderless">Button</Button>
                    <Button size="small" style="borderless" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Gray</h4>
                  <div className="button-showcase-buttons">
                    <Button size="small" style="gray">Button</Button>
                    <Button size="small" style="gray" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Outline</h4>
                  <div className="button-showcase-buttons">
                    <Button size="small" style="outline">Button</Button>
                    <Button size="small" style="outline" disabled>Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Filled</h4>
                  <div className="button-showcase-buttons">
                    <Button size="small" style="filled">Button</Button>
                    <Button size="small" style="filled" disabled>Button</Button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* All Sizes Comparison */}
          <div className="button-variant-group">
            <h3 className="variant-title">尺寸对比 (Size Comparison)</h3>
            <div className="button-showcase">
              <div className="button-showcase-row">
                <div className="button-showcase-item">
                  <h4>Filled Style</h4>
                  <div className="button-showcase-buttons button-showcase-buttons--vertical">
                    <Button size="large" style="filled">Large Button</Button>
                    <Button size="medium" style="filled">Medium Button</Button>
                    <Button size="small" style="filled">Small Button</Button>
                  </div>
                </div>
                <div className="button-showcase-item">
                  <h4>Outline Style</h4>
                  <div className="button-showcase-buttons button-showcase-buttons--vertical">
                    <Button size="large" style="outline">Large Button</Button>
                    <Button size="medium" style="outline">Medium Button</Button>
                    <Button size="small" style="outline">Small Button</Button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* Slider 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('slider')}>
          <h2>🎚️ 滑块组件 (SnowUI Slider)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的滑块组件，完全符合 Figma 设计稿规范</p>
          
          <div className="slider-variant-group">
            <h3 className="variant-title">不同进度状态</h3>
            <div className="slider-showcase">
              <div className="slider-showcase-item">
                <h4>0% (非激活)</h4>
                <Slider value={0} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>0% (激活)</h4>
                <Slider defaultValue={0} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>28% (非激活)</h4>
                <Slider value={28} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>28% (激活)</h4>
                <Slider defaultValue={28} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>74% (非激活)</h4>
                <Slider value={74} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>74% (激活)</h4>
                <Slider defaultValue={74} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>100% (非激活)</h4>
                <Slider value={100} label="Text" />
              </div>
              <div className="slider-showcase-item">
                <h4>100% (激活)</h4>
                <Slider defaultValue={100} label="Text" />
              </div>
            </div>
          </div>

          <div className="slider-variant-group">
            <h3 className="variant-title">交互式滑块</h3>
            <div className="slider-showcase">
              <div className="slider-showcase-item">
                <h4>可交互滑块</h4>
                <Slider 
                  defaultValue={progress} 
                  label="Text"
                  onChange={(value) => setProgress(value)}
                />
                <p className="slider-value-display">当前值: {progress}%</p>
              </div>
              <div className="slider-showcase-item">
                <h4>禁用状态</h4>
                <Slider value={50} label="Text" disabled />
              </div>
              <div className="slider-showcase-item">
                <h4>自定义范围 (0-200)</h4>
                <Slider defaultValue={100} min={0} max={200} label="Text" valueSuffix="" />
              </div>
              <div className="slider-showcase-item">
                <h4>无标签</h4>
                <Slider defaultValue={60} showValue={true} />
              </div>
            </div>
          </div>
        </section>

        {/* Notification 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('notification')}>
          <h2>🔔 通知组件 (SnowUI Notification)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的通知组件，完全符合 Figma 设计稿规范</p>
          
          <div className="notification-variant-group">
            <h3 className="variant-title">Toast Popup</h3>
            <div className="notification-toast-demo">
              <Button size="small" style="filled" onClick={() => success('Done')}>
                Show Success
              </Button>
              <Button size="small" style="filled" onClick={() => error('Something Wrong')}>
                Show Error
              </Button>
            </div>
          </div>

          
          <div className="notification-variant-group">
            <h3 className="variant-title">Large Size (大尺寸)</h3>
            <div className="notification-showcase">
              <div className="notification-showcase-item">
                <h4>Success - Default</h4>
                <Notification state="success" size="large" variant="default" message="Done" />
              </div>
              <div className="notification-showcase-item">
                <h4>Failure - Default</h4>
                <Notification state="failure" size="large" variant="default" message="Something Wrong" />
              </div>
              <div className="notification-showcase-item">
                <h4>Success - Glass</h4>
                <Notification state="success" size="large" variant="glass" message="Done" />
              </div>
              <div className="notification-showcase-item">
                <h4>Failure - Glass</h4>
                <Notification state="failure" size="large" variant="glass" message="Something Wrong" />
              </div>
            </div>
          </div>

          <div className="notification-variant-group">
            <h3 className="variant-title">Small Size (小尺寸)</h3>
            <div className="notification-showcase">
              <div className="notification-showcase-item">
                <h4>Success - Default</h4>
                <Notification state="success" size="small" variant="default" message="Done" />
              </div>
              <div className="notification-showcase-item">
                <h4>Failure - Default</h4>
                <Notification state="failure" size="small" variant="default" message="Something Wrong" />
              </div>
              <div className="notification-showcase-item">
                <h4>Success - Glass</h4>
                <Notification state="success" size="small" variant="glass" message="Done" />
              </div>
              <div className="notification-showcase-item">
                <h4>Failure - Glass</h4>
                <Notification state="failure" size="small" variant="glass" message="Something Wrong" />
              </div>
            </div>
          </div>

          <div className="notification-variant-group">
            <h3 className="variant-title">尺寸对比 (Size Comparison)</h3>
            <div className="notification-showcase">
              <div className="notification-showcase-item">
                <h4>Success State</h4>
                <div className="notification-showcase-buttons notification-showcase-buttons--vertical">
                  <Notification state="success" size="large" variant="default" message="Done" />
                  <Notification state="success" size="small" variant="default" message="Done" />
                </div>
              </div>
              <div className="notification-showcase-item">
                <h4>Failure State</h4>
                <div className="notification-showcase-buttons notification-showcase-buttons--vertical">
                  <Notification state="failure" size="large" variant="default" message="Something Wrong" />
                  <Notification state="failure" size="small" variant="default" message="Something Wrong" />
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* StatusBadge 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('status-badge')}>
          <h2>🏷️ 状态标签组件 (SnowUI StatusBadge)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的状态标签组件，完全符合 Figma 设计稿规范</p>
          
          <div className="badge-variant-group">
            <h3 className="variant-title">Small Size - Dot Variant (小尺寸 - 点样式)</h3>
            <div className="badge-showcase">
              <div className="badge-showcase-row">
                <div className="badge-showcase-item">
                  <h4>Purple</h4>
                  <StatusBadge color="purple" size="small" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Blue</h4>
                  <StatusBadge color="blue" size="small" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Yellow</h4>
                  <StatusBadge color="yellow" size="small" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Grey</h4>
                  <StatusBadge color="grey" size="small" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Green</h4>
                  <StatusBadge color="green" size="small" variant="dot" label="Label" />
                </div>
              </div>
            </div>
          </div>

          <div className="badge-variant-group">
            <h3 className="variant-title">Large Size - Dot Variant (大尺寸 - 点样式)</h3>
            <div className="badge-showcase">
              <div className="badge-showcase-row">
                <div className="badge-showcase-item">
                  <h4>Purple</h4>
                  <StatusBadge color="purple" size="large" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Blue</h4>
                  <StatusBadge color="blue" size="large" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Yellow</h4>
                  <StatusBadge color="yellow" size="large" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Grey</h4>
                  <StatusBadge color="grey" size="large" variant="dot" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Green</h4>
                  <StatusBadge color="green" size="large" variant="dot" label="Label" />
                </div>
              </div>
            </div>
          </div>

          <div className="badge-variant-group">
            <h3 className="variant-title">Small Size - Background Variant (小尺寸 - 背景样式)</h3>
            <div className="badge-showcase">
              <div className="badge-showcase-row">
                <div className="badge-showcase-item">
                  <h4>Purple</h4>
                  <StatusBadge color="purple" size="small" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Blue</h4>
                  <StatusBadge color="blue" size="small" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Yellow</h4>
                  <StatusBadge color="yellow" size="small" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Grey</h4>
                  <StatusBadge color="grey" size="small" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Green</h4>
                  <StatusBadge color="green" size="small" variant="background" label="Label" />
                </div>
              </div>
            </div>
          </div>

          <div className="badge-variant-group">
            <h3 className="variant-title">Large Size - Background Variant (大尺寸 - 背景样式)</h3>
            <div className="badge-showcase">
              <div className="badge-showcase-row">
                <div className="badge-showcase-item">
                  <h4>Purple</h4>
                  <StatusBadge color="purple" size="large" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Blue</h4>
                  <StatusBadge color="blue" size="large" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Yellow</h4>
                  <StatusBadge color="yellow" size="large" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Grey</h4>
                  <StatusBadge color="grey" size="large" variant="background" label="Label" />
                </div>
                <div className="badge-showcase-item">
                  <h4>Green</h4>
                  <StatusBadge color="green" size="large" variant="background" label="Label" />
                </div>
              </div>
            </div>
          </div>

          <div className="badge-variant-group">
            <h3 className="variant-title">尺寸对比 (Size Comparison)</h3>
            <div className="badge-showcase">
              <div className="badge-showcase-row">
                <div className="badge-showcase-item">
                  <h4>Purple - Dot</h4>
                  <div className="badge-showcase-buttons badge-showcase-buttons--vertical">
                    <StatusBadge color="purple" size="large" variant="dot" label="Label" />
                    <StatusBadge color="purple" size="small" variant="dot" label="Label" />
                  </div>
                </div>
                <div className="badge-showcase-item">
                  <h4>Purple - Background</h4>
                  <div className="badge-showcase-buttons badge-showcase-buttons--vertical">
                    <StatusBadge color="purple" size="large" variant="background" label="Label" />
                    <StatusBadge color="purple" size="small" variant="background" label="Label" />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* SearchInput 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('search-input')}>
          <h2>🔍 搜索输入框组件 (SnowUI SearchInput)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的搜索输入框组件，完全符合 Figma 设计稿规范</p>
          
          <div className="search-input-variant-group">
            <h3 className="variant-title">Gray Type (灰色类型)</h3>
            <div className="search-input-showcase">
              <div className="search-input-showcase-item">
                <h4>Default</h4>
                <SearchInput type="gray" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <SearchInput type="gray" disabled />
              </div>
              <div className="search-input-showcase-item">
                <h4>Hover</h4>
                <SearchInput type="gray" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Focus</h4>
                <SearchInput type="gray" autoFocus />
              </div>
            </div>
          </div>

          <div className="search-input-variant-group">
            <h3 className="variant-title">Glass Type (玻璃类型)</h3>
            <div className="search-input-showcase">
              <div className="search-input-showcase-item">
                <h4>Default</h4>
                <SearchInput type="glass" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <SearchInput type="glass" disabled />
              </div>
              <div className="search-input-showcase-item">
                <h4>Hover</h4>
                <SearchInput type="glass" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Focus</h4>
                <SearchInput type="glass" autoFocus />
              </div>
            </div>
          </div>

          <div className="search-input-variant-group">
            <h3 className="variant-title">Outline Type (轮廓类型)</h3>
            <div className="search-input-showcase">
              <div className="search-input-showcase-item">
                <h4>Default</h4>
                <SearchInput type="outline" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <SearchInput type="outline" disabled />
              </div>
              <div className="search-input-showcase-item">
                <h4>Hover</h4>
                <SearchInput type="outline" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Focus</h4>
                <SearchInput type="outline" autoFocus />
              </div>
            </div>
          </div>

          <div className="search-input-variant-group">
            <h3 className="variant-title">Typing Type (输入中类型)</h3>
            <div className="search-input-showcase">
              <div className="search-input-showcase-item">
                <h4>Default (有值)</h4>
                <SearchInput type="typing" defaultValue="Typing" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <SearchInput type="typing" defaultValue="Typing" disabled />
              </div>
              <div className="search-input-showcase-item">
                <h4>Hover (有值)</h4>
                <SearchInput type="typing" defaultValue="Typing" />
              </div>
              <div className="search-input-showcase-item">
                <h4>Focus (有值)</h4>
                <SearchInput type="typing" defaultValue="Typing" autoFocus />
              </div>
            </div>
          </div>

          <div className="search-input-variant-group">
            <h3 className="variant-title">Interactive Examples (交互示例)</h3>
            <div className="search-input-showcase">
              <div className="search-input-showcase-item">
                <h4>可交互搜索框 (Gray)</h4>
                <SearchInput
                  type="gray"
                  placeholder="搜索..."
                  onChange={(e) => console.log('Search:', e.target.value)}
                />
              </div>
              <div className="search-input-showcase-item">
                <h4>可交互搜索框 (Glass)</h4>
                <SearchInput
                  type="glass"
                  placeholder="搜索..."
                  onChange={(e) => console.log('Search:', e.target.value)}
                />
              </div>
              <div className="search-input-showcase-item">
                <h4>可交互搜索框 (Outline)</h4>
                <SearchInput
                  type="outline"
                  placeholder="搜索..."
                  onChange={(e) => console.log('Search:', e.target.value)}
                />
              </div>
              <div className="search-input-showcase-item">
                <h4>无快捷键提示</h4>
                <SearchInput
                  type="gray"
                  showShortcut={false}
                  placeholder="搜索..."
                />
              </div>
            </div>
          </div>
        </section>

        {/* TextInput 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('text-input')}>
          <h2>📝 文本输入框组件 (SnowUI TextInput)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的文本输入框组件，完全符合 Figma 设计稿规范</p>
          
          <div className="text-input-variant-group">
            <h3 className="variant-title">Single Row Type (单行类型)</h3>
            <div className="text-input-showcase">
              <div className="text-input-showcase-item">
                <h4>Default</h4>
                <TextInput type="single" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <TextInput type="single" placeholder="Text" disabled />
              </div>
              <div className="text-input-showcase-item">
                <h4>Hover</h4>
                <TextInput type="single" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Focus</h4>
                <TextInput type="single" placeholder="Text" autoFocus />
              </div>
            </div>
          </div>

          <div className="text-input-variant-group">
            <h3 className="variant-title">Textarea Type (多行类型)</h3>
            <div className="text-input-showcase">
              <div className="text-input-showcase-item">
                <h4>Default</h4>
                <TextInput type="textarea" placeholder="Text" maxLength={200} showCounter />
              </div>
              <div className="text-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <TextInput type="textarea" placeholder="Text" maxLength={200} showCounter disabled />
              </div>
              <div className="text-input-showcase-item">
                <h4>Hover</h4>
                <TextInput type="textarea" placeholder="Text" maxLength={200} showCounter />
              </div>
              <div className="text-input-showcase-item">
                <h4>Focus</h4>
                <TextInput type="textarea" placeholder="Text" maxLength={200} showCounter autoFocus />
              </div>
            </div>
          </div>

          <div className="text-input-variant-group">
            <h3 className="variant-title">With Label Horizontal (带标签 - 水平布局)</h3>
            <div className="text-input-showcase">
              <div className="text-input-showcase-item">
                <h4>Default</h4>
                <TextInput type="with-label-horizontal" label="Title" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <TextInput type="with-label-horizontal" label="Title" placeholder="Text" disabled />
              </div>
              <div className="text-input-showcase-item">
                <h4>Hover</h4>
                <TextInput type="with-label-horizontal" label="Title" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Focus</h4>
                <TextInput type="with-label-horizontal" label="Title" placeholder="Text" autoFocus />
              </div>
            </div>
          </div>

          <div className="text-input-variant-group">
            <h3 className="variant-title">With Label Vertical (带标签 - 垂直布局)</h3>
            <div className="text-input-showcase">
              <div className="text-input-showcase-item">
                <h4>Default</h4>
                <TextInput type="with-label-vertical" label="Title" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Static (Disabled)</h4>
                <TextInput type="with-label-vertical" label="Title" placeholder="Text" disabled />
              </div>
              <div className="text-input-showcase-item">
                <h4>Hover</h4>
                <TextInput type="with-label-vertical" label="Title" placeholder="Text" />
              </div>
              <div className="text-input-showcase-item">
                <h4>Focus</h4>
                <TextInput type="with-label-vertical" label="Title" placeholder="Text" autoFocus />
              </div>
            </div>
          </div>

          <div className="text-input-variant-group">
            <h3 className="variant-title">Interactive Examples (交互示例)</h3>
            <div className="text-input-showcase">
              <div className="text-input-showcase-item">
                <h4>可交互单行输入框</h4>
                <TextInput
                  type="single"
                  placeholder="请输入文本..."
                  onChange={(e) => console.log('Input:', e.target.value)}
                />
              </div>
              <div className="text-input-showcase-item">
                <h4>可交互多行输入框（带计数）</h4>
                <TextInput
                  type="textarea"
                  placeholder="请输入多行文本..."
                  maxLength={200}
                  showCounter
                  rows={4}
                  onChange={(e) => console.log('Textarea:', e.target.value)}
                />
              </div>
              <div className="text-input-showcase-item">
                <h4>带标签的水平布局</h4>
                <TextInput
                  type="with-label-horizontal"
                  label="用户名"
                  placeholder="请输入用户名"
                  onChange={(e) => console.log('Username:', e.target.value)}
                />
              </div>
              <div className="text-input-showcase-item">
                <h4>带标签的垂直布局</h4>
                <TextInput
                  type="with-label-vertical"
                  label="邮箱"
                  placeholder="请输入邮箱地址"
                  onChange={(e) => console.log('Email:', e.target.value)}
                />
              </div>
            </div>
          </div>
        </section>

        {/* SelectCard 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('select-card')}>
          <h2>🎯 选择卡片组件 (SnowUI SelectCard)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的选择卡片组件，完全符合 Figma 设计稿规范</p>
          
          <div className="select-card-variant-group">
            <h3 className="variant-title">Default State (默认状态)</h3>
            <div className="select-card-showcase">
              <div className="select-card-showcase-item">
                <h4>1 Item</h4>
                <SelectCard>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>2 Items</h4>
                <SelectCard>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>3 Items</h4>
                <SelectCard>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>4 Items</h4>
                <SelectCard>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
            </div>
          </div>

          <div className="select-card-variant-group">
            <h3 className="variant-title">Static State (禁用状态)</h3>
            <div className="select-card-showcase">
              <div className="select-card-showcase-item">
                <h4>1 Item</h4>
                <SelectCard disabled>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>2 Items</h4>
                <SelectCard disabled>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>3 Items</h4>
                <SelectCard disabled>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>4 Items</h4>
                <SelectCard disabled>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
            </div>
          </div>

          <div className="select-card-variant-group">
            <h3 className="variant-title">Hover State (悬停状态)</h3>
            <div className="select-card-showcase">
              <div className="select-card-showcase-item">
                <h4>1 Item</h4>
                <SelectCard state="hover">
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>2 Items</h4>
                <SelectCard state="hover">
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>3 Items</h4>
                <SelectCard state="hover">
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>4 Items</h4>
                <SelectCard state="hover">
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
            </div>
          </div>

          <div className="select-card-variant-group">
            <h3 className="variant-title">Selected State (选中状态)</h3>
            <div className="select-card-showcase">
              <div className="select-card-showcase-item">
                <h4>1 Item</h4>
                <SelectCard selected>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>2 Items</h4>
                <SelectCard selected>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>3 Items</h4>
                <SelectCard selected>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>4 Items</h4>
                <SelectCard selected>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                  <span>Text</span>
                </SelectCard>
              </div>
            </div>
          </div>

          <div className="select-card-variant-group">
            <h3 className="variant-title">Interactive Examples (交互示例)</h3>
            <div className="select-card-showcase">
              <div className="select-card-showcase-item">
                <h4>可交互卡片（单选）</h4>
                <SelectCard
                  selected={false}
                  onClick={() => console.log('Card clicked')}
                >
                  <span>选项 1</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>已选中卡片</h4>
                <SelectCard
                  selected={true}
                  onClick={() => console.log('Card clicked')}
                >
                  <span>选项 2</span>
                  <span>描述信息</span>
                </SelectCard>
              </div>
              <div className="select-card-showcase-item">
                <h4>多行内容卡片</h4>
                <SelectCard
                  selected={false}
                  onClick={() => console.log('Card clicked')}
                >
                  <span>标题</span>
                  <span>内容行 1</span>
                  <span>内容行 2</span>
                  <span>内容行 3</span>
                </SelectCard>
              </div>
            </div>
          </div>
        </section>

        {/* Avatar 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('avatar')}>
          <h2>👤 头像组件 (SnowUI Avatar)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的头像组件，完全符合 Figma 设计稿规范</p>
          
          <div className="avatar-variant-group">
            <h3 className="variant-title">Single Avatar (单个头像)</h3>
            <div className="avatar-showcase">
              <div className="avatar-showcase-item">
                <h4>Small (24px)</h4>
                <Avatar size="small" name="John Doe" />
              </div>
              <div className="avatar-showcase-item">
                <h4>Medium (64px)</h4>
                <Avatar size="medium" name="Jane Smith" />
              </div>
              <div className="avatar-showcase-item">
                <h4>Large (96px)</h4>
                <Avatar size="large" name="Bob Johnson" />
              </div>
              <div className="avatar-showcase-item">
                <h4>With Image</h4>
                <Avatar size="medium" src="https://i.pravatar.cc/150?img=1" name="User" />
              </div>
            </div>
          </div>

          <div className="avatar-variant-group">
            <h3 className="variant-title">Avatar Group (头像组)</h3>
            <div className="avatar-showcase">
              <div className="avatar-showcase-item">
                <h4>2 Avatars</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                  ]}
                  size="medium"
                />
              </div>
              <div className="avatar-showcase-item">
                <h4>3 Avatars</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                    { name: 'User 3' },
                  ]}
                  size="medium"
                />
              </div>
              <div className="avatar-showcase-item">
                <h4>4 Avatars</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                    { name: 'User 3' },
                    { name: 'User 4' },
                  ]}
                  size="medium"
                />
              </div>
              <div className="avatar-showcase-item">
                <h4>7 Avatars (with More)</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                    { name: 'User 3' },
                    { name: 'User 4' },
                    { name: 'User 5' },
                    { name: 'User 6' },
                    { name: 'User 7' },
                  ]}
                  size="small"
                  max={6}
                />
              </div>
              <div className="avatar-showcase-item">
                <h4>Small Size</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                    { name: 'User 3' },
                  ]}
                  size="small"
                />
              </div>
              <div className="avatar-showcase-item">
                <h4>Large Size</h4>
                <AvatarGroup
                  avatars={[
                    { name: 'User 1' },
                    { name: 'User 2' },
                    { name: 'User 3' },
                  ]}
                  size="large"
                />
              </div>
            </div>
          </div>

          <div className="avatar-variant-group">
            <h3 className="variant-title">People List (多人列表)</h3>
            <div className="people-list-showcase">
              <div className="people-list-showcase-item">
                <h4>Basic List</h4>
                <PeopleList
                  items={[
                    { name: 'John Doe', description: 'Software Engineer' },
                    { name: 'Jane Smith', description: 'Product Designer' },
                    { name: 'Bob Johnson', description: 'Marketing Manager' },
                  ]}
                  avatarSize="medium"
                />
              </div>
              <div className="people-list-showcase-item">
                <h4>With Click Handler</h4>
                <PeopleList
                  items={[
                    { name: 'Alice Brown', description: 'Frontend Developer', onClick: () => console.log('Clicked Alice') },
                    { name: 'Charlie Wilson', description: 'Backend Developer', onClick: () => console.log('Clicked Charlie') },
                    { name: 'Diana Prince', description: 'UI/UX Designer', onClick: () => console.log('Clicked Diana') },
                  ]}
                  avatarSize="medium"
                />
              </div>
              <div className="people-list-showcase-item">
                <h4>Small Avatars</h4>
                <PeopleList
                  items={[
                    { name: 'Emma Davis', description: 'Project Manager' },
                    { name: 'Frank Miller', description: 'Data Analyst' },
                  ]}
                  avatarSize="small"
                />
              </div>
            </div>
          </div>
        </section>

        {/* 弹窗组件 */}
        {/* Dialog 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('dialog')}>
          <h2>🔔 弹窗组件 (SnowUI Dialog)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的弹窗组件，完全符合 Figma 设计稿规范</p>
          <div className="dialog-group">
            <div className="dialog-demo">
              <h3>默认弹窗</h3>
              <button
                className="btn btn-primary"
                onClick={() => {
                  setAlertType('default');
                  setAlertOpen(true);
                }}
              >
                打开默认弹窗
              </button>
            </div>
            <div className="dialog-demo">
              <h3>危险操作弹窗</h3>
              <button
                className="btn btn-danger"
                onClick={() => {
                  setAlertType('danger');
                  setAlertOpen(true);
                }}
              >
                打开删除确认弹窗
              </button>
            </div>
            <div className="dialog-demo">
              <h3>自定义内容弹窗</h3>
              <button
                className="btn btn-outline"
                onClick={() => {
                  setAlertType('custom');
                  setAlertOpen(true);
                }}
              >
                打开自定义弹窗
              </button>
            </div>
          </div>
        </section>

        {/* Select 组件 - SnowUI Design System */}
        <section className="component-section" id={getUiDemoSectionDomId('select')}>
          <h2>📋 下拉选择框组件 (SnowUI Select)</h2>
          <p className="section-desc">基于 SnowUI 设计系统的下拉选择框组件，完全符合 Figma 设计稿规范</p>
          
          <div className="select-variant-group">
            <h3 className="variant-title">Basic Select (基础选择框)</h3>
            <div className="select-showcase">
              <div className="select-showcase-item">
                <h4>Default</h4>
                <Select
                  options={[
                    { value: 'option1', label: 'Option 1' },
                    { value: 'option2', label: 'Option 2' },
                    { value: 'option3', label: 'Option 3' },
                    { value: 'option4', label: 'Option 4' },
                  ]}
                  placeholder="Select an option..."
                  onChange={(value) => {
                    console.log('Selected:', value);
                    setSnowSelectValue(value);
                  }}
                />
              </div>
              <div className="select-showcase-item">
                <h4>With Default Value</h4>
                <Select
                  options={[
                    { value: 'apple', label: 'Apple' },
                    { value: 'banana', label: 'Banana' },
                    { value: 'orange', label: 'Orange' },
                    { value: 'grape', label: 'Grape' },
                  ]}
                  defaultValue="banana"
                  onChange={(value) => {
                    console.log('Selected:', value);
                    setSnowSelectValue2(value);
                  }}
                />
              </div>
              <div className="select-showcase-item">
                <h4>Disabled</h4>
                <Select
                  options={[
                    { value: 'option1', label: 'Option 1' },
                    { value: 'option2', label: 'Option 2' },
                  ]}
                  placeholder="Disabled select"
                  disabled={true}
                />
              </div>
              <div className="select-showcase-item">
                <h4>With Disabled Options</h4>
                <Select
                  options={[
                    { value: 'option1', label: 'Option 1' },
                    { value: 'option2', label: 'Option 2', disabled: true },
                    { value: 'option3', label: 'Option 3' },
                    { value: 'option4', label: 'Option 4', disabled: true },
                  ]}
                  placeholder="Select an option..."
                />
              </div>
            </div>
          </div>

          <div className="select-variant-group">
            <h3 className="variant-title">SelectItem (独立选择项)</h3>
            <div className="select-item-showcase">
              <div className="select-item-showcase-item">
                <h4>Default Item</h4>
                <SelectItem>Item 1</SelectItem>
              </div>
              <div className="select-item-showcase-item">
                <h4>Selected Item</h4>
                <SelectItem selected={true}>Selected Item</SelectItem>
              </div>
              <div className="select-item-showcase-item">
                <h4>Disabled Item</h4>
                <SelectItem disabled={true}>Disabled Item</SelectItem>
              </div>
              <div className="select-item-showcase-item">
                <h4>With Click Handler</h4>
                <SelectItem
                  onClick={() => {
                    console.log('Item clicked');
                    alert('Item clicked!');
                  }}
                >
                  Clickable Item
                </SelectItem>
              </div>
            </div>
          </div>

          <div className="select-variant-group">
            <h3 className="variant-title">Current Selection</h3>
            <div className="select-value-display">
              <p>Select 1 Value: <strong>{snowSelectValue || 'None'}</strong></p>
              <p>Select 2 Value: <strong>{snowSelectValue2 || 'None'}</strong></p>
            </div>
          </div>
        </section>
      </div>

      {/* Dialog 组件 - SnowUI Design System */}
      {alertType === 'default' && (
        <Dialog
          open={alertOpen}
          title="确认操作"
          cancelText="取消"
          confirmText="确认"
          onCancel={() => {
            console.log('取消');
            setAlertOpen(false);
          }}
          onConfirm={() => {
            console.log('确认');
            alert('操作已确认！');
            setAlertOpen(false);
          }}
          onClose={() => setAlertOpen(false)}
        >
          <p>您确定要执行此操作吗？</p>
          <p>此操作可能会影响您的数据</p>
        </Dialog>
      )}

      {alertType === 'danger' && (
        <Dialog
          open={alertOpen}
          title="Delete chat？"
          cancelText="Cancel"
          confirmText="Delete"
          onCancel={() => {
            console.log('取消删除');
            setAlertOpen(false);
          }}
          onConfirm={() => {
            console.log('确认删除');
            alert('聊天已删除！');
            setAlertOpen(false);
          }}
          onClose={() => setAlertOpen(false)}
        >
          <p>This will delete "Alright! Here's something fresh and fascinating..."</p>
          <p>Visit settings to delete any memories saved during this chat.</p>
        </Dialog>
      )}

      {alertType === 'custom' && (
        <Dialog
          open={alertOpen}
          title="保存更改？"
          cancelText="不保存"
          confirmText="保存"
          onCancel={() => {
            console.log('不保存');
            alert('更改未保存');
            setAlertOpen(false);
          }}
          onConfirm={() => {
            console.log('保存');
            alert('更改已保存！');
            setAlertOpen(false);
          }}
          onClose={() => setAlertOpen(false)}
        >
          <p>您有未保存的更改，是否要保存？</p>
          <p>如果不保存，您的更改将会丢失</p>
        </Dialog>
      )}
    </div>
  );
};

export default UIComponentsTest;

