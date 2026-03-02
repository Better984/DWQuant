import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { HttpError } from '../../network/httpClient';
import {
  pullDiscoverCentralBankCalendars,
  pullDiscoverEconomicDataCalendars,
  pullDiscoverFinancialEventsCalendars,
  type DiscoverCalendarItem,
} from '../../network/discover/calendarClient';
import flagUS from '../../assets/icons/country/US.svg';
import flagCN from '../../assets/icons/country/CN.svg';
import flagEU from '../../assets/icons/country/EU.svg';
import flagJP from '../../assets/icons/country/JP.svg';
import flagGB from '../../assets/icons/country/GB.svg';
import './WhatsOnRoadPanel.css';

type PanelSize = {
  width: number;
  height: number;
};

type WhatsOnRoadPanelProps = {
  onResize?: (size: PanelSize) => void;
  allowResize?: boolean;
  allowWidthResize?: boolean;
  allowHeightResize?: boolean;
};

type DayItem = {
  key: string;
  offset: number;
  dayLabel: string;
  dateNumber: number;
  isToday: boolean;
  startMs: number;
  endMs: number;
};

type CalendarKind = 'central-bank' | 'financial-events' | 'economic-data';

type RoadEvent = DiscoverCalendarItem & {
  kind: CalendarKind;
};

const DAY_LABELS = ['SU', 'MO', 'TU', 'WE', 'TH', 'FR', 'SA'];
const HEADER_HEIGHT = 32;
const DAY_LIST_HEIGHT = 48;
const EVENT_ROW_HEIGHT = 56;
const MIN_WIDTH = 180;
const MIN_HEIGHT = HEADER_HEIGHT + DAY_LIST_HEIGHT + EVENT_ROW_HEIGHT * 2 + 48;
const DEFAULT_SIZE: PanelSize = {
  width: 248,
  height: 360,
};
const RANGE_PULL_LIMIT = 600;
const REFRESH_INTERVAL_MS = 60 * 1000;

const countryIcons: Record<string, string> = {
  US: flagUS,
  USA: flagUS,
  CN: flagCN,
  CHN: flagCN,
  EU: flagEU,
  EUR: flagEU,
  JP: flagJP,
  JPN: flagJP,
  GB: flagGB,
  GBR: flagGB,
  UK: flagGB,
};

