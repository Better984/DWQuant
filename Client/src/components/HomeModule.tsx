import React, { useEffect, useMemo, useRef, useState } from 'react';
import { HttpClient, HttpError, getToken } from '../network';
import adaIcon from '../assets/SnowUI/cryptoicon/ADA.svg';
import bnbIcon from '../assets/SnowUI/cryptoicon/BNB.svg';
import btcIcon from '../assets/SnowUI/cryptoicon/BTC.svg';
import dogeIcon from '../assets/SnowUI/cryptoicon/DOGE.svg';
import ethIcon from '../assets/SnowUI/cryptoicon/ETH.svg';
import solIcon from '../assets/SnowUI/cryptoicon/SOL.svg';
import usdcIcon from '../assets/SnowUI/cryptoicon/USDC.svg';
import usdtIcon from '../assets/SnowUI/cryptoicon/USDT.svg';
import xrpIcon from '../assets/SnowUI/cryptoicon/XRP.svg';
import './HomeModule.css';

type PositionRecentSummaryResponse = {
  queryAt: string;
  from: string;
  to: string;
  hasData: boolean;
  windowDays: number;
  candidateWindowDays: number[];
  openCount: number;
  closedCount: number;
  winCount: number;
  winRate: number | null;
  realizedPnl: number;
  currentOpenCount: number;
  currentFloatingPnl: number;
  floatingPriceHitCount: number;
  floatingPriceMissCount: number;
};

type PositionRecentSummaryRequest = {
  candidateWindowDays: number[];
};

type PositionRecentActivityItem = {
  eventType: string;
  eventAt: string;
  title: string;
  description: string;
  exchange?: string | null;
  symbol?: string | null;
  side?: string | null;
  positionId?: number | null;
  realizedPnl?: number | null;
  severity?: string | null;
};

type PositionRecentActivityResponse = {
  queryAt: string;
  from: string;
  to: string;
  limit: number;
  items: PositionRecentActivityItem[];
};

type PositionRecentActivityRequest = {
  to?: string;
  days: number;
  limit: number;
};

const SUMMARY_WINDOW_DAYS = [1, 3, 7, 30];
const RECENT_ACTIVITY_QUERY_DAYS = 7;
const RECENT_ACTIVITY_QUERY_LIMIT = 8;
type SummaryQueryMode = 'auto' | 'manual';

const CRYPTO_ICON_MAP: Record<string, string> = {
  ADA: adaIcon,
  BNB: bnbIcon,
  BTC: btcIcon,
  DOGE: dogeIcon,
  ETH: ethIcon,
  SOL: solIcon,
  USDC: usdcIcon,
  USDT: usdtIcon,
  XRP: xrpIcon,
};

type IndicatorPreview = {
  id: string;
  name: string;
  category: string;
  sample: string;
};

const HOME_INDICATOR_PREVIEWS: IndicatorPreview[] = [
  {
    id: 'fear-greed',
    name: '贪婪恐慌指数 (Fear & Greed Index)',
    category: '情绪',
    sample: '样例值：73（极度贪婪）',
  },
  {
    id: 'etf-flow',
    name: '比特币现货 ETF 净流入',
    category: '资金流向',
    sample: '样例：+1.20 亿 USD / 日',
  },
  {
    id: 'exchange-flow',
    name: '交易所资金净流入 / 流出',
    category: '资金流向',
    sample: '样例：-8,500 BTC / 24h',
  },
  {
    id: 'long-short',
    name: '多空持仓比 (Long / Short Ratio)',
    category: '合约杠杆',
    sample: '样例：1.35',
  },
  {
    id: 'funding-rate',
    name: '永续合约资金费率 (Funding Rate)',
    category: '合约杠杆',
    sample: '样例：+0.032% / 8h',
  },
  {
    id: 'open-interest',
    name: '未平仓合约总量 (Open Interest)',
    category: '合约杠杆',
    sample: '样例：15.3B USD',
  },
  {
    id: 'liquidations',
    name: '24 小时强平金额 (Liquidations)',
    category: '风险事件',
    sample: '样例：多头强平 3.1B / 空头 0.8B',
  },
  {
    id: 'stablecoin',
    name: '稳定币净流入与流通市值',
    category: '资金流向',
    sample: '样例：稳定币净流入 +650M USD / 流通市值 140B',
  },
];

