import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';
import { HttpError } from '../../network/httpClient';
import type { DiscoverFeedItem } from '../../network/discover/feedClient';
import { pullDiscoverArticles, pullDiscoverNewsflashes } from '../../network/discover/feedClient';
import './DiscoverModule.css';

interface ChartPalette {
  textPrimary: string;
  textSecondary: string;
  axisLine: string;
  colorPrimary: string;
  colorWarning: string;
}

const INITIAL_LIMIT = 20;
const HISTORY_LIMIT = 20;
const INCREMENT_LIMIT = 200;
const POLL_INTERVAL_MS = 8000;

const createPalette = (isDarkMode: boolean): ChartPalette => ({
  textPrimary: isDarkMode ? '#F3F4F6' : '#0F172A',
  textSecondary: isDarkMode ? 'rgba(243, 244, 246, 0.72)' : '#64748B',
  axisLine: isDarkMode ? 'rgba(148, 163, 184, 0.45)' : 'rgba(15, 23, 42, 0.18)',
  colorPrimary: '#3B82F6',
  colorWarning: '#F59E0B',
});

const buildFearGreedGaugeOption = (
  value: number,
  name: string,
  palette: ChartPalette,
): EChartsOption => ({
  tooltip: { formatter: `情绪指数：${value}` },
  series: [
    {
      type: 'gauge',
      min: 0,
      max: 100,
      startAngle: 210,
      endAngle: -30,
      radius: '95%',
      pointer: {
        length: '56%',
        width: 4,
        itemStyle: { color: palette.colorPrimary },
      },
      progress: {
        show: true,
        width: 14,
        roundCap: true,
        itemStyle: { color: palette.colorWarning },
      },
      axisLine: {
        roundCap: true,
        lineStyle: {
          width: 14,
          color: [
            [0.25, '#22C55E'],
            [0.75, '#F59E0B'],
            [1, '#EF4444'],
          ],
        },
      },
      axisTick: {
        distance: -20,
        splitNumber: 4,
        lineStyle: { color: palette.axisLine, width: 1 },
      },
      splitLine: {
        distance: -20,
        length: 12,
        lineStyle: { color: palette.axisLine, width: 2 },
      },
      axisLabel: {
        distance: 16,
        color: palette.textSecondary,
        fontSize: 10,
      },
      anchor: {
        show: true,
        size: 10,
        itemStyle: { color: palette.colorPrimary },
      },
      title: {
        offsetCenter: [0, '74%'],
        color: palette.textSecondary,
        fontSize: 12,
      },
      detail: {
        valueAnimation: true,
        offsetCenter: [0, '28%'],
        color: palette.textPrimary,
        fontSize: 24,
        formatter: '{value}',
      },
      data: [{ value, name }],
    },
  ],
});