const WhatsOnRoadPanel: React.FC<WhatsOnRoadPanelProps> = ({
  onResize,
  allowResize = true,
  allowWidthResize = true,
  allowHeightResize = true,
}) => {
  const [panelSize, setPanelSize] = useState<PanelSize>(DEFAULT_SIZE);
  const [selectedKey, setSelectedKey] = useState<string>('');
  const [visibleDayCount, setVisibleDayCount] = useState<number>(7);
  const [events, setEvents] = useState<RoadEvent[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const dayListRef = useRef<HTMLDivElement>(null);
  const resizeStartRef = useRef<{ x: number; y: number; width: number; height: number } | null>(null);
  const resizeFrameRef = useRef<number | null>(null);

  const dayItems = useMemo<DayItem[]>(() => {
    const today = new Date();
    const items: DayItem[] = [];
    for (let offset = -1; offset <= 5; offset += 1) {
      const date = new Date(today);
      date.setDate(today.getDate() + offset);

      const start = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, 0, 0, 0).getTime();
      const end = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999).getTime();

      items.push({
        key: `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`,
        offset,
        dayLabel: DAY_LABELS[date.getDay()],
        dateNumber: date.getDate(),
        isToday: offset === 0,
        startMs: start,
        endMs: end,
      });
    }
    return items;
  }, []);

  useEffect(() => {
    if (!selectedKey && dayItems.length > 1) {
      setSelectedKey(dayItems[1].key);
    }
  }, [dayItems, selectedKey]);

  // 动态计算可显示的日期数量
  useEffect(() => {
    const dayList = dayListRef.current;
    if (!dayList) {
      return;
    }

    const calculateVisibleDays = () => {
      const containerWidth = dayList.clientWidth;
      const minDayWidth = 40;
      const gap = 8;
      const maxCount = Math.floor((containerWidth + gap) / (minDayWidth + gap));
      const count = Math.max(3, Math.min(maxCount, dayItems.length));
      setVisibleDayCount(count);
    };

    calculateVisibleDays();

    const resizeObserver = new ResizeObserver(calculateVisibleDays);
    resizeObserver.observe(dayList);

    return () => {
      resizeObserver.disconnect();
    };
  }, [dayItems.length, panelSize.width]);

  useEffect(() => {
    if (allowResize && allowWidthResize) {
      onResize?.(panelSize);
    }
  }, [allowResize, allowWidthResize, onResize, panelSize]);

  const loadRangeData = useCallback(async (signal?: AbortSignal) => {
    if (dayItems.length <= 0) {
      return;
    }

    const startTime = dayItems[0].startMs;
    const endTime = dayItems[dayItems.length - 1].endMs;

    setLoading(true);
    setError(null);

    try {
      const [centralRes, financialRes, economicRes] = await Promise.all([
        pullDiscoverCentralBankCalendars({ startTime, endTime, limit: RANGE_PULL_LIMIT }, { signal }),
        pullDiscoverFinancialEventsCalendars({ startTime, endTime, limit: RANGE_PULL_LIMIT }, { signal }),
        pullDiscoverEconomicDataCalendars({ startTime, endTime, limit: RANGE_PULL_LIMIT }, { signal }),
      ]);

      const merged: RoadEvent[] = [];
      merged.push(...centralRes.items.map((item) => ({ ...item, kind: 'central-bank' as const })));
      merged.push(...financialRes.items.map((item) => ({ ...item, kind: 'financial-events' as const })));
      merged.push(...economicRes.items.map((item) => ({ ...item, kind: 'economic-data' as const })));

      const uniqueMap = new Map<string, RoadEvent>();
      for (const item of merged) {
        uniqueMap.set(`${item.kind}-${item.id}`, item);
      }

      const sorted = Array.from(uniqueMap.values()).sort((a, b) => {
        if (b.publishTimestamp !== a.publishTimestamp) {
          return b.publishTimestamp - a.publishTimestamp;
        }
        return b.id - a.id;
      });

      setEvents(sorted);
      setError(null);
    } catch (e) {
      if (signal?.aborted) {
        return;
      }

      setError(resolveHttpErrorMessage(e, '日历加载失败'));
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, [dayItems]);

  useEffect(() => {
    const controller = new AbortController();
    void loadRangeData(controller.signal);

    const intervalId = window.setInterval(() => {
      void loadRangeData();
    }, REFRESH_INTERVAL_MS);

    return () => {
      controller.abort();
      window.clearInterval(intervalId);
    };
  }, [loadRangeData]);

  const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

  const handleResizeStart = (event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    const panelBounds = panelRef.current?.getBoundingClientRect();
    resizeStartRef.current = {
      x: event.clientX,
      y: event.clientY,
      width: panelBounds?.width ?? panelSize.width,
      height: panelBounds?.height ?? panelSize.height,
    };
    let latestSize: PanelSize | null = null;

    const handlePointerMove = (moveEvent: PointerEvent) => {
      if (!resizeStartRef.current) {
        return;
      }

      const deltaX = moveEvent.clientX - resizeStartRef.current.x;
      const deltaY = moveEvent.clientY - resizeStartRef.current.y;
      const maxWidth = Math.max(MIN_WIDTH, window.innerWidth - 120);
      const maxHeight = Math.max(MIN_HEIGHT, window.innerHeight - 120);
      const nextWidth = allowWidthResize
        ? clamp(resizeStartRef.current.width + deltaX, MIN_WIDTH, maxWidth)
        : resizeStartRef.current.width;
      const nextHeight = allowHeightResize
        ? clamp(resizeStartRef.current.height + deltaY, MIN_HEIGHT, maxHeight)
        : resizeStartRef.current.height;

      latestSize = { width: nextWidth, height: nextHeight };
      if (resizeFrameRef.current !== null) {
        return;
      }

      // 拖拽按帧更新，减少高频 pointermove 导致的重排抖动。
      resizeFrameRef.current = window.requestAnimationFrame(() => {
        resizeFrameRef.current = null;
        if (latestSize) {
          setPanelSize(latestSize);
        }
      });
    };

    const handlePointerUp = () => {
      resizeStartRef.current = null;
      if (resizeFrameRef.current !== null) {
        window.cancelAnimationFrame(resizeFrameRef.current);
        resizeFrameRef.current = null;
      }
      if (latestSize) {
        setPanelSize(latestSize);
      }
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', handlePointerUp);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', handlePointerUp);
  };

  // 根据可见数量筛选日期项
  const visibleDayItems = useMemo(() => {
    const selectedIndex = selectedKey ? dayItems.findIndex((item) => item.key === selectedKey) : -1;
    const todayIndex = dayItems.findIndex((item) => item.isToday);

    let startIndex = 0;

    if (selectedIndex !== -1) {
      const halfCount = Math.floor(visibleDayCount / 2);
      startIndex = Math.max(0, Math.min(selectedIndex - halfCount, dayItems.length - visibleDayCount));
    } else if (todayIndex !== -1) {
      startIndex = Math.max(0, todayIndex - Math.max(0, visibleDayCount - (dayItems.length - todayIndex)));
    }

    return dayItems.slice(startIndex, startIndex + visibleDayCount);
  }, [dayItems, visibleDayCount, selectedKey]);

  const selectedDay = dayItems.find((item) => item.key === selectedKey);
  const dayEvents = useMemo(() => {
    if (!selectedDay) {
      return [];
    }

    return events
      .filter((item) => item.publishTimestamp >= selectedDay.startMs && item.publishTimestamp <= selectedDay.endMs)
      .sort((a, b) => {
        if (b.publishTimestamp !== a.publishTimestamp) {
          return b.publishTimestamp - a.publishTimestamp;
        }
        return b.id - a.id;
      });
  }, [events, selectedDay]);

  return (
    <div
      className="whats-on-road-panel"
      ref={panelRef}
      style={{
        width: allowResize && allowWidthResize ? `${panelSize.width}px` : '100%',
        height: allowResize && allowHeightResize ? `${panelSize.height}px` : '100%',
        minHeight: allowResize && allowHeightResize ? `${MIN_HEIGHT}px` : 0,
        minWidth: `${MIN_WIDTH}px`,
      }}
    >
      <div className="whats-on-road-header">
        <h3 className="whats-on-road-title">Macro Calendar</h3>
      </div>

      <div
        className="whats-on-road-day-list ui-scrollable"
        ref={dayListRef}
        style={{ '--visible-day-count': visibleDayCount } as React.CSSProperties}
      >
        {visibleDayItems.map((item) => (
          <button
            key={item.key}
            className={`whats-on-road-day${selectedKey === item.key ? ' is-active' : ''}`}
            onClick={() => setSelectedKey(item.key)}
          >
            <span className="whats-on-road-day-date">{item.dateNumber}</span>
            {item.isToday ? <span className="whats-on-road-day-dot" /> : null}
          </button>
        ))}
      </div>

      <div className="whats-on-road-list-wrapper">
        <div className="whats-on-road-list ui-scrollable" ref={listRef}>
          {loading && dayEvents.length <= 0 ? <div className="whats-on-road-empty">日历加载中...</div> : null}
          {!loading && error && dayEvents.length <= 0 ? <div className="whats-on-road-empty">{error}</div> : null}
          {!loading && !error && dayEvents.length <= 0 ? <div className="whats-on-road-empty">当日暂无日历事件</div> : null}

          {dayEvents.map((event) => {
            const icon = resolveCountryIcon(event.countryCode);
            const details = buildEventDetails(event);
            return (
              <div key={`${event.kind}-${event.id}`} className="whats-on-road-item">
                <div className="whats-on-road-item-flag">
                  {icon ? (
                    <img src={icon} alt={event.countryCode || 'country'} />
                  ) : (
                    <span className="whats-on-road-item-country-code">{normalizeCountryCode(event.countryCode)}</span>
                  )}
                </div>
                <div className="whats-on-road-item-text">
                  <p className="whats-on-road-item-title">{event.calendarName}</p>
                  <p className="whats-on-road-item-time">
                    {formatCalendarTime(event.publishTimestamp, event.hasExactPublishTime)}
                    {' · '}
                    {event.countryName || normalizeCountryCode(event.countryCode)}
                    {' · '}
                    {resolveKindLabel(event.kind)}
                  </p>
                  {details ? <p className="whats-on-road-item-meta">{details}</p> : null}
                  <p className="whats-on-road-item-importance">重要性：{renderImportance(event.importanceLevel)}</p>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {allowResize && (allowWidthResize || allowHeightResize) ? (
        <div
          className={`whats-on-road-resize-handle${allowWidthResize && allowHeightResize ? '' : ' is-height-only'}`}
          onPointerDown={handleResizeStart}
        />
      ) : null}
    </div>
  );
};

function normalizeCountryCode(code: string): string {
  if (!code) {
    return '--';
  }

  return code.trim().toUpperCase();
}

function resolveCountryIcon(code: string): string | null {
  const normalized = normalizeCountryCode(code);
  return countryIcons[normalized] ?? null;
}

function resolveKindLabel(kind: CalendarKind): string {
  switch (kind) {
    case 'central-bank':
      return '央行活动';
    case 'financial-events':
      return '财经事件';
    case 'economic-data':
      return '经济数据';
    default:
      return '日历';
  }
}

function formatCalendarTime(publishTimestamp: number, hasExactPublishTime: boolean): string {
  if (!Number.isFinite(publishTimestamp) || publishTimestamp <= 0) {
    return '--:--';
  }

  const date = new Date(publishTimestamp);
  if (Number.isNaN(date.getTime())) {
    return '--:--';
  }

  if (!hasExactPublishTime) {
    return `${new Intl.DateTimeFormat('zh-CN', {
      month: '2-digit',
      day: '2-digit',
    }).format(date)} 全天`;
  }

  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date);
}

function buildEventDetails(event: RoadEvent): string {
  if (event.kind !== 'economic-data') {
    return '';
  }

  const parts: string[] = [];
  if (event.publishedValue) {
    parts.push(`公布 ${event.publishedValue}`);
  }
  if (event.forecastValue) {
    parts.push(`预测 ${event.forecastValue}`);
  }
  if (event.previousValue) {
    parts.push(`前值 ${event.previousValue}`);
  }

  if (parts.length > 0) {
    return parts.join(' · ');
  }

  return event.dataEffect ? `影响：${event.dataEffect}` : '';
}

function renderImportance(level: number): string {
  const safeLevel = Number.isFinite(level) ? Math.max(0, Math.min(3, Math.floor(level))) : 0;
  if (safeLevel <= 0) {
    return '低';
  }

  return '●'.repeat(safeLevel);
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

export default WhatsOnRoadPanel;