const HomeIndicatorCarousel: React.FC<{
  onOpenIndicatorDetail?: (indicatorId: string) => void;
}> = ({ onOpenIndicatorDetail }) => {
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    if (HOME_INDICATOR_PREVIEWS.length <= 1) {
      return;
    }
    const intervalId = window.setInterval(() => {
      setActiveIndex((current) => (current + 1) % HOME_INDICATOR_PREVIEWS.length);
    }, 8000);
    return () => {
      window.clearInterval(intervalId);
    };
  }, []);

  const active = HOME_INDICATOR_PREVIEWS[activeIndex];

  return (
    <section className="home-module-card home-module-indicator-card">
      <div className="home-module-card-header">
        <h2 className="home-module-card-title">指标精选</h2>
        <span className="home-module-card-subtitle">从指标中心轮流精选重点指标，点击可查看完整图表</span>
      </div>
      <div className="home-module-indicator-body">
        <div className="home-module-indicator-meta">
          <div className="home-module-indicator-category">{active.category}</div>
          <div className="home-module-indicator-title">{active.name}</div>
          <div className="home-module-indicator-sample">{active.sample}</div>
        </div>
        <div className="home-module-indicator-actions">
          <button
            type="button"
            className="home-module-indicator-view-btn"
            onClick={() => onOpenIndicatorDetail?.(active.id)}
          >
            查看详情
          </button>
        </div>
      </div>
      <div className="home-module-indicator-pagination" aria-label="指标轮播进度">
        {HOME_INDICATOR_PREVIEWS.map((item, index) => (
          <span
            key={item.id}
            className={`home-module-indicator-dot ${index === activeIndex ? 'is-active' : ''}`}
          />
        ))}
      </div>
    </section>
  );
};

type NewsPreview = {
  id: string;
  title: string;
  source: string;
  time: string;
};

const HOME_NEWS_PREVIEWS: NewsPreview[] = [
  {
    id: 'backpack-ipo',
    time: '22:34',
    title: '代币经济学新范式？当 Backpack 开始让 VC「延迟满足」',
    source: 'BLOCKBEATS',
  },
  {
    id: 'jump-prediction',
    time: '22:30',
    title: '华尔街顶级量化机构 Jump Trading 杀入预测市场，散户时代结束了？',
    source: 'chaincatcher',
  },
  {
    id: 'megaeth-launch',
    time: '21:12',
    title: 'L2 疲软当头、Vitalik 转向悲观，MegaETH 此时上线胜算几何？',
    source: 'chaincatcher',
  },
  {
    id: 'rootdata-transparency',
    time: '21:08',
    title: 'RootData：2026 年 1 月加密交易所透明度研究报告',
    source: 'chaincatcher',
  },
  {
    id: 'ark-stablecoin',
    time: '21:00',
    title: 'ARK Invest：稳定币，下一代货币体系的基石？',
    source: 'ODAILY',
  },
  {
    id: 'openclaw-deploy',
    time: '20:00',
    title: 'OpenClaw 极简部署：最快 1 分钟搞定，纯小白友好教程',
    source: 'ODAILY',
  },
  {
    id: 'crypto-2002-analogy',
    time: '19:30',
    title: '黎明前的黑暗：2026 年的 Crypto = 2002 年的互联网',
    source: 'PANews',
  },
  {
    id: 'daily-intel-0210',
    time: '19:18',
    title: '2 月 10 日市场关键情报，你错过了多少？',
    source: 'BLOCKBEATS',
  },
  {
    id: 'kalshi-nba',
    time: '19:00',
    title: '字母哥入股 Kalshi：当 NBA 巨星成为「利益相关方」',
    source: 'ODAILY',
  },
  {
    id: 'bithumb-2000btc',
    time: '18:34',
    title: '2000 枚 BTC 的险情背后：CEX 账本的根本问题',
    source: 'ODAILY',
  },
];

