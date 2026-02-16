import React, { useEffect, useMemo, useRef, useState } from 'react';
import BtcIcon from '../../assets/icons/crypto/BTC.svg';
import EthIcon from '../../assets/icons/crypto/ETH.svg';
import BnbIcon from '../../assets/icons/crypto/BNB.svg';
import XrpIcon from '../../assets/icons/crypto/XRP.svg';
import SolIcon from '../../assets/icons/crypto/SOL.svg';
import DogeIcon from '../../assets/icons/crypto/DOGE.svg';
import './CryptoMarketPanel.css';
import { getWsStatus, onWsStatusChange, subscribeMarket } from '../../network/index.ts';

type MarketItem = {
  icon: string;
  name: string;
  symbol: string;
  price: string;
  change: string;
};

type PanelSize = {
  width: number;
  height: number;
};

type CryptoMarketPanelProps = {
  onResize?: (size: PanelSize) => void;
  allowResize?: boolean;
  allowWidthResize?: boolean;
  allowHeightResize?: boolean;
  selectedSymbol?: string;
  onSelectSymbol?: (symbol: string) => void;
};

const MIN_ROWS = 3;
const ROW_HEIGHT = 52;
const HEADER_HEIGHT = 44;
const TABLE_HEADER_HEIGHT = 32;
const PANEL_PADDING = 16;
const MIN_WIDTH = 180;
const NARROW_WIDTH = 210;
const DEFAULT_SIZE: PanelSize = {
  width: 248,
  height: 480,
};
const MIN_HEIGHT = HEADER_HEIGHT + TABLE_HEADER_HEIGHT + ROW_HEIGHT * MIN_ROWS + PANEL_PADDING;

const initialItems: MarketItem[] = [
  { icon: BtcIcon, name: 'Bitcoin', symbol: 'BTC', price: '92,948.50', change: '+0.00%' },
  { icon: EthIcon, name: 'Ethereum', symbol: 'ETH', price: '3,280.22', change: '+1.12%' },
  { icon: BnbIcon, name: 'BNB', symbol: 'BNB', price: '562.10', change: '-0.84%' },
  { icon: XrpIcon, name: 'XRP', symbol: 'XRP', price: '0.5884', change: '+3.21%' },
  { icon: SolIcon, name: 'Solana', symbol: 'SOL', price: '192.30', change: '+4.51%' },
  { icon: DogeIcon, name: 'Dogecoin', symbol: 'DOGE', price: '0.1587', change: '+4.89%' },
];

