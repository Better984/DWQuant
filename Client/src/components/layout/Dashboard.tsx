import React, { Suspense, lazy, useEffect, useMemo, useRef, useState } from 'react';
import logoImage from '../../assets/logo.png';
import SidebarIcon from '../../assets/icons/icon/Sidebar.svg';
import VectorIcon from '../../assets/icons/icon/Vector.svg';
import ClockCounterClockwiseIcon from '../../assets/icons/icon/ClockCounterClockwise.svg';
import BellIcon from '../../assets/icons/icon/Bell.svg';
import HouseIcon from '../../assets/icons/icon/House.svg';
import ChartLineUpIcon from '../../assets/icons/icon/ChartLineUp.svg';
import TargetIcon from '../../assets/icons/icon/Target.svg';
import GaugeIcon from '../../assets/icons/icon/Gauge.svg';
import CompassIcon from '../../assets/icons/icon/Compass.svg';
import PlanetIcon from '../../assets/icons/icon/Planet.svg';
import ChatDotsIcon from '../../assets/icons/icon/ChatDots.svg';
import UserCircleIcon from '../../assets/icons/icon/UserCircle.svg';
import AvatarByewind from '../../assets/icons/head/AvatarByewind.svg';
import CryptoMarketPanel from '../market/CryptoMarketPanel';
import WhatsOnRoadPanel from '../discover/WhatsOnRoadPanel';
import './Dashboard.css';
import { getAuthProfile } from '../../auth/profileStore.ts';
import { ensureWsConnected, getWsStatus, onWsStatusChange } from '../../network/index.ts';
import { useNotification } from '../ui/index.ts';

const HomeModule = lazy(() => import('../home/HomeModule'));
const MarketModule = lazy(() => import('../market/MarketModule'));
const StrategyModule = lazy(() => import('../strategy/StrategyModule'));
const IndicatorModule = lazy(() => import('../indicator/IndicatorModule'));
const DiscoverModule = lazy(() => import('../discover/DiscoverModule'));
const PlanetModule = lazy(() => import('../planet/PlanetModule'));
const ChatModule = lazy(() => import('../chat/ChatModule'));
const StrategyList = lazy(() => import('../strategy/StrategyList'));
const UserSettings = lazy(() => import('../user/UserSettings'));
const IndicatorGeneratorSelector = lazy(() => import('../indicator/IndicatorGeneratorSelector'));
const HistoricalDataCacheDialog = lazy(() => import('../dialogs/HistoricalDataCacheDialog'));

const menuBreadcrumbMap: { [key: number]: { first: string; second: string } } = {
  0: { first: '主页', second: '概览' },
  1: { first: '行情', second: '市场数据' },
  2: { first: '策略', second: '策略管理' },
  3: { first: '指标', second: '技术指标' },
  4: { first: '发现', second: '探索' },
  5: { first: '星球', second: '策略社区' },
  6: { first: '聊天', second: '消息' },
  7: { first: '我的', second: '个人中心' },
};