const HomeNewsCarousel: React.FC<{
  onOpenNewsDetail?: (newsId: string) => void;
}> = ({ onOpenNewsDetail }) => {
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    if (HOME_NEWS_PREVIEWS.length <= 1) {
      return;
    }
    const intervalId = window.setInterval(() => {
      setActiveIndex((current) => (current + 1) % HOME_NEWS_PREVIEWS.length);
    }, 10000);
    return () => {
      window.clearInterval(intervalId);
    };
  }, []);

  const active = HOME_NEWS_PREVIEWS[activeIndex];

  return (
    <section className="home-module-card home-module-news-card">
      <div className="home-module-card-header">
        <h2 className="home-module-card-title">新闻精选</h2>
      </div>
      <div className="home-module-news-body">
        <div className="home-module-news-meta">
          <div className="home-module-news-time-source">
            <span className="home-module-news-time">{active.time}</span>
            <span className="home-module-news-source">{active.source}</span>
          </div>
          <div className="home-module-news-title">{active.title}</div>
        </div>
        <div className="home-module-news-actions">
          <button
            type="button"
            className="home-module-news-view-btn"
            onClick={() => onOpenNewsDetail?.(active.id)}
          >
            查看资讯
          </button>
        </div>
      </div>
      <div className="home-module-news-pagination" aria-label="新闻轮播进度">
        {HOME_NEWS_PREVIEWS.map((item, index) => (
          <span
            key={item.id}
            className={`home-module-news-dot ${index === activeIndex ? 'is-active' : ''}`}
          />
        ))}
      </div>
    </section>
  );
};
type HomeModuleProps = {
  onCreateStrategy?: () => void;
  onOpenStrategyList?: () => void;
  onImportShareCode?: () => void;
  onOpenMarket?: () => void;
  onOpenIndicatorDetail?: (indicatorId: string) => void;
   onOpenNewsDetail?: (newsId: string) => void;
};