const CryptoMarketPanel: React.FC<CryptoMarketPanelProps> = ({
  onResize,
  allowResize = true,
  allowWidthResize = true,
  allowHeightResize = true,
  selectedSymbol,
  onSelectSymbol,
}) => {
  const [items, setItems] = useState<MarketItem[]>(initialItems);
  const [wsStatus, setWsStatus] = useState(() => getWsStatus());
  const [panelSize, setPanelSize] = useState<PanelSize>(DEFAULT_SIZE);
  const [draggingIndex, setDraggingIndex] = useState<number | null>(null);
  const [dragOverIndex, setDragOverIndex] = useState<number | null>(null);
  const [hiddenElements, setHiddenElements] = useState<Record<number, { name: boolean; change: boolean }>>({});
  const listRef = useRef<HTMLDivElement>(null);
  const scrollTrackRef = useRef<HTMLDivElement>(null);
  const scrollThumbRef = useRef<HTMLDivElement>(null);
  const resizeStartRef = useRef<{ x: number; y: number; width: number; height: number } | null>(null);
  const resizeFrameRef = useRef<number | null>(null);
  const nameRefs = useRef<(HTMLSpanElement | null)[]>([]);
  const changeRefs = useRef<(HTMLDivElement | null)[]>([]);
  const rowRefs = useRef<(HTMLDivElement | null)[]>([]);
  const lastPriceRef = useRef<Record<string, number>>({});
  const pendingTickMapRef = useRef<Map<string, { symbol: string; price: number; ts: number }>>(new Map());
  const flushTickFrameRef = useRef<number | null>(null);

  const isNarrow = panelSize.width <= NARROW_WIDTH;
  const panelClassName = useMemo(
    () => `crypto-panel ${isNarrow ? 'is-narrow' : ''}`,
    [isNarrow]
  );

  const subscriptionKey = useMemo(
    () => items.map((item) => item.symbol).join('|'),
    [items]
  );
  const subscriptionSymbols = useMemo(
    () => subscriptionKey.split('|').filter(Boolean).map((symbol) => `${symbol}/USDT`),
    [subscriptionKey]
  );

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
  }, [panelSize.height, panelSize.width, items.length]);

  // 监听 WebSocket 连接状态，驱动右上角 Live 指示灯
  useEffect(() => {
    const unsubscribe = onWsStatusChange((status) => {
      setWsStatus(status);
    });
    return unsubscribe;
  }, []);

  // 检测文本是否被截断并更新隐藏状态
  useEffect(() => {
    const checkOverflow = () => {
      const newHiddenElements: Record<number, { name: boolean; change: boolean }> = {};

      items.forEach((_, index) => {
        const nameEl = nameRefs.current[index];
        const changeEl = changeRefs.current[index];

        if (!nameEl || !changeEl) {
          return;
        }

        // 第一步：检查 crypto-name 是否被截断
        const isNameOverflowing = nameEl.scrollWidth > nameEl.clientWidth + 1;
        
        if (isNameOverflowing) {
          // 如果 name 被截断，先标记为隐藏
          newHiddenElements[index] = { name: true, change: false };
        } else {
          // 如果 name 没有被截断，检查 change 是否被截断
          const isChangeOverflowing = changeEl.scrollWidth > changeEl.clientWidth + 1;
          newHiddenElements[index] = { name: false, change: isChangeOverflowing };
        }
      });

      // 先应用 name 的隐藏状态
      setHiddenElements((prev) => {
        const updated = { ...prev };
        Object.keys(newHiddenElements).forEach((key) => {
          const idx = parseInt(key);
          updated[idx] = { ...updated[idx], name: newHiddenElements[idx].name };
        });
        return updated;
      });

      // 第二步：如果 name 被隐藏了，重新检查 change 是否仍然被截断
      requestAnimationFrame(() => {
        const secondCheck: Record<number, { name: boolean; change: boolean }> = {};
        
        items.forEach((_, index) => {
          const nameEl = nameRefs.current[index];
          const changeEl = changeRefs.current[index];

          if (!nameEl || !changeEl) {
            return;
          }

          const wasNameHidden = newHiddenElements[index]?.name;
          
          if (wasNameHidden) {
            // name 被隐藏了，检查 change 是否仍然被截断
            const isChangeOverflowing = changeEl.scrollWidth > changeEl.clientWidth + 1;
            secondCheck[index] = { name: true, change: isChangeOverflowing };
          } else {
            // name 没有被隐藏，使用第一次检查的结果
            secondCheck[index] = newHiddenElements[index];
          }
        });

        setHiddenElements((prev) => ({ ...prev, ...secondCheck }));
      });
    };

    // 延迟执行以确保 DOM 已更新
    const timeoutId = setTimeout(checkOverflow, 50);

    // 监听窗口大小变化和面板大小变化
    const resizeObserver = new ResizeObserver(() => {
      setTimeout(checkOverflow, 50);
    });

    const list = listRef.current;
    if (list) {
      resizeObserver.observe(list);
    }

    items.forEach((_, index) => {
      const rowEl = rowRefs.current[index];
      if (rowEl) {
        resizeObserver.observe(rowEl);
      }
    });

    return () => {
      clearTimeout(timeoutId);
      resizeObserver.disconnect();
    };
  }, [panelSize.width, subscriptionKey]);

  useEffect(() => {
    if (subscriptionSymbols.length === 0) {
      return;
    }

    const flushTicks = () => {
      const pendingMap = pendingTickMapRef.current;
      if (pendingMap.size === 0) {
        return;
      }

      const tickMap = new Map(pendingMap.entries());
      pendingMap.clear();

      setItems((prev) => {
        let changed = false;
        const next = prev.map((item) => {
          const symbol = `${item.symbol}/USDT`;
          const tick = tickMap.get(symbol);
          if (!tick) {
            return item;
          }

          const previous = lastPriceRef.current[symbol];
          lastPriceRef.current[symbol] = tick.price;

          const price = formatPrice(tick.price);
          const change = previous && previous > 0
            ? formatChange((tick.price - previous) / previous)
            : item.change;

          if (price === item.price && change === item.change) {
            return item;
          }

          changed = true;
          return { ...item, price, change };
        });

        return changed ? next : prev;
      });
    };

    const unsubscribe = subscribeMarket(subscriptionSymbols, (ticks) => {
      const pendingMap = pendingTickMapRef.current;
      for (const tick of ticks) {
        pendingMap.set(tick.symbol, tick);
      }

      if (flushTickFrameRef.current !== null) {
        return;
      }

      // 价格推送合并到下一帧，降低连续 setState 带来的卡顿。
      flushTickFrameRef.current = window.requestAnimationFrame(() => {
        flushTickFrameRef.current = null;
        flushTicks();
      });
    });

    return () => {
      unsubscribe();
      pendingTickMapRef.current.clear();
      if (flushTickFrameRef.current !== null) {
        window.cancelAnimationFrame(flushTickFrameRef.current);
        flushTickFrameRef.current = null;
      }
    };
  }, [subscriptionSymbols]);

  const handleDragStart = (index: number) => (event: React.DragEvent<HTMLDivElement>) => {
    setDraggingIndex(index);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', String(index));
  };

  const handleDragOver = (index: number) => (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    setDragOverIndex(index);
  };

  const handleDrop = (index: number) => (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    const fromIndex =
      draggingIndex ?? Number(event.dataTransfer.getData('text/plain') || 'NaN');

    if (!Number.isFinite(fromIndex) || fromIndex === index) {
      setDragOverIndex(null);
      setDraggingIndex(null);
      return;
    }

    setItems((prevItems) => {
      const nextItems = [...prevItems];
      const [moved] = nextItems.splice(fromIndex, 1);
      nextItems.splice(index, 0, moved);
      return nextItems;
    });
    setDragOverIndex(null);
    setDraggingIndex(null);
  };

  const handleDragEnd = () => {
    setDragOverIndex(null);
    setDraggingIndex(null);
  };

  const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

  const handleResizeStart = (event: React.PointerEvent<HTMLDivElement>) => {
    event.preventDefault();
    resizeStartRef.current = {
      x: event.clientX,
      y: event.clientY,
      width: panelSize.width,
      height: panelSize.height,
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

      // 拖拽尺寸变化按帧提交，避免 pointermove 高频触发导致掉帧。
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

  const isLive = wsStatus === 'connected';
  const isConnecting = wsStatus === 'connecting';

  const actionClassName = `crypto-panel-action ${
    isLive ? 'is-live' : isConnecting ? 'is-connecting' : 'is-disconnected'
  }`;

  const actionLabel = isLive ? 'Live' : isConnecting ? 'Connecting' : 'Offline';

  return (
    <div
      className={panelClassName}
      style={{
        width: allowResize && allowWidthResize ? `${panelSize.width}px` : '100%',
        height: `${panelSize.height}px`,
        minWidth: `${MIN_WIDTH}px`,
        minHeight: `${MIN_HEIGHT}px`,
      }}
    >
      <div className="crypto-panel-header">
        <div className="crypto-panel-title">
          <span className="crypto-title">Markets</span>
        </div>
        <button className={actionClassName}>{actionLabel}</button>
      </div>

      <div className="crypto-table-header">
        <span className="crypto-header-asset">Asset</span>
        <span className="crypto-header-price">Price</span>
        <span className="crypto-header-change">24h</span>
      </div>

      <div className="crypto-list-wrapper">
        <div className="crypto-list" ref={listRef}>
          {items.map((item, index) => (
            <div
              key={item.symbol}
              ref={(el) => {
                rowRefs.current[index] = el;
              }}
              className={`crypto-row${draggingIndex === index ? ' is-dragging' : ''}${
                dragOverIndex === index ? ' is-over' : ''
              }${selectedSymbol === item.symbol ? ' is-selected' : ''}`}
              draggable
              onDragStart={handleDragStart(index)}
              onDragOver={handleDragOver(index)}
              onDrop={handleDrop(index)}
              onDragEnd={handleDragEnd}
              onClick={() => onSelectSymbol?.(item.symbol)}
            >
              <div className="crypto-asset">
                <div className="crypto-icon">
                  <img src={item.icon} alt={item.symbol} />
                </div>
                <div className="crypto-asset-info">
                  <span
                    ref={(el) => {
                      nameRefs.current[index] = el;
                    }}
                    className={`crypto-name ${hiddenElements[index]?.name ? 'is-hidden' : ''}`}
                  >
                    {item.symbol}
                  </span>
                </div>
              </div>
              <div className="crypto-price">${item.price}</div>
              <div
                ref={(el) => {
                  changeRefs.current[index] = el;
                }}
                className={`crypto-change ${item.change.startsWith('+') ? 'positive' : 'negative'} ${
                  hiddenElements[index]?.change ? 'is-hidden' : ''
                }`}
              >
                {item.change}
              </div>
            </div>
          ))}
        </div>
        <div className="crypto-scrollbar" ref={scrollTrackRef}>
          <div className="crypto-scrollbar-thumb" ref={scrollThumbRef}></div>
        </div>
      </div>

      {allowResize && (allowWidthResize || allowHeightResize) ? (
        <div
          className={`crypto-resize-handle${allowWidthResize && allowHeightResize ? '' : ' is-height-only'}`}
          onPointerDown={handleResizeStart}
        />
      ) : null}
    </div>
  );
};

function formatPrice(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatChange(ratio: number): string {
  const percent = ratio * 100;
  const sign = percent >= 0 ? '+' : '';
  return `${sign}${percent.toFixed(2)}%`;
}

export default CryptoMarketPanel;