const Dashboard: React.FC = () => {
  const headerRef = useRef<HTMLElement>(null);
  const mainContentRef = useRef<HTMLElement>(null);
  const navRef = useRef<HTMLDivElement>(null);
  const menuItemRefs = useRef<Array<HTMLDivElement | null>>([]);
  const [activeMenuIndex, setActiveMenuIndex] = useState(0);
  const [activeBgStyle, setActiveBgStyle] = useState<React.CSSProperties>({
    opacity: 0,
  });
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);
  const [isRightSidebarCollapsed, setIsRightSidebarCollapsed] = useState(false);
  const [isDarkMode, setIsDarkMode] = useState(false);
  const [rightSidebarWidth, setRightSidebarWidth] = useState(280);
  const rightSidebarResizeRef = useRef<{ x: number; width: number } | null>(null);
  const rightSidebarResizeFrameRef = useRef<number | null>(null);
  const [userProfile] = useState(() => getAuthProfile());
  const userDisplayName = userProfile?.nickname || userProfile?.email || 'User';
  const userRoleLabel = (() => {
    switch (userProfile?.role) {
      case 255:
        return '超级管理';
      case 40:
        return '达人';
      case 20:
        return '会员';
      case 0:
      default:
        return '普通用户';
    }
  })();
  const [showUserSettings, setShowUserSettings] = useState(false);
  const [wsStatus, setWsStatus] = useState(getWsStatus());
  const wsNotifiedRef = useRef(false);
  const { success } = useNotification();
  const [selectedSymbol, setSelectedSymbol] = useState('BTC');
  const [showIndicatorGenerator, setShowIndicatorGenerator] = useState(false);
  const [showHistoricalCacheDialog, setShowHistoricalCacheDialog] = useState(false);

  // 从右侧行情面板跳转到左侧“行情”菜单
  const handleOpenMarketFromRightPanel = (symbol: string) => {
    setSelectedSymbol(symbol);
    setActiveMenuIndex(1);
    setBreadcrumbText(menuBreadcrumbMap[1]);
  };
  
  const [breadcrumbText, setBreadcrumbText] = useState(menuBreadcrumbMap[0]);
  const [strategyInitialMenuId, setStrategyInitialMenuId] = useState<string>('recommend');
  const [autoOpenStrategyImport, setAutoOpenStrategyImport] = useState(false);
  const [pendingIndicatorFocusId, setPendingIndicatorFocusId] = useState<string | null>(null);
  const [pendingNewsFocusId, setPendingNewsFocusId] = useState<string | null>(null);
  const rightSidebarMin = 220;
  const rightSidebarMax = 520;

  // 切换主题
  const toggleTheme = () => {
    setIsDarkMode((prev) => !prev);
  };

  const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

  const handleRightSidebarResizeStart = (event: React.PointerEvent<HTMLElement>) => {
    // 检查点击位置是否在左边缘8px范围内
    const target = event.currentTarget;
    const rect = target.getBoundingClientRect();
    const clickX = event.clientX - rect.left;
    
    // 只有点击左边缘8px范围内才触发拖拽
    if (clickX > 8) {
      return;
    }
    
    event.preventDefault();
    rightSidebarResizeRef.current = {
      x: event.clientX,
      width: rightSidebarWidth,
    };
    let nextWidthInFrame: number | null = null;

    const handlePointerMove = (moveEvent: PointerEvent) => {
      if (!rightSidebarResizeRef.current) {
        return;
      }

      const deltaX = moveEvent.clientX - rightSidebarResizeRef.current.x;
      const maxWidth = Math.max(rightSidebarMin, Math.min(rightSidebarMax, window.innerWidth - 200));
      const nextWidth = clamp(rightSidebarResizeRef.current.width - deltaX, rightSidebarMin, maxWidth);
      nextWidthInFrame = nextWidth;

      if (rightSidebarResizeFrameRef.current !== null) {
        return;
      }

      // 侧栏拖拽按帧更新，避免 pointermove 高频触发导致卡顿。
      rightSidebarResizeFrameRef.current = window.requestAnimationFrame(() => {
        rightSidebarResizeFrameRef.current = null;
        if (nextWidthInFrame !== null) {
          setRightSidebarWidth(nextWidthInFrame);
        }
      });
    };

    const handlePointerUp = () => {
      rightSidebarResizeRef.current = null;
      if (rightSidebarResizeFrameRef.current !== null) {
        window.cancelAnimationFrame(rightSidebarResizeFrameRef.current);
        rightSidebarResizeFrameRef.current = null;
      }
      if (nextWidthInFrame !== null) {
        setRightSidebarWidth(nextWidthInFrame);
      }
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  };

  // 应用主题到根元素
  useEffect(() => {
    const root = document.documentElement;
    if (isDarkMode) {
      root.classList.add('dark-theme');
    } else {
      root.classList.remove('dark-theme');
    }
  }, [isDarkMode]);

  useEffect(() => {
    const updateHeaderHeight = () => {
      if (headerRef.current && mainContentRef.current) {
        // Header 高度固定为 72px，但需要确保实际高度正确
        const headerHeight = 72;
        document.documentElement.style.setProperty('--header-height', `${headerHeight}px`);
        mainContentRef.current.style.top = `${headerHeight}px`;
      }
    };

    // 初始计算
    updateHeaderHeight();

    // 监听窗口大小变化
    window.addEventListener('resize', updateHeaderHeight);

    return () => {
      window.removeEventListener('resize', updateHeaderHeight);
    };
  }, []);

  useEffect(() => {
    const updateActiveBg = () => {
      const container = navRef.current;
      const activeItem = menuItemRefs.current[activeMenuIndex];
      if (!container || !activeItem) {
        return;
      }

      const containerRect = container.getBoundingClientRect();
      const itemRect = activeItem.getBoundingClientRect();
      const top = itemRect.top - containerRect.top;

      setActiveBgStyle({
        transform: `translateY(${top}px)`,
        height: `${itemRect.height}px`,
        opacity: 1,
      });
    };

    updateActiveBg();
    window.addEventListener('resize', updateActiveBg);

    return () => {
      window.removeEventListener('resize', updateActiveBg);
    };
  }, [activeMenuIndex, isSidebarCollapsed]);

  useEffect(() => {
    let mounted = true;
    const unsubscribe = onWsStatusChange((next) => {
      if (!mounted) {
        return;
      }
      setWsStatus(next);
      if (next === 'connected' && !wsNotifiedRef.current) {
        success('连接成功');
        wsNotifiedRef.current = true;
      }
    });

    ensureWsConnected().catch(() => {
      // Connection status handled via status indicator.
    });

    return () => {
      mounted = false;
      unsubscribe();
    };
  }, [success]);

  const renderMainContent = () => {
    switch (activeMenuIndex) {
      case 0:
        return (
          <HomeModule
            onCreateStrategy={() => {
              setStrategyInitialMenuId('create');
              setActiveMenuIndex(2);
              setBreadcrumbText(menuBreadcrumbMap[2]);
            }}
            onOpenStrategyList={() => {
              setActiveMenuIndex(7);
              setBreadcrumbText(menuBreadcrumbMap[7]);
            }}
            onImportShareCode={() => {
              setActiveMenuIndex(7);
              setBreadcrumbText(menuBreadcrumbMap[7]);
              setAutoOpenStrategyImport(true);
            }}
            onOpenMarket={() => {
              setActiveMenuIndex(1);
              setBreadcrumbText(menuBreadcrumbMap[1]);
            }}
            onOpenIndicatorDetail={(indicatorId: string) => {
              setActiveMenuIndex(3);
              setBreadcrumbText(menuBreadcrumbMap[3]);
              setPendingIndicatorFocusId(indicatorId);
            }}
            onOpenNewsDetail={(newsId: string) => {
              setActiveMenuIndex(4);
              setBreadcrumbText(menuBreadcrumbMap[4]);
              setPendingNewsFocusId(newsId);
            }}
          />
        );
      case 1:
        return <MarketModule chartSymbol={`Binance:${selectedSymbol}/USDT`} />;
      case 2:
        return <StrategyModule initialMenuId={strategyInitialMenuId} />;
      case 3:
        return (
          <IndicatorModule
            focusIndicatorId={pendingIndicatorFocusId ?? undefined}
            onFocusHandled={() => setPendingIndicatorFocusId(null)}
          />
        );
      case 4:
        return (
          <DiscoverModule
            focusNewsId={pendingNewsFocusId ?? undefined}
            onFocusHandled={() => setPendingNewsFocusId(null)}
          />
        );
      case 5:
        return <PlanetModule />;
      case 6:
        return <ChatModule />;
      case 7:
        return (
          <StrategyList
            autoOpenImport={autoOpenStrategyImport}
            onAutoOpenHandled={() => setAutoOpenStrategyImport(false)}
          />
        );
      default:
        return <HomeModule />;
    }
  };

  const mainContentClassName = useMemo(
    () => `dashboard-main-content ${activeMenuIndex === 1 ? 'is-market-module' : ''}`,
    [activeMenuIndex],
  );

  return (
    <div
      className={`dashboard-container ${isSidebarCollapsed ? 'sidebar-collapsed' : ''} ${isRightSidebarCollapsed ? 'right-sidebar-collapsed' : ''}`}
      style={{ ['--right-sidebar-width' as string]: `${isRightSidebarCollapsed ? 0 : rightSidebarWidth}px` }}
    >
      {/* Left Sidebar */}
      <aside className={`dashboard-left-sidebar ${isSidebarCollapsed ? 'is-collapsed' : ''}`}>
        {/* Top Section: Logo + Navigation */}
        <div className="sidebar-top-section">
          {/* Logo Section */}
          <div className="sidebar-logo-section">
          <div className="sidebar-logo-container">
            <div className="sidebar-logo-icon">
              <img src={logoImage} alt="Logo" className="logo-image" />
            </div>
            <div className="sidebar-logo-text">
              <span className="logo-text-content">多维量化</span>
            </div>
          </div>
        </div>

        {/* Navigation Menu */}
        <div ref={navRef} className="sidebar-navigation">
          <div className="sidebar-menu-active-bg" style={activeBgStyle}></div>
          <div
            ref={(el) => {
              menuItemRefs.current[0] = el;
            }}
            className={`sidebar-menu-item sidebar-menu-item-first ${activeMenuIndex === 0 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(0);
              setBreadcrumbText(menuBreadcrumbMap[0]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={HouseIcon} alt="主页" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">主页</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[1] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 1 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(1);
              setBreadcrumbText(menuBreadcrumbMap[1]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={ChartLineUpIcon} alt="行情" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">行情</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[2] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 2 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(2);
              setBreadcrumbText(menuBreadcrumbMap[2]);
              setStrategyInitialMenuId('recommend');
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={TargetIcon} alt="策略" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">策略</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[3] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 3 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(3);
              setBreadcrumbText(menuBreadcrumbMap[3]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={GaugeIcon} alt="指标" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">指标</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[4] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 4 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(4);
              setBreadcrumbText(menuBreadcrumbMap[4]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={CompassIcon} alt="发现" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">发现</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[5] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 5 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(5);
              setBreadcrumbText(menuBreadcrumbMap[5]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={PlanetIcon} alt="星球" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">星球</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[6] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 6 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(6);
              setBreadcrumbText(menuBreadcrumbMap[6]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={ChatDotsIcon} alt="聊天" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">聊天</span>
          </div>

          <div
            ref={(el) => {
              menuItemRefs.current[7] = el;
            }}
            className={`sidebar-menu-item ${activeMenuIndex === 7 ? 'active' : ''}`}
            onClick={() => {
              setActiveMenuIndex(7);
              setBreadcrumbText(menuBreadcrumbMap[7]);
            }}
          >
            <div className="sidebar-menu-icon">
              <img src={UserCircleIcon} alt="我的" className="sidebar-menu-icon-img" />
            </div>
            <span className="sidebar-menu-text">我的</span>
          </div>

          <div className="sidebar-test-section">
            <div className="sidebar-test-title">测试</div>
            <div className="sidebar-test-actions">
              <button
                type="button"
                className="sidebar-test-button"
                onClick={() => setShowIndicatorGenerator(true)}
              >
                指标创建器
              </button>
              <button
                type="button"
                className="sidebar-test-button"
                onClick={() => setShowHistoricalCacheDialog(true)}
              >
                回测区间
              </button>
            </div>
          </div>
        </div>
        </div>

        {/* User Profile Section */}
        <div className="sidebar-user-section">
          <div 
            className="sidebar-user-profile-new"
            onClick={() => setShowUserSettings(true)}
            style={{ cursor: 'pointer' }}
          >
            <div className="sidebar-user-avatar">
              <div className="user-avatar-bg">
                <img src={AvatarByewind} alt="Avatar" className="user-avatar-img" />
                <span
                  className={`connection-indicator ${wsStatus === 'connected' ? 'is-online' : 'is-offline'}`}
                  aria-label={wsStatus === 'connected' ? 'connected' : 'disconnected'}
                />
              </div>
            </div>
            <div className="sidebar-user-text">
              <span className="sidebar-user-name">{userDisplayName}</span>
              <span className="sidebar-user-plan">{userRoleLabel}</span>
            </div>
          </div>
        </div>
      </aside>

      {/* Header */}
      <header ref={headerRef} className="dashboard-header">
        <div className="header-left">
          <div className="header-icon-breadcrumb">
            <div className="header-icon-group">
              <button
                className="header-icon-button"
                data-cursor-element-id="cursor-el-83"
                onClick={() => setIsSidebarCollapsed((prev) => !prev)}
              >
                <img src={SidebarIcon} alt="Sidebar" className="header-icon-img" />
              </button>
            </div>
            <div className="breadcrumb-container">
              <button className="breadcrumb-button breadcrumb-button-first">{breadcrumbText.first}</button>
              <span className="breadcrumb-separator">/</span>
              <button className="breadcrumb-button breadcrumb-button-second">{breadcrumbText.second}</button>
            </div>
          </div>
        </div>
        <div className="header-right">
          <div className="search-container">
            <div className="search-input-wrapper">
              <div className="search-icon-text">
                <svg className="search-icon" width="16" height="16" viewBox="0 0 16 16" fill="none">
                  <path d="M7.33333 12.6667C10.2789 12.6667 12.6667 10.2789 12.6667 7.33333C12.6667 4.38781 10.2789 2 7.33333 2C4.38781 2 2 4.38781 2 7.33333C2 10.2789 4.38781 12.6667 7.33333 12.6667Z" stroke="currentColor" strokeWidth="1.33333" strokeLinecap="round" strokeLinejoin="round"/>
                  <path d="M14 14L11.1 11.1" stroke="currentColor" strokeWidth="1.33333" strokeLinecap="round" strokeLinejoin="round"/>
                </svg>
                <span className="search-placeholder">Search</span>
              </div>
              <div className="search-shortcut">/</div>
            </div>
          </div>
          <div className="header-icons-group">
            <button className="header-icon-button" onClick={toggleTheme}>
              <img src={VectorIcon} alt="Theme Toggle" className="header-icon-img" />
            </button>
            <button className="header-icon-button">
              <img src={ClockCounterClockwiseIcon} alt="Clock" className="header-icon-img" />
            </button>
            <button className="header-icon-button">
              <img src={BellIcon} alt="Bell" className="header-icon-img" />
            </button>
            <button 
              className="header-icon-button"
              onClick={() => setIsRightSidebarCollapsed((prev) => !prev)}
            >
              <img src={SidebarIcon} alt="Right Sidebar Toggle" className="header-icon-img" />
            </button>
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main ref={mainContentRef} className={mainContentClassName}>
        <Suspense fallback={<div className="dashboard-module-loading">模块加载中...</div>}>
          {renderMainContent()}
        </Suspense>
      </main>

      {/* Right Sidebar */}
      <aside 
        className={`dashboard-right-sidebar ${isRightSidebarCollapsed ? 'is-collapsed' : ''}`}
        onPointerDown={handleRightSidebarResizeStart}
      >
        <CryptoMarketPanel
          allowResize
          allowWidthResize={false}
          allowHeightResize={true}
          selectedSymbol={selectedSymbol}
          onSelectSymbol={handleOpenMarketFromRightPanel}
        />
        <WhatsOnRoadPanel
          allowResize
          allowWidthResize={false}
          allowHeightResize={false}
        />
      </aside>

      {/* User Settings Modal */}
      {showUserSettings && (
        <Suspense fallback={null}>
          <UserSettings onClose={() => setShowUserSettings(false)} />
        </Suspense>
      )}

      <Suspense fallback={null}>
        <IndicatorGeneratorSelector
          open={showIndicatorGenerator}
          onClose={() => setShowIndicatorGenerator(false)}
        />
      </Suspense>

      <Suspense fallback={null}>
        <HistoricalDataCacheDialog
          open={showHistoricalCacheDialog}
          onClose={() => setShowHistoricalCacheDialog(false)}
        />
      </Suspense>
    </div>
  );
};

export default Dashboard;