const HomeModule: React.FC<HomeModuleProps> = ({
  onCreateStrategy,
  onOpenStrategyList,
  onImportShareCode,
  onOpenMarket,
  onOpenIndicatorDetail,
  onOpenNewsDetail,
}) => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [summary, setSummary] = useState<PositionRecentSummaryResponse | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(false);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [recentActivityList, setRecentActivityList] = useState<PositionRecentActivityItem[]>([]);
  const [activityLoading, setActivityLoading] = useState(false);
  const [activityError, setActivityError] = useState<string | null>(null);
  const [queryMode, setQueryMode] = useState<SummaryQueryMode>('auto');
  const [selectedWindowDays, setSelectedWindowDays] = useState<number>(1);
  const [summaryLeftHeight, setSummaryLeftHeight] = useState(0);
  const [isDesktopSummaryLayout, setIsDesktopSummaryLayout] = useState(() =>
    typeof window === 'undefined' ? true : window.matchMedia('(min-width: 1181px)').matches,
  );
  const mountedRef = useRef(true);
  const requestIdRef = useRef(0);
  const activityRequestIdRef = useRef(0);
  const summaryLeftRef = useRef<HTMLDivElement | null>(null);

  const loadRecentSummary = async (
    mode: SummaryQueryMode,
    options?: { selectedDays?: number; signal?: AbortSignal },
  ) => {
    const currentRequestId = ++requestIdRef.current;
    setSummaryLoading(true);
    setSummaryError(null);

    try {
      const selectedDays = options?.selectedDays;
      const payload: PositionRecentSummaryRequest = {
        candidateWindowDays: mode === 'manual' && selectedDays ? [selectedDays] : SUMMARY_WINDOW_DAYS,
      };
      const response = await client.postProtocol<PositionRecentSummaryResponse, PositionRecentSummaryRequest>(
        '/api/positions/recent-summary',
        'position.recent.summary',
        payload,
        { signal: options?.signal },
      );

      if (!mountedRef.current || currentRequestId !== requestIdRef.current) {
        return;
      }

      setSummary(response);
      setQueryMode(mode);
      setSelectedWindowDays(mode === 'manual' && selectedDays ? selectedDays : response.windowDays);
    } catch (error) {
      if (!mountedRef.current || currentRequestId !== requestIdRef.current) {
        return;
      }

      if (error instanceof HttpError) {
        setSummaryError(error.message || '近期总结加载失败');
      } else {
        setSummaryError('近期总结加载失败');
      }
    } finally {
      if (mountedRef.current && currentRequestId === requestIdRef.current) {
        setSummaryLoading(false);
      }
    }
  };

  const loadRecentActivity = async (options?: { days?: number; limit?: number; signal?: AbortSignal }) => {
    const currentRequestId = ++activityRequestIdRef.current;
    setActivityLoading(true);
    setActivityError(null);

    try {
      const payload: PositionRecentActivityRequest = {
        days: options?.days ?? RECENT_ACTIVITY_QUERY_DAYS,
        limit: options?.limit ?? RECENT_ACTIVITY_QUERY_LIMIT,
      };

      const response = await client.postProtocol<PositionRecentActivityResponse, PositionRecentActivityRequest>(
        '/api/positions/recent-activity',
        'position.recent.activity',
        payload,
        { signal: options?.signal },
      );

      if (!mountedRef.current || currentRequestId !== activityRequestIdRef.current) {
        return;
      }

      setRecentActivityList(response.items ?? []);
    } catch (error) {
      if (!mountedRef.current || currentRequestId !== activityRequestIdRef.current) {
        return;
      }

      if (error instanceof HttpError) {
        setActivityError(error.message || '最近操作日志加载失败');
      } else {
        setActivityError('最近操作日志加载失败');
      }
    } finally {
      if (mountedRef.current && currentRequestId === activityRequestIdRef.current) {
        setActivityLoading(false);
      }
    }
  };

  useEffect(() => {
    mountedRef.current = true;
    const controller = new AbortController();
    void loadRecentSummary('auto', { signal: controller.signal });
    void loadRecentActivity({
      days: RECENT_ACTIVITY_QUERY_DAYS,
      limit: RECENT_ACTIVITY_QUERY_LIMIT,
      signal: controller.signal,
    });
    return () => {
      mountedRef.current = false;
      controller.abort();
    };
  }, []);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    const mediaQuery = window.matchMedia('(min-width: 1181px)');
    const handleChange = (event: MediaQueryListEvent) => {
      setIsDesktopSummaryLayout(event.matches);
    };

    setIsDesktopSummaryLayout(mediaQuery.matches);
    mediaQuery.addEventListener('change', handleChange);
    return () => {
      mediaQuery.removeEventListener('change', handleChange);
    };
  }, []);

  useEffect(() => {
    const leftElement = summaryLeftRef.current;
    if (!leftElement) {
      return;
    }

    // 监听左栏高度，右栏高度始终对齐左栏，右栏内部再滚动显示更多日志。
    const updateHeight = () => {
      const nextHeight = Math.max(0, Math.round(leftElement.getBoundingClientRect().height));
      setSummaryLeftHeight((currentHeight) => (currentHeight === nextHeight ? currentHeight : nextHeight));
    };

    updateHeight();
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(() => {
      updateHeight();
    });
    observer.observe(leftElement);
    return () => {
      observer.disconnect();
    };
  }, [summary, summaryLoading, summaryError, activityLoading, recentActivityList.length]);

  const handleWindowClick = (days: number) => {
    if (summaryLoading) {
      return;
    }

    void loadRecentSummary('manual', { selectedDays: days });
  };

  const renderSummaryContent = () => {
    // 没有任何历史数据时，才展示整体的加载/错误/空状态
    if (!summary) {
      if (summaryLoading) {
        return (
          <div className="home-module-summary-body">
            <div className="home-module-summary-loading">正在加载近期总结...</div>
          </div>
        );
      }

      if (summaryError) {
        return (
          <div className="home-module-summary-body">
            <div className="home-module-summary-error">{summaryError}</div>
          </div>
        );
      }

      return (
        <div className="home-module-summary-body">
          <div className="home-module-summary-empty">暂无近期总结数据。</div>
        </div>
      );
    }

    // 已经有一次成功数据后，后续切换窗口只更新数值，不再出现额外提示行，避免 UI 跳动
    const noOpenRecord = !summary.hasData;
    const openCountText = noOpenRecord ? '**' : String(summary.openCount);
    const winRateText = noOpenRecord ? '**' : formatWinRate(summary.winRate, summary.closedCount);
    const realizedPnlText = noOpenRecord ? '**' : formatPnl(summary.realizedPnl);

    return (
      <div className="home-module-summary-body">
        <div className="home-module-stats-row home-module-summary-stats">
          <div className="home-module-stat">
            <span className="home-module-stat-label">开仓次数</span>
            <span className="home-module-stat-value">{openCountText}</span>
          </div>
          <div className="home-module-stat">
            <span className="home-module-stat-label">胜率</span>
            <span className="home-module-stat-value">{winRateText}</span>
          </div>
          <div className="home-module-stat">
            <span className="home-module-stat-label">已实现盈亏</span>
            <span className={`home-module-stat-value ${noOpenRecord ? '' : resolvePnlClass(summary.realizedPnl)}`}>
              {realizedPnlText}
            </span>
          </div>
          <div className="home-module-stat">
            <span className="home-module-stat-label">当前浮动盈亏</span>
            <span className={`home-module-stat-value ${resolvePnlClass(summary.currentFloatingPnl)}`}>
              {formatPnl(summary.currentFloatingPnl)}
            </span>
            <span className="home-module-stat-hint">当前持仓 {summary.currentOpenCount} 笔</span>
          </div>
        </div>
      </div>
    );
  };

  const renderRecentActivityContent = () => {
    if (activityLoading && recentActivityList.length <= 0) {
      return <div className="home-module-activity-state">正在加载操作日志...</div>;
    }

    if (activityError && recentActivityList.length <= 0) {
      return <div className="home-module-activity-state home-module-activity-state-error">{activityError}</div>;
    }

    if (recentActivityList.length <= 0) {
      return <div className="home-module-activity-state">暂无最近操作日志。</div>;
    }

    return (
      <ul className="home-module-activity-list">
        {recentActivityList.map((item, index) => {
          const typeClass = resolveActivityTypeClass(item.eventType);
          const iconText = resolveActivityIconText(item);
          const iconSrc = resolveActivityIconSrc(item);
          const iconTag = resolveActivityIconTag(item.eventType);
          const mainText = resolveActivityMainText(item);
          const timeText = formatActivityTimeText(item.eventAt);

          return (
            <li
              key={`${item.eventType}-${item.eventAt}-${item.positionId ?? index}`}
              className={`home-module-activity-item ${typeClass}`}
              title={item.description || item.title}
            >
              <div className="home-module-activity-icon">
                {iconSrc ? (
                  <img className="home-module-activity-icon-image" src={iconSrc} alt={`${iconText}图标`} />
                ) : (
                  <span className="home-module-activity-icon-text">{iconText}</span>
                )}
                <span className="home-module-activity-icon-tag">{iconTag}</span>
              </div>
              <div className="home-module-activity-content">
                <div className="home-module-activity-main-text">{mainText}</div>
                <div className="home-module-activity-time-text">{timeText}</div>
              </div>
            </li>
          );
        })}
      </ul>
    );
  };

  const renderSubtitle = () => {
    if (!summary) {
      return '默认展示最近有数据的时间段';
    }

    if (queryMode === 'auto') {
      if (!summary.hasData && summary.windowDays === 30) {
        return '近30天无交易';
      }

      return `默认展示最近有数据：近${summary.windowDays}天`;
    }

    if (!summary.hasData) {
      return summary.windowDays === 30 ? '近30天无交易' : `近${summary.windowDays}天没有开仓记录`;
    }

    return `当前查看：近${summary.windowDays}天`;
  };

  const summaryRightStyle =
    isDesktopSummaryLayout && summaryLeftHeight > 0
      ? ({
          height: `${summaryLeftHeight}px`,
        } as React.CSSProperties)
      : undefined;

  return (
    <div className="home-module-root">
      {/* 近期总结卡片 */}
      <section className="home-module-card home-module-main-card home-module-summary-card">
        <div className="home-module-card-header">
          <h2 className="home-module-card-title">近期总结</h2>
          <span className="home-module-card-subtitle">{renderSubtitle()}</span>
        </div>
        <div className="home-module-summary-layout">
          <div ref={summaryLeftRef} className="home-module-summary-left">
            <div className="home-module-summary-filters" role="tablist" aria-label="近期总结时间范围">
              {SUMMARY_WINDOW_DAYS.map((days) => {
                const active = selectedWindowDays === days;
                return (
                  <button
                    key={days}
                    type="button"
                    className={`home-module-summary-filter-btn ${active ? 'is-active' : ''}`}
                    onClick={() => handleWindowClick(days)}
                    disabled={summaryLoading}
                  >
                    近{days}天
                  </button>
                );
              })}
            </div>
            {renderSummaryContent()}
          </div>
          <aside className="home-module-summary-right" style={summaryRightStyle} aria-label="最近操作日志">
            <div className="home-module-summary-right-title">最近操作日志</div>
            <div className="home-module-summary-right-body">{renderRecentActivityContent()}</div>
          </aside>
        </div>
      </section>

      {/* 快捷入口 */}
      <section className="home-module-card home-module-main-card">
        <div className="home-module-card-header">
          <h2 className="home-module-card-title">快捷入口</h2>
        </div>
        <div className="home-module-actions">
          <button type="button" className="home-module-action-btn" onClick={onCreateStrategy}>
            新建策略
          </button>
          <button type="button" className="home-module-action-btn" onClick={onOpenStrategyList}>
            打开策略列表
          </button>
          <button type="button" className="home-module-action-btn" onClick={onImportShareCode}>
            导入分享码
          </button>
          <button type="button" className="home-module-action-btn" onClick={onOpenMarket}>
            前往行情
          </button>
        </div>
      </section>

      {/* 指标精选轮播 */}
      <HomeIndicatorCarousel onOpenIndicatorDetail={onOpenIndicatorDetail} />

      {/* 新闻精选轮播 */}
      <HomeNewsCarousel onOpenNewsDetail={onOpenNewsDetail} />

      {/* 系统公告 / 风险提示 */}
      <section className="home-module-card home-module-wide-card">
        <div className="home-module-card-header">
          <h2 className="home-module-card-title">系统公告 / 风险提示</h2>
        </div>
        <div className="home-module-placeholder">
          当前为占位内容，后续可以接入服务端公告接口，或者展示本地配置的提示文案。
        </div>
      </section>
    </div>
  );
};

