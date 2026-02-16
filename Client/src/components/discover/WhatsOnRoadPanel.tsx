import React, { useEffect, useMemo, useRef, useState } from 'react';
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

type RoadEvent = {
  country: keyof typeof countryIcons;
  title: string;
  time: string;
};

type DayItem = {
  key: string;
  offset: number;
  dayLabel: string;
  dateNumber: number;
  isToday: boolean;
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

const countryIcons = {
  US: flagUS,
  CN: flagCN,
  EU: flagEU,
  JP: flagJP,
  GB: flagGB,
};

const eventsByOffset: Record<number, RoadEvent[]> = {
  [-1]: [
    { country: 'US', title: 'Trump rally remarks on trade outlook', time: '21:30' },
    { country: 'EU', title: 'Eurozone consumer confidence flash', time: '17:00' },
  ],
  [0]: [
    { country: 'US', title: 'FOMC rate decision', time: '02:00' },
    { country: 'US', title: 'Fed chair press conference', time: '02:30' },
    { country: 'CN', title: 'China industrial profits', time: '09:30' },
  ],
  [1]: [
    { country: 'JP', title: 'BoJ policy statement', time: '12:00' },
    { country: 'EU', title: 'ECB staff macro projections', time: '16:00' },
  ],
  [2]: [
    { country: 'CN', title: 'CPI release', time: '09:30' },
    { country: 'GB', title: 'BoE governor speech', time: '18:30' },
  ],
  [3]: [
    { country: 'US', title: 'Initial jobless claims', time: '20:30' },
    { country: 'EU', title: 'ECB press conference', time: '21:45' },
  ],
  [4]: [
    { country: 'JP', title: 'BoJ minutes', time: '12:30' },
    { country: 'US', title: 'PCE price index', time: '20:30' },
  ],
  [5]: [
    { country: 'US', title: 'Nonfarm payrolls', time: '20:30' },
    { country: 'CN', title: 'Caixin services PMI', time: '10:45' },
  ],
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
  const panelRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const dayListRef = useRef<HTMLDivElement>(null);
  const scrollTrackRef = useRef<HTMLDivElement>(null);
  const scrollThumbRef = useRef<HTMLDivElement>(null);
  const resizeStartRef = useRef<{ x: number; y: number; width: number; height: number } | null>(null);
  const resizeFrameRef = useRef<number | null>(null);

  const dayItems = useMemo<DayItem[]>(() => {
    const today = new Date();
    const items: DayItem[] = [];
    for (let offset = -1; offset <= 5; offset += 1) {
      const date = new Date(today);
      date.setDate(today.getDate() + offset);
      items.push({
        key: `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`,
        offset,
        dayLabel: DAY_LABELS[date.getDay()],
        dateNumber: date.getDate(),
        isToday: offset === 0,
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
      // 每个日期按钮的最小宽度（包括padding和内容，移除标签后更窄）
      const minDayWidth = 40; // min-width from CSS (reduced after removing label)
      // gap between items
      const gap = 8;
      // 计算可以容纳多少个日期
      // 公式: (containerWidth + gap) / (minDayWidth + gap)
      const maxCount = Math.floor((containerWidth + gap) / (minDayWidth + gap));
      // 至少显示3个，最多显示全部
      const count = Math.max(3, Math.min(maxCount, dayItems.length));
      setVisibleDayCount(count);
    };

    // 初始计算
    calculateVisibleDays();

    // 使用 ResizeObserver 监听容器宽度变化
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

  useEffect(() => {
    const list = listRef.current;
    const track = scrollTrackRef.current;
    const thumb = scrollThumbRef.current;
    if (!list || !track || !thumb) {
      return;
    }

    const updateScroll = () => {
      const { scrollHeight, clientHeight, scrollTop } = list;
      const trackHeight = track.clientHeight;
      const isScrollable = scrollHeight > clientHeight + 1;
      track.style.opacity = isScrollable ? '1' : '0';

      if (!isScrollable) {
        thumb.style.height = `${trackHeight}px`;
        thumb.style.transform = 'translateY(0px)';
        return;
      }

      const thumbHeight = Math.max(24, (clientHeight / scrollHeight) * trackHeight);
      const maxThumbTop = trackHeight - thumbHeight;
      const thumbTop =
        scrollHeight === clientHeight
          ? 0
          : (scrollTop / (scrollHeight - clientHeight)) * maxThumbTop;

      thumb.style.height = `${thumbHeight}px`;
      thumb.style.transform = `translateY(${thumbTop}px)`;
    };

    updateScroll();
    list.addEventListener('scroll', updateScroll);

    const resizeObserver = new ResizeObserver(updateScroll);
    resizeObserver.observe(list);

    return () => {
      list.removeEventListener('scroll', updateScroll);
      resizeObserver.disconnect();
    };
  }, [panelSize.height, selectedKey]);

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
    // 如果选中的日期存在，确保它在可见列表中
    const selectedIndex = selectedKey ? dayItems.findIndex((item) => item.key === selectedKey) : -1;
    const todayIndex = dayItems.findIndex((item) => item.isToday);
    
    let startIndex = 0;
    
    if (selectedIndex !== -1) {
      // 优先确保选中的日期在可见列表中
      // 尽量让选中的日期居中显示
      const halfCount = Math.floor(visibleDayCount / 2);
      startIndex = Math.max(0, Math.min(selectedIndex - halfCount, dayItems.length - visibleDayCount));
    } else if (todayIndex !== -1) {
      // 如果没有选中，优先显示今天及之后的日期
      startIndex = Math.max(0, todayIndex - Math.max(0, visibleDayCount - (dayItems.length - todayIndex)));
    }
    
    return dayItems.slice(startIndex, startIndex + visibleDayCount);
  }, [dayItems, visibleDayCount, selectedKey]);

  const selectedDay = dayItems.find((item) => item.key === selectedKey);
  const events = selectedDay ? eventsByOffset[selectedDay.offset] ?? [] : [];

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
        <h3 className="whats-on-road-title">Whats on the road</h3>
      </div>

      <div 
        className="whats-on-road-day-list" 
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
        <div className="whats-on-road-list" ref={listRef}>
          {events.length === 0 ? (
            <div className="whats-on-road-empty">No key events</div>
          ) : (
            events.map((event, index) => (
              <div key={`${event.title}-${index}`} className="whats-on-road-item">
                <div className="whats-on-road-item-flag">
                  <img src={countryIcons[event.country]} alt={event.country} />
                </div>
                <div className="whats-on-road-item-text">
                  <p className="whats-on-road-item-title">{event.title}</p>
                  <p className="whats-on-road-item-time">{event.time}</p>
                </div>
              </div>
            ))
          )}
        </div>
        <div className="whats-on-road-scrollbar" ref={scrollTrackRef}>
          <div className="whats-on-road-scrollbar-thumb" ref={scrollThumbRef}></div>
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

export default WhatsOnRoadPanel;