const FearGreedChart: React.FC<{ value: number; label: string }> = ({ value, label }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<ECharts | null>(null);
  const [isDarkMode, setIsDarkMode] = useState(() =>
    document.documentElement.classList.contains('dark-theme'),
  );

  useEffect(() => {
    const root = document.documentElement;
    const updateTheme = () => setIsDarkMode(root.classList.contains('dark-theme'));
    const observer = new MutationObserver(updateTheme);
    observer.observe(root, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);

  const palette = useMemo(() => createPalette(isDarkMode), [isDarkMode]);
  const option = useMemo(() => buildFearGreedGaugeOption(value, label, palette), [value, label, palette]);

  useEffect(() => {
    if (!containerRef.current) return;
    const chart = echarts.init(containerRef.current);
    chartRef.current = chart;
    chart.setOption(option, true);
    const resizeObserver = new ResizeObserver(() => chart.resize());
    resizeObserver.observe(containerRef.current);
    return () => {
      resizeObserver.disconnect();
      chart.dispose();
      chartRef.current = null;
    };
  }, []);

  useEffect(() => {
    chartRef.current?.setOption(option, true);
  }, [option]);

  return <div ref={containerRef} className="discover-fg-chart" />;
};

type DiscoverModuleProps = {
  focusNewsId?: string;
  onFocusHandled?: () => void;
};

const DiscoverModule: React.FC<DiscoverModuleProps> = ({ focusNewsId, onFocusHandled }) => {
  const [newsItems, setNewsItems] = useState<DiscoverFeedItem[]>([]);
  const [flashItems, setFlashItems] = useState<DiscoverFeedItem[]>([]);
  const [newsLoading, setNewsLoading] = useState(false);
  const [flashLoading, setFlashLoading] = useState(false);
  const [newsLoadingMore, setNewsLoadingMore] = useState(false);
  const [flashLoadingMore, setFlashLoadingMore] = useState(false);
  const [newsError, setNewsError] = useState<string | null>(null);
  const [flashError, setFlashError] = useState<string | null>(null);
  const [newsHasMore, setNewsHasMore] = useState(true);
  const [flashHasMore, setFlashHasMore] = useState(true);
  const newsItemsRef = useRef<DiscoverFeedItem[]>([]);
  const flashItemsRef = useRef<DiscoverFeedItem[]>([]);
  const newsIncrementRunningRef = useRef(false);
  const flashIncrementRunningRef = useRef(false);

  const fearGreed = {
    today: 10,
    yesterday: 8,
    lastWeek: 41,
    label: '极度恐惧',
  };

  useEffect(() => {
    newsItemsRef.current = newsItems;
  }, [newsItems]);

  useEffect(() => {
    flashItemsRef.current = flashItems;
  }, [flashItems]);

  const loadInitialFeeds = useCallback(async (signal?: AbortSignal) => {
    setNewsLoading(true);
    setFlashLoading(true);
    setNewsError(null);
    setFlashError(null);

    const [articleResult, flashResult] = await Promise.allSettled([
      pullDiscoverArticles({ limit: INITIAL_LIMIT }, { signal }),
      pullDiscoverNewsflashes({ limit: INITIAL_LIMIT }, { signal }),
    ]);

    if (articleResult.status === 'fulfilled') {
      const sorted = sortItemsDesc(articleResult.value.items);
      setNewsItems(sorted);
      setNewsHasMore(articleResult.value.hasMore);
      setNewsError(null);
    } else {
      setNewsError(resolveHttpErrorMessage(articleResult.reason, '新闻加载失败'));
    }
    setNewsLoading(false);

    if (flashResult.status === 'fulfilled') {
      const sorted = sortItemsDesc(flashResult.value.items);
      setFlashItems(sorted);
      setFlashHasMore(flashResult.value.hasMore);
      setFlashError(null);
    } else {
      setFlashError(resolveHttpErrorMessage(flashResult.reason, '快讯加载失败'));
    }
    setFlashLoading(false);
  }, []);

  const pullIncrementalArticles = useCallback(async () => {
    const current = newsItemsRef.current;
    if (current.length <= 0 || newsIncrementRunningRef.current) {
      return;
    }

    const latestId = current[0]?.id;
    if (!latestId || latestId <= 0) {
      return;
    }

    newsIncrementRunningRef.current = true;
    try {
      const result = await pullDiscoverArticles({ latestId, limit: INCREMENT_LIMIT });
      if (!result.items || result.items.length <= 0) {
        return;
      }

      setNewsItems((prev) => mergeItemsDesc(prev, sortItemsDesc(result.items)));
    } catch (error) {
      console.warn('[Discover] 新闻增量拉取失败', error);
    } finally {
      newsIncrementRunningRef.current = false;
    }
  }, []);

  const pullIncrementalFlashes = useCallback(async () => {
    const current = flashItemsRef.current;
    if (current.length <= 0 || flashIncrementRunningRef.current) {
      return;
    }

    const latestId = current[0]?.id;
    if (!latestId || latestId <= 0) {
      return;
    }

    flashIncrementRunningRef.current = true;
    try {
      const result = await pullDiscoverNewsflashes({ latestId, limit: INCREMENT_LIMIT });
      if (!result.items || result.items.length <= 0) {
        return;
      }

      setFlashItems((prev) => mergeItemsDesc(prev, sortItemsDesc(result.items)));
    } catch (error) {
      console.warn('[Discover] 快讯增量拉取失败', error);
    } finally {
      flashIncrementRunningRef.current = false;
    }
  }, []);

  const loadMoreNews = useCallback(async () => {
    const newsList = newsItemsRef.current;
    const oldestId = newsList.length > 0 ? newsList[newsList.length - 1]?.id : undefined;
    if (!oldestId || oldestId <= 0 || newsLoadingMore || !newsHasMore) {
      return;
    }

    setNewsLoadingMore(true);
    setNewsError(null);
    try {
      const result = await pullDiscoverArticles({ beforeId: oldestId, limit: HISTORY_LIMIT });
      setNewsItems((prev) => mergeItemsDesc(prev, sortItemsDesc(result.items)));
      setNewsHasMore(result.hasMore);
    } catch (error) {
      setNewsError(resolveHttpErrorMessage(error, '新闻加载失败'));
    } finally {
      setNewsLoadingMore(false);
    }
  }, [newsHasMore, newsLoadingMore]);

  const loadMoreFlashes = useCallback(async () => {
    const flashList = flashItemsRef.current;
    const oldestId = flashList.length > 0 ? flashList[flashList.length - 1]?.id : undefined;
    if (!oldestId || oldestId <= 0 || flashLoadingMore || !flashHasMore) {
      return;
    }

    setFlashLoadingMore(true);
    setFlashError(null);
    try {
      const result = await pullDiscoverNewsflashes({ beforeId: oldestId, limit: HISTORY_LIMIT });
      setFlashItems((prev) => mergeItemsDesc(prev, sortItemsDesc(result.items)));
      setFlashHasMore(result.hasMore);
    } catch (error) {
      setFlashError(resolveHttpErrorMessage(error, '快讯加载失败'));
    } finally {
      setFlashLoadingMore(false);
    }
  }, [flashHasMore, flashLoadingMore]);

  useEffect(() => {
    const controller = new AbortController();
    void loadInitialFeeds(controller.signal);

    const intervalId = window.setInterval(() => {
      void pullIncrementalArticles();
      void pullIncrementalFlashes();
    }, POLL_INTERVAL_MS);

    return () => {
      controller.abort();
      window.clearInterval(intervalId);
    };
  }, [loadInitialFeeds, pullIncrementalArticles, pullIncrementalFlashes]);

  useEffect(() => {
    if (!focusNewsId) {
      return;
    }

    const scrollTimer = window.setTimeout(() => {
      const element = document.getElementById(`discover-news-${focusNewsId}`);
      if (!element) {
        if (newsItems.length > 0) {
          onFocusHandled?.();
        }
        return;
      }

      element.scrollIntoView({ behavior: 'smooth', block: 'center' });
      const animationTimer = window.setTimeout(() => {
        element.classList.add('discover-news-item--focused');
        const cleanupTimer = window.setTimeout(() => {
          element.classList.remove('discover-news-item--focused');
          onFocusHandled?.();
        }, 800);
        return () => window.clearTimeout(cleanupTimer);
      }, 500);

      return () => window.clearTimeout(animationTimer);
    }, 0);

    return () => {
      window.clearTimeout(scrollTimer);
    };
  }, [focusNewsId, onFocusHandled, newsItems.length]);

  return (
    <div className="module-container discover-module-container">
      <div className="page-title">
        <h1 className="title-text">市场资讯</h1>
      </div>

      <div className="discover-layout">
        <section className="discover-news-section">
          <h2 className="discover-section-title">新闻</h2>
          <div className="discover-news-list-wrapper ui-scrollable">
            {newsLoading && newsItems.length <= 0 ? (
              <div className="discover-empty-tip">新闻加载中...</div>
            ) : null}
            {!newsLoading && newsError && newsItems.length <= 0 ? (
              <div className="discover-empty-tip discover-empty-tip--error">{newsError}</div>
            ) : null}
            {!newsLoading && !newsError && newsItems.length <= 0 ? (
              <div className="discover-empty-tip">暂无新闻数据</div>
            ) : null}
            <ul className="discover-news-list">
              {newsItems.map((item) => (
                <li
                  key={item.id}
                  id={`discover-news-${item.id}`}
                  className="discover-news-item"
                >
                  <div className="discover-news-time">{formatReleaseTime(item.releaseTime)}</div>
                  <div className="discover-news-main">
                    <div className="discover-news-title">{item.title}</div>
                    <p className="discover-news-summary">{resolveSummary(item)}</p>
                    <div className="discover-news-meta">
                      <span className="discover-news-source-chip">{item.source}</span>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          </div>
          <div className="discover-list-actions">
            <button
              type="button"
              className="discover-list-more-btn"
              onClick={() => void loadMoreNews()}
              disabled={newsLoadingMore || !newsHasMore}
            >
              {newsLoadingMore ? '加载中...' : newsHasMore ? '加载更早新闻' : '没有更早新闻'}
            </button>
          </div>
          {newsError && newsItems.length > 0 ? (
            <div className="discover-inline-error">{newsError}</div>
          ) : null}
        </section>

        <aside className="discover-right-column">
          <section className="discover-fg-card">
            <div className="discover-section-title-row">
              <h2 className="discover-section-title">恐惧&贪婪指数</h2>
            </div>
            <FearGreedChart value={fearGreed.today} label={fearGreed.label} />
            <div className="discover-fg-stats">
              <div className="discover-fg-stat">
                <span className="discover-fg-stat-label">昨天</span>
                <span className="discover-fg-stat-value">{fearGreed.yesterday}</span>
              </div>
              <div className="discover-fg-stat">
                <span className="discover-fg-stat-label">上周</span>
                <span className="discover-fg-stat-value">{fearGreed.lastWeek}</span>
              </div>
            </div>
          </section>

          <section className="discover-flash-card">
            <div className="discover-section-title-row">
              <h2 className="discover-section-title">快讯</h2>
            </div>
            {flashLoading && flashItems.length <= 0 ? (
              <div className="discover-empty-tip">快讯加载中...</div>
            ) : null}
            {!flashLoading && flashError && flashItems.length <= 0 ? (
              <div className="discover-empty-tip discover-empty-tip--error">{flashError}</div>
            ) : null}
            {!flashLoading && !flashError && flashItems.length <= 0 ? (
              <div className="discover-empty-tip">暂无快讯数据</div>
            ) : null}
            <div className="discover-flash-section ui-scrollable">
              <ul className="discover-flash-list">
                {flashItems.map((item) => (
                  <li key={item.id} className="discover-flash-item">
                    <div className="discover-flash-time">{formatReleaseTime(item.releaseTime)}</div>
                    <div className="discover-flash-content">
                      <div className="discover-flash-title">{item.title}</div>
                      <p className="discover-flash-summary">{resolveSummary(item)}</p>
                      <div className="discover-flash-meta">
                        <span className="discover-flash-source-chip">{item.source}</span>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
            <div className="discover-list-actions">
              <button
                type="button"
                className="discover-list-more-btn"
                onClick={() => void loadMoreFlashes()}
                disabled={flashLoadingMore || !flashHasMore}
              >
                {flashLoadingMore ? '加载中...' : flashHasMore ? '加载更早快讯' : '没有更早快讯'}
              </button>
            </div>
            {flashError && flashItems.length > 0 ? (
              <div className="discover-inline-error">{flashError}</div>
            ) : null}
          </section>
        </aside>
      </div>
    </div>
  );
};

function sortItemsDesc(items: DiscoverFeedItem[]): DiscoverFeedItem[] {
  return [...items].sort((a, b) => {
    if (b.releaseTime !== a.releaseTime) {
      return b.releaseTime - a.releaseTime;
    }
    return b.id - a.id;
  });
}

function mergeItemsDesc(baseItems: DiscoverFeedItem[], incomingItems: DiscoverFeedItem[]): DiscoverFeedItem[] {
  if (incomingItems.length <= 0) {
    return baseItems;
  }

  const map = new Map<number, DiscoverFeedItem>();
  for (const item of baseItems) {
    map.set(item.id, item);
  }
  for (const item of incomingItems) {
    map.set(item.id, item);
  }

  return Array.from(map.values()).sort((a, b) => {
    if (b.releaseTime !== a.releaseTime) {
      return b.releaseTime - a.releaseTime;
    }
    return b.id - a.id;
  });
}

function resolveSummary(item: DiscoverFeedItem): string {
  const summary = item.summary?.trim();
  if (summary) {
    return summary;
  }

  return stripHtml(item.contentHtml).slice(0, 280);
}

function stripHtml(input: string): string {
  if (!input) {
    return '';
  }

  return input
    .replace(/<[^>]+>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function formatReleaseTime(releaseTime: number): string {
  if (!Number.isFinite(releaseTime) || releaseTime <= 0) {
    return '--:--';
  }

  const date = new Date(releaseTime);
  if (Number.isNaN(date.getTime())) {
    return '--:--';
  }

  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

function resolveHttpErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpError) {
    return error.message || fallback;
  }

  if (error instanceof Error) {
    return error.message || fallback;
  }

  return fallback;
}

export default DiscoverModule;