function formatPnl(value: number): string {
  if (!Number.isFinite(value)) {
    return '--';
  }

  const prefix = value > 0 ? '+' : '';
  return `${prefix}${value.toFixed(2)}`;
}

function formatWinRate(winRate: number | null, closedCount: number): string {
  if (closedCount <= 0 || winRate === null || !Number.isFinite(winRate)) {
    return '--';
  }

  return `${(winRate * 100).toFixed(2)}%`;
}

function resolvePnlClass(value: number): string {
  if (value > 0) {
    return 'home-module-pnl-positive';
  }

  if (value < 0) {
    return 'home-module-pnl-negative';
  }

  return '';
}

function resolveActivityTypeClass(eventType: string): string {
  if (eventType === 'close') {
    return 'is-close';
  }

  if (eventType === 'warn') {
    return 'is-warn';
  }

  return 'is-open';
}

function resolveActivityMainText(item: PositionRecentActivityItem): string {
  const title = item.title?.trim();
  if (title) {
    return title;
  }

  const description = item.description?.trim();
  if (description) {
    return description;
  }

  return '系统事件';
}

function resolveActivityIconTag(eventType: string): string {
  if (eventType === 'close') {
    return '平';
  }

  if (eventType === 'warn') {
    return '!';
  }

  return '开';
}

function resolveActivityIconText(item: PositionRecentActivityItem): string {
  if (item.eventType === 'warn') {
    return '告警';
  }

  return resolveCoinBadge(item.symbol);
}

function resolveActivityIconSrc(item: PositionRecentActivityItem): string | null {
  if (item.eventType === 'warn') {
    return null;
  }

  const baseAsset = resolveBaseAsset(item.symbol);
  if (!baseAsset) {
    return null;
  }

  return CRYPTO_ICON_MAP[baseAsset] ?? null;
}

function resolveCoinBadge(symbol?: string | null): string {
  const baseAsset = resolveBaseAsset(symbol);
  if (!baseAsset) {
    return '--';
  }

  return baseAsset.slice(0, 3);
}

function resolveBaseAsset(symbol?: string | null): string | null {
  if (!symbol) {
    return null;
  }

  const normalized = symbol
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, '');

  if (!normalized) {
    return null;
  }

  const suffixes = ['USDT', 'USDC', 'BUSD', 'USD', 'PERP'];
  for (const suffix of suffixes) {
    if (normalized.endsWith(suffix) && normalized.length > suffix.length) {
      return normalized.slice(0, normalized.length - suffix.length);
    }
  }

  return normalized;
}

function formatActivityTimeText(eventAt: string): string {
  const eventDate = new Date(eventAt);
  if (Number.isNaN(eventDate.getTime())) {
    return '--';
  }

  const relative = formatRelativeTime(eventDate);
  const absolute = new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(eventDate);

  return `${relative} · ${absolute}`;
}

function formatRelativeTime(targetDate: Date): string {
  const diffMs = Date.now() - targetDate.getTime();
  if (diffMs < 60 * 1000) {
    return '刚刚';
  }

  const diffMinutes = Math.floor(diffMs / (60 * 1000));
  if (diffMinutes < 60) {
    return `${diffMinutes}分钟前`;
  }

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `${diffHours}小时前`;
  }

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 30) {
    return `${diffDays}天前`;
  }

  const diffMonths = Math.floor(diffDays / 30);
  return `${diffMonths}个月前`;
}

export default HomeModule;
