import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import {
  dispose,
  getSupportedIndicators,
  init,
  LoadDataType,
  type Chart,
  type KLineData,
} from "klinecharts";
import { HttpClient, subscribeMarket } from "../network";
import { registerCustomOverlays } from "./customOverlays";
import "./MarketChart.css";

// 注册自定义绘图工具
registerCustomOverlays();

type MarketChartProps = {
  symbol?: string;
  interval?: string;
  height?: string | number;
  theme?: "light" | "dark";
};

type IndicatorOption = {
  label: string;
  value: string;
  stack: boolean;
  pane?: "main" | "sub";
};

const DEFAULT_SYMBOL = "Binance:BTC/USDT";
const DEFAULT_INTERVAL = "1";
const DEFAULT_COUNT = 500;
const CANDLE_PANE_ID = "candle_pane";
const DEBUG_KLINE = import.meta.env.DEV;

const PERIOD_TABS = [
  { label: "1m", value: "1" },
  { label: "5m", value: "5" },
  { label: "15m", value: "15" },
  { label: "1H", value: "60" },
  { label: "2H", value: "120", disabled: true },
  { label: "4H", value: "240" },
  { label: "D", value: "D" },
  { label: "W", value: "W" },
  { label: "M", value: "M", disabled: true },
  { label: "Y", value: "Y", disabled: true },
];

const INDICATOR_OPTIONS: IndicatorOption[] = [
  { label: "MA", value: "MA", stack: true, pane: "main" },
  { label: "EMA", value: "EMA", stack: true, pane: "main" },
  { label: "BOLL", value: "BOLL", stack: true, pane: "main" },
  { label: "VOL", value: "VOL", stack: false },
  { label: "MACD", value: "MACD", stack: false },
  { label: "RSI", value: "RSI", stack: false },
  { label: "KDJ", value: "KDJ", stack: false },
];

// 工具栏分组配置 - 完全匹配 KlineCharts Pro
const DRAWING_TOOL_GROUPS = [
  // 1. 线段工具
  {
    id: "singleLine",
    label: "线段工具",
    defaultIcon: "horizontalStraightLine",
    tools: [
      { id: "horizontalStraightLine", label: "水平直线", overlay: "horizontalStraightLine" },
      { id: "horizontalRayLine", label: "水平射线", overlay: "horizontalRayLine" },
      { id: "horizontalSegment", label: "水平线段", overlay: "horizontalSegment" },
      { id: "verticalStraightLine", label: "垂直直线", overlay: "verticalStraightLine" },
      { id: "verticalRayLine", label: "垂直射线", overlay: "verticalRayLine" },
      { id: "verticalSegment", label: "垂直线段", overlay: "verticalSegment" },
      { id: "straightLine", label: "直线", overlay: "straightLine" },
      { id: "rayLine", label: "射线", overlay: "rayLine" },
      { id: "segment", label: "线段", overlay: "segment" },
      { id: "priceLine", label: "价格线", overlay: "priceLine" },
    ],
  },
  // 2. 通道工具
  {
    id: "moreLine",
    label: "通道工具",
    defaultIcon: "priceChannelLine",
    tools: [
      { id: "priceChannelLine", label: "价格通道线", overlay: "priceChannelLine" },
      { id: "parallelStraightLine", label: "平行直线", overlay: "parallelStraightLine" },
    ],
  },
  // 3. 绘图（形状）
  {
    id: "polygon",
    label: "绘图",
    defaultIcon: "circle",
    tools: [
      { id: "circle", label: "圆", overlay: "circle" },
      { id: "rect", label: "矩形", overlay: "rect" },
      { id: "parallelogram", label: "平行四边形", overlay: "parallelogram" },
      { id: "triangle", label: "三角形", overlay: "triangle" },
    ],
  },
  // 4. 绘图（斐波那契）
  {
    id: "fibonacci",
    label: "绘图",
    defaultIcon: "fibonacciLine",
    tools: [
      { id: "fibonacciLine", label: "斐波那契回调直线", overlay: "fibonacciLine" },
      { id: "fibonacciSegment", label: "斐波那契回调线段", overlay: "fibonacciSegment" },
      { id: "fibonacciCircle", label: "斐波那契圆环", overlay: "fibonacciCircle" },
      { id: "fibonacciSpiral", label: "斐波那契螺旋", overlay: "fibonacciSpiral" },
      { id: "fibonacciSpeedResistanceFan", label: "斐波那契速度阻力扇", overlay: "fibonacciSpeedResistanceFan" },
      { id: "fibonacciExtension", label: "斐波那契趋势扩展", overlay: "fibonacciExtension" },
      { id: "gannBox", label: "江恩箱", overlay: "gannBox" },
    ],
  },
  // 5. 形态（波浪）
  {
    id: "wave",
    label: "形态",
    defaultIcon: "xabcd",
    tools: [
      { id: "xabcd", label: "XABCD形态", overlay: "xabcd" },
      { id: "abcd", label: "ABCD形态", overlay: "abcd" },
      { id: "threeWaves", label: "三浪", overlay: "threeWaves" },
      { id: "fiveWaves", label: "五浪", overlay: "fiveWaves" },
      { id: "eightWaves", label: "八浪", overlay: "eightWaves" },
      { id: "anyWaves", label: "任意浪", overlay: "anyWaves" },
    ],
  },
];

const TIMEZONE_OPTIONS = [
  { label: "本地", value: "local" },
  { label: "UTC", value: "UTC" },
  { label: "Asia/Shanghai", value: "Asia/Shanghai" },
];

const SYMBOL_OPTIONS = [
  { label: "BTC/USDT", value: "Binance:BTC/USDT", badge: "B", color: "#f59e0b" },
  { label: "ETH/USDT", value: "Binance:ETH/USDT", badge: "E", color: "#627eea" },
  { label: "BNB/USDT", value: "Binance:BNB/USDT", badge: "B", color: "#f3ba2f" },
  { label: "SOL/USDT", value: "Binance:SOL/USDT", badge: "S", color: "#00ffa3" },
  { label: "XRP/USDT", value: "Binance:XRP/USDT", badge: "X", color: "#23292f" },
  { label: "DOGE/USDT", value: "Binance:DOGE/USDT", badge: "D", color: "#c3a634" },
];

const RESOLUTION_TO_TIMEFRAME: Record<string, string> = {
  "1": "m1",
  "3": "m3",
  "5": "m5",
  "15": "m15",
  "30": "m30",
  "60": "h1",
  "240": "h4",
  "D": "d1",
  "1D": "d1",
  "W": "w1",
  "1W": "w1",
};

const RESOLUTION_TO_MS: Record<string, number> = {
  "1": 60_000,
  "3": 180_000,
  "5": 300_000,
  "15": 900_000,
  "30": 1_800_000,
  "60": 3_600_000,
  "240": 14_400_000,
  "D": 86_400_000,
  "1D": 86_400_000,
  "W": 604_800_000,
  "1W": 604_800_000,
};

const logKline = (...args: unknown[]) => {
  if (DEBUG_KLINE) {
    console.debug("[Klinecharts]", ...args);
  }
};

const getIndicatorPaneOptions = (option?: IndicatorOption) => {
  if (option?.pane === "main") {
    return { id: CANDLE_PANE_ID };
  }
  return undefined;
};

const MarketChart: React.FC<MarketChartProps> = ({
  symbol = DEFAULT_SYMBOL,
  interval = DEFAULT_INTERVAL,
  height = 420,
  theme = "light",
}) => {
  const shellRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<Chart | null>(null);
  const lastBarRef = useRef<KLineData | null>(null);
  const indicatorPaneRef = useRef(new Map<string, string>());
  const overlayIdsRef = useRef<string[]>([]);
  const httpRef = useRef(new HttpClient());
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const indicatorPanelRef = useRef<HTMLDivElement>(null);
  const timezonePanelRef = useRef<HTMLDivElement>(null);
  const settingsPanelRef = useRef<HTMLDivElement>(null);
  const symbolPanelRef = useRef<HTMLDivElement>(null);
  const [activeSymbol, setActiveSymbol] = useState(symbol);
  const [activeInterval, setActiveInterval] = useState(interval);
  const [activeIndicators, setActiveIndicators] = useState<string[]>([]);
  const [activeDrawing, setActiveDrawing] = useState<string>("cursor");
  const [reloadKey, setReloadKey] = useState(0);
  const [latestBar, setLatestBar] = useState<KLineData | null>(null);
  const [activeTimezone, setActiveTimezone] = useState("Asia/Shanghai");
  const [showIndicators, setShowIndicators] = useState(false);
  const [showTimezone, setShowTimezone] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [showSymbolSelector, setShowSymbolSelector] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [expandedToolGroup, setExpandedToolGroup] = useState<string | null>(null);
  const [toolListDirection, setToolListDirection] = useState<"down" | "up">("down");
  const [selectedToolPerGroup, setSelectedToolPerGroup] = useState<Record<string, string>>(() => {
    // 每个分组默认选中第一个工具
    const defaults: Record<string, string> = {};
    DRAWING_TOOL_GROUPS.forEach((group) => {
      if (group.tools.length > 0) {
        defaults[group.id] = group.defaultIcon || group.tools[0].id;
      }
    });
    return defaults;
  });
  const drawingBarRef = useRef<HTMLDivElement>(null);
  const expandedListRef = useRef<HTMLUListElement>(null);
  const [isDrawingLocked, setIsDrawingLocked] = useState(false);
  const [isDrawingVisible, setIsDrawingVisible] = useState(true);

  useEffect(() => {
    setActiveSymbol(symbol ?? DEFAULT_SYMBOL);
  }, [symbol]);

  useEffect(() => {
    setActiveInterval(interval ?? DEFAULT_INTERVAL);
  }, [interval]);

  const supportedIndicators = useMemo(() => new Set(getSupportedIndicators()), []);
  const indicatorOptions = useMemo(
    () => INDICATOR_OPTIONS.filter((option) => supportedIndicators.has(option.value)),
    [supportedIndicators]
  );
  // 显示所有绘图工具组（不过滤，保持完整的工具栏）
  const filteredToolGroups = DRAWING_TOOL_GROUPS;
  const displaySymbol = useMemo(() => {
    if (!activeSymbol) {
      return "";
    }
    const parts = activeSymbol.split(":");
    return parts.length > 1 ? parts[1] : parts[0];
  }, [activeSymbol]);

  useEffect(() => {
    if (!showIndicators && !showTimezone && !showSettings && !showSymbolSelector && !expandedToolGroup) {
      return undefined;
    }

    const handleClick = (event: MouseEvent) => {
      const target = event.target as Node;
      if (showIndicators && indicatorPanelRef.current && indicatorPanelRef.current.contains(target)) {
        return;
      }
      if (showTimezone && timezonePanelRef.current && timezonePanelRef.current.contains(target)) {
        return;
      }
      if (showSettings && settingsPanelRef.current && settingsPanelRef.current.contains(target)) {
        return;
      }
      if (showSymbolSelector && symbolPanelRef.current && symbolPanelRef.current.contains(target)) {
        return;
      }
      if (expandedToolGroup && drawingBarRef.current && drawingBarRef.current.contains(target)) {
        return;
      }
      setShowIndicators(false);
      setShowTimezone(false);
      setShowSettings(false);
      setShowSymbolSelector(false);
      setExpandedToolGroup(null);
    };

    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, [showIndicators, showTimezone, showSettings, showSymbolSelector, expandedToolGroup]);

  useLayoutEffect(() => {
    if (!expandedToolGroup || !expandedListRef.current) {
      return;
    }
    const listRect = expandedListRef.current.getBoundingClientRect();
    const bottomOverflow = listRect.bottom > window.innerHeight - 8;
    const topOverflow = listRect.top < 8;

    if (bottomOverflow && toolListDirection !== "up") {
      setToolListDirection("up");
      return;
    }
    if (topOverflow && toolListDirection !== "down") {
      setToolListDirection("down");
    }
  }, [expandedToolGroup, toolListDirection]);

  useEffect(() => {
    if (!containerRef.current) {
      return undefined;
    }

    return () => {
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
      if (chartRef.current) {
        dispose(chartRef.current);
        chartRef.current = null;
      }
    };
  }, []);

  useEffect(() => {
    if (!containerRef.current || chartRef.current) {
      return;
    }

    const chart = init(containerRef.current, { locale: "zh" });
    if (!chart) {
      return;
    }
    chartRef.current = chart;
    indicatorPaneRef.current.clear();
    overlayIdsRef.current = [];

    const resizeObserver = new ResizeObserver(() => chart.resize());
    resizeObserver.observe(containerRef.current);

    const defaults = ["BOLL", "MA", "VOL", "MACD"];
    const nextIndicators: string[] = [];
    for (const indicator of defaults) {
      if (!supportedIndicators.has(indicator)) {
        continue;
      }
      const option = INDICATOR_OPTIONS.find((item) => item.value === indicator);
      const paneId = chart.createIndicator(
        indicator,
        option?.stack ?? false,
        getIndicatorPaneOptions(option)
      );
      if (paneId) {
        indicatorPaneRef.current.set(indicator, paneId);
        nextIndicators.push(indicator);
      }
    }
    if (nextIndicators.length > 0) {
      setActiveIndicators(nextIndicators);
    }

    return () => {
      resizeObserver.disconnect();
    };
  }, [supportedIndicators]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles(theme === "dark" ? "dark" : "light");
  }, [theme]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const timezone = activeTimezone === "local"
      ? Intl.DateTimeFormat().resolvedOptions().timeZone
      : activeTimezone;
    chart.setTimezone(timezone);
  }, [activeTimezone]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    const parsed = parseSymbolName(activeSymbol);
    if (!parsed) {
      chart.clearData();
      setLatestBar(null);
      return;
    }

    const timeframe = RESOLUTION_TO_TIMEFRAME[activeInterval] ?? "m1";
    const intervalMs = RESOLUTION_TO_MS[activeInterval] ?? 60_000;

    chart.clearData();
    lastBarRef.current = null;
    setLatestBar(null);

    const controller = new AbortController();
    let disposed = false;

    const loadHistory = async () => {
      try {
        logKline("loadHistory:init", { symbol: parsed.symbol, timeframe, count: DEFAULT_COUNT });
        const bars = await fetchHistory({
          http: httpRef.current,
          exchange: parsed.exchange,
          symbol: parsed.symbol,
          timeframe,
          intervalMs,
          count: DEFAULT_COUNT,
          signal: controller.signal,
        });
        if (disposed) {
          return;
        }
        logKline("loadHistory:done", {
          count: bars.length,
          first: bars[0]?.timestamp,
          last: bars[bars.length - 1]?.timestamp,
        });
        chart.applyNewData(bars);
        if (bars.length > 0) {
          lastBarRef.current = bars[bars.length - 1];
          setLatestBar(bars[bars.length - 1]);
        }
      } catch (error) {
        if (!disposed) {
          console.error("[Klinecharts] Failed to load history", error);
        }
      }
    };

    loadHistory();

    chart.setLoadDataCallback(async (params) => {
      if (disposed) {
        return;
      }
      if (!params.data) {
        logKline("loadMore:skip:no-anchor", { type: params.type });
        params.callback([], false);
        return;
      }
      if (params.type === LoadDataType.Forward) {
        const endTime = params.data.timestamp - intervalMs;
        if (!Number.isFinite(endTime) || endTime <= 0) {
          logKline("loadMore:skip:invalid-end", { endTime });
          params.callback([], false);
          return;
        }
        try {
          logKline("loadMore:backward", {
            symbol: parsed.symbol,
            timeframe,
            endTime,
            count: DEFAULT_COUNT,
          });
          const bars = await fetchHistory({
            http: httpRef.current,
            exchange: parsed.exchange,
            symbol: parsed.symbol,
            timeframe,
            intervalMs,
            count: DEFAULT_COUNT,
            endTime,
            signal: controller.signal,
          });
          const more = bars.length === DEFAULT_COUNT;
          logKline("loadMore:done", { count: bars.length, more });
          params.callback(bars, more);
        } catch (error) {
          if (!disposed) {
            console.error("[Klinecharts] Failed to load more history", error);
          }
          params.callback([], false);
        }
        return;
      }
      if (params.type === LoadDataType.Backward) {
        logKline("loadMore:forward:skip", { reason: "live-ticks", last: params.data.timestamp });
        params.callback([], false);
      }
    });

    unsubscribeRef.current?.();
    unsubscribeRef.current = subscribeMarket([parsed.symbol], (ticks) => {
      if (disposed) {
        return;
      }
      for (const tick of ticks) {
        if (tick.symbol !== parsed.symbol) {
          continue;
        }
        const nextBar = updateBar(lastBarRef.current, intervalMs, tick.price, tick.ts);
        if (!nextBar) {
          continue;
        }
        lastBarRef.current = nextBar;
        setLatestBar(nextBar);
        chart.updateData(nextBar);
      }
    });

    return () => {
      disposed = true;
      controller.abort();
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
    };
  }, [activeSymbol, activeInterval, reloadKey]);

  useEffect(() => {
    const handleFullscreenChange = () => {
      setIsFullscreen(Boolean(document.fullscreenElement));
    };
    document.addEventListener("fullscreenchange", handleFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", handleFullscreenChange);
  }, []);

  const handleToggleIndicator = (option: IndicatorOption) => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const paneId = indicatorPaneRef.current.get(option.value);
    if (paneId) {
      chart.removeIndicator(paneId, option.value);
      indicatorPaneRef.current.delete(option.value);
      setActiveIndicators((prev) => prev.filter((name) => name !== option.value));
      return;
    }
    const createdPaneId = chart.createIndicator(
      option.value,
      option.stack,
      getIndicatorPaneOptions(option)
    );
    if (createdPaneId) {
      indicatorPaneRef.current.set(option.value, createdPaneId);
      setActiveIndicators((prev) => [...prev, option.value]);
    }
  };

  const handleStartDrawing = (overlay: string, id: string) => {
    setActiveDrawing(id);
    if (!overlay) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    // klinecharts 9.x 需要传入对象格式
    const created = chart.createOverlay({
      name: overlay,
      lock: isDrawingLocked,
      visible: isDrawingVisible,
    });
    if (created) {
      overlayIdsRef.current = [...overlayIdsRef.current, created as string];
    }
  };

  const handleClearDrawings = () => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    for (const id of overlayIdsRef.current) {
      chart.removeOverlay(id);
    }
    overlayIdsRef.current = [];
    setActiveDrawing("cursor");
  };

  const handleScreenshot = () => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const url = chart.getConvertPictureUrl(true);
    if (!url) {
      return;
    }
    const link = document.createElement("a");
    link.href = url;
    link.download = `${displaySymbol || "chart"}.png`;
    link.click();
  };

  const handleFullscreen = async () => {
    if (!shellRef.current) {
      return;
    }
    if (!document.fullscreenElement) {
      await shellRef.current.requestFullscreen();
    } else {
      await document.exitFullscreen();
    }
  };

  const formattedTime = latestBar ? formatDisplayTime(latestBar.timestamp) : "--";
  const openText = latestBar ? formatNumber(latestBar.open) : "--";
  const highText = latestBar ? formatNumber(latestBar.high) : "--";
  const lowText = latestBar ? formatNumber(latestBar.low) : "--";
  const closeText = latestBar ? formatNumber(latestBar.close) : "--";
  const volumeText = latestBar ? formatNumber(latestBar.volume ?? 0) : "--";
  const showBoll = activeIndicators.includes("BOLL");

  const currentSymbolOption = SYMBOL_OPTIONS.find((opt) => opt.value === activeSymbol) || SYMBOL_OPTIONS[0];

  return (
    <div ref={shellRef} className="market-chart-shell">
      <div className="market-chart-header">
        <div className="market-chart-header-left">
          <div className="market-chart-symbol-selector">
            <button
              type="button"
              className={`market-chart-symbol-button ${showSymbolSelector ? "is-active" : ""}`}
              onClick={() => {
                setShowSymbolSelector((prev) => !prev);
                setShowIndicators(false);
                setShowTimezone(false);
                setShowSettings(false);
              }}
            >
              <span
                className="market-chart-symbol-badge"
                style={{ background: currentSymbolOption.color }}
              >
                {currentSymbolOption.badge}
              </span>
              <span className="market-chart-symbol-text">{displaySymbol || activeSymbol}</span>
              <svg className="market-chart-symbol-arrow" viewBox="0 0 12 12" width="12" height="12">
                <path d="M3 4.5L6 7.5L9 4.5" stroke="currentColor" strokeWidth="1.5" fill="none" />
              </svg>
            </button>
            {showSymbolSelector && (
              <div ref={symbolPanelRef} className="market-chart-symbol-popover">
                <div className="market-chart-popover-title">选择交易对</div>
                <div className="market-chart-symbol-list">
                  {SYMBOL_OPTIONS.map((opt) => (
                    <button
                      key={opt.value}
                      type="button"
                      className={`market-chart-symbol-item ${activeSymbol === opt.value ? "is-active" : ""}`}
                      onClick={() => {
                        setActiveSymbol(opt.value);
                        setShowSymbolSelector(false);
                      }}
                    >
                      <span className="market-chart-symbol-badge" style={{ background: opt.color }}>
                        {opt.badge}
                      </span>
                      <span>{opt.label}</span>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-periods">
            {PERIOD_TABS.map((tab) => (
              <button
                key={tab.label}
                type="button"
                className={`market-chart-period ${
                  activeInterval === tab.value ? "is-active" : ""
                } ${tab.disabled ? "is-disabled" : ""}`}
                onClick={() => {
                  if (tab.disabled) {
                    return;
                  }
                  setActiveInterval(tab.value);
                }}
                disabled={tab.disabled}
              >
                {tab.label}
              </button>
            ))}
          </div>
        </div>
        <div className="market-chart-header-right">
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showIndicators ? "is-active" : ""}`}
              onClick={() => {
                setShowIndicators((prev) => !prev);
                setShowTimezone(false);
                setShowSettings(false);
              }}
            >
              <span className="market-chart-action-icon">指标</span>
            </button>
            {showIndicators && (
              <div ref={indicatorPanelRef} className="market-chart-popover">
                <div className="market-chart-popover-title">指标</div>
                <div className="market-chart-popover-grid">
                  {indicatorOptions.map((option) => {
                    const isActive = activeIndicators.includes(option.value);
                    return (
                      <button
                        key={option.value}
                        type="button"
                        className={`market-chart-popover-item ${isActive ? "is-active" : ""}`}
                        onClick={() => handleToggleIndicator(option)}
                      >
                        {option.label}
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showTimezone ? "is-active" : ""}`}
              onClick={() => {
                setShowTimezone((prev) => !prev);
                setShowIndicators(false);
                setShowSettings(false);
              }}
            >
              <span className="market-chart-action-icon">时区</span>
            </button>
            {showTimezone && (
              <div ref={timezonePanelRef} className="market-chart-popover">
                <div className="market-chart-popover-title">时区</div>
                <div className="market-chart-popover-list">
                  {TIMEZONE_OPTIONS.map((option) => (
                    <button
                      key={option.value}
                      type="button"
                      className={`market-chart-popover-item ${
                        activeTimezone === option.value ? "is-active" : ""
                      }`}
                      onClick={() => setActiveTimezone(option.value)}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showSettings ? "is-active" : ""}`}
              onClick={() => {
                setShowSettings((prev) => !prev);
                setShowIndicators(false);
                setShowTimezone(false);
              }}
            >
              <span className="market-chart-action-icon">设置</span>
            </button>
            {showSettings && (
              <div ref={settingsPanelRef} className="market-chart-popover">
                <div className="market-chart-popover-title">设置</div>
                <div className="market-chart-popover-list">
                  <button type="button" className="market-chart-popover-item" onClick={handleClearDrawings}>
                    清除绘图
                  </button>
                  <button
                    type="button"
                    className="market-chart-popover-item"
                    onClick={() => setReloadKey((prev) => prev + 1)}
                  >
                    重载数据
                  </button>
                </div>
              </div>
            )}
          </div>
          <button type="button" className="market-chart-action" onClick={handleScreenshot}>
            <span className="market-chart-action-icon">截图</span>
          </button>
          <button
            type="button"
            className={`market-chart-action ${isFullscreen ? "is-active" : ""}`}
            onClick={handleFullscreen}
          >
            <span className="market-chart-action-icon">全屏</span>
          </button>
        </div>
      </div>
      <div className="market-chart-body" style={{ height }}>
        <div ref={drawingBarRef} className="klinecharts-pro-drawing-bar">
          {filteredToolGroups.map((group) => {
            const selectedToolId = selectedToolPerGroup[group.id] || group.defaultIcon || group.tools[0]?.id;
            const selectedTool = group.tools.find((t) => t.id === selectedToolId) || group.tools[0];
            const isExpanded = expandedToolGroup === group.id;
            const isGroupActive = group.tools.some((t) => t.id === activeDrawing);

            return (
              <div key={group.id} className="item" tabIndex={0}>
                <span
                  className={`icon-overlay ${isGroupActive ? "selected" : ""}`}
                  onClick={() => {
                    if (selectedTool) {
                      handleStartDrawing(selectedTool.overlay, selectedTool.id);
                    }
                  }}
                  title={selectedTool?.label || group.label}
                >
                  <ToolIcon id={selectedTool?.id || group.defaultIcon} />
                </span>
                <div
                  className="icon-arrow"
                  onClick={() => {
                    if (isExpanded) {
                      setExpandedToolGroup(null);
                      return;
                    }
                    setToolListDirection("down");
                    setExpandedToolGroup(group.id);
                  }}
                >
                  <svg viewBox="0 0 4 6" className={isExpanded ? "rotate" : ""}>
                    <path
                      d="M1.07298,0.159458C0.827521,-0.0531526,0.429553,-0.0531526,0.184094,0.159458C-0.0613648,0.372068,-0.0613648,0.716778,0.184094,0.929388L2.61275,3.03303L0.260362,5.07061C0.0149035,5.28322,0.0149035,5.62793,0.260362,5.84054C0.505822,6.05315,0.903789,6.05315,1.14925,5.84054L3.81591,3.53075C4.01812,3.3556,4.05374,3.0908,3.92279,2.88406C3.93219,2.73496,3.87113,2.58315,3.73964,2.46925L1.07298,0.159458Z"
                      fill="currentColor"
                    />
                  </svg>
                </div>
                {isExpanded && (
                  <ul
                    ref={expandedListRef}
                    className={`list ${toolListDirection === "up" ? "is-up" : ""}`}
                  >
                    {group.tools.map((tool) => {
                      const isToolActive = activeDrawing === tool.id;
                      return (
                        <li
                          key={tool.id}
                          onClick={() => {
                            setSelectedToolPerGroup((prev) => ({ ...prev, [group.id]: tool.id }));
                            handleStartDrawing(tool.overlay, tool.id);
                            setExpandedToolGroup(null);
                          }}
                        >
                          <span className={`icon-overlay ${isToolActive ? "selected" : ""}`}>
                            <ToolIcon id={tool.id} />
                          </span>
                          <span style={{ paddingLeft: 8 }}>{tool.label}</span>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>
            );
          })}
          <span className="split-line" />
          <div className="item" tabIndex={0}>
            <span
              className={`icon-overlay ${isDrawingLocked ? "selected" : ""}`}
              onClick={() => setIsDrawingLocked((prev) => !prev)}
              title={isDrawingLocked ? "解锁" : "锁定"}
            >
              <ToolIcon id={isDrawingLocked ? "lock" : "unlock"} />
            </span>
          </div>
          <div className="item" tabIndex={0}>
            <span
              className={`icon-overlay ${!isDrawingVisible ? "selected" : ""}`}
              onClick={() => setIsDrawingVisible((prev) => !prev)}
              title={isDrawingVisible ? "隐藏" : "显示"}
            >
              <ToolIcon id={isDrawingVisible ? "visible" : "invisible"} />
            </span>
          </div>
          <div className="item" tabIndex={0}>
            <span
              className="icon-overlay"
              onClick={handleClearDrawings}
              title="清除全部"
            >
              <ToolIcon id="trash" />
            </span>
          </div>
        </div>
        <div className="market-chart-stage">
          <div className="market-chart-info">
            <span>时间: {formattedTime}</span>
            <span>开: {openText}</span>
            <span>高: {highText}</span>
            <span>低: {lowText}</span>
            <span>收: {closeText}</span>
            <span>成交量: {volumeText}</span>
          </div>
          {showBoll && (
            <div className="market-chart-indicators">
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-up" /> BOLL(20,2)
              </span>
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-mid" /> MID
              </span>
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-low" /> DN
              </span>
            </div>
          )}
          <div
            className={`market-chart-wrapper ${
              theme === "dark" ? "market-chart-wrapper--dark" : "market-chart-wrapper--light"
            }`}
          >
            <div ref={containerRef} className="market-chart-container" />
            <div className="market-chart-currency">USD</div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default MarketChart;

type OHLCV = {
  timestamp?: number | null;
  open?: number | null;
  high?: number | null;
  low?: number | null;
  close?: number | null;
  volume?: number | null;
};

function parseSymbolName(symbolName: string): { exchange: string; symbol: string } | null {
  if (!symbolName) {
    return null;
  }
  if (symbolName.includes(":")) {
    const [exchange, symbol] = symbolName.split(":");
    if (!exchange || !symbol) {
      return null;
    }
    return { exchange, symbol };
  }
  return { exchange: "Binance", symbol: symbolName };
}

function toSymbolEnum(symbol: string): string {
  return symbol.replace("/", "_").replace("-", "_").toUpperCase();
}

async function fetchHistory(options: {
  http: HttpClient;
  exchange: string;
  symbol: string;
  timeframe: string;
  intervalMs: number;
  count: number;
  endTime?: number;
  startTime?: number;
  signal: AbortSignal;
}): Promise<KLineData[]> {
  const endTime = options.endTime ?? Date.now();
  const startTime = options.startTime ?? endTime - options.intervalMs * (options.count - 1);
  const query: Record<string, string | number> = {
    exchange: options.exchange,
    timeframe: options.timeframe,
    symbol: toSymbolEnum(options.symbol),
    count: options.count,
    startTime: formatDateTime(startTime),
    endTime: formatDateTime(endTime),
  };

  const data = await options.http.get<OHLCV[]>("/api/MarketData/history", query, { signal: options.signal });
  return data
    .map((item) => toKLine(item))
    .filter((bar): bar is KLineData => bar !== null)
    .sort((a, b) => a.timestamp - b.timestamp);
}

function toKLine(item: OHLCV): KLineData | null {
  if (!item.timestamp) {
    return null;
  }
  const open = item.open ?? item.close ?? 0;
  const high = item.high ?? open;
  const low = item.low ?? open;
  const close = item.close ?? open;
  return {
    timestamp: item.timestamp,
    open,
    high,
    low,
    close,
    volume: item.volume ?? 0,
  };
}

function updateBar(lastBar: KLineData | null, intervalMs: number, price: number, ts: number): KLineData | null {
  const barTime = Math.floor(ts / intervalMs) * intervalMs;
  if (!lastBar || barTime > lastBar.timestamp) {
    return {
      timestamp: barTime,
      open: price,
      high: price,
      low: price,
      close: price,
      volume: 0,
    };
  }
  if (barTime < lastBar.timestamp) {
    return null;
  }
  return {
    ...lastBar,
    high: Math.max(lastBar.high, price),
    low: Math.min(lastBar.low, price),
    close: price,
  };
}

function formatDateTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function formatDisplayTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function formatNumber(value: number): string {
  if (!Number.isFinite(value)) {
    return "--";
  }
  if (Math.abs(value) >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(2)}M`;
  }
  if (Math.abs(value) >= 1_000) {
    return `${(value / 1_000).toFixed(2)}K`;
  }
  return value.toFixed(2);
}

function ToolIcon({ id }: { id: string }) {
  switch (id) {
    // 水平线工具
    case "horizontalStraightLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="3" y1="12" x2="21" y2="12" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "horizontalRayLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="6" y1="12" x2="21" y2="12" stroke="currentColor" strokeWidth="2" />
          <circle cx="6" cy="12" r="2" fill="currentColor" />
        </svg>
      );
    case "horizontalSegment":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="5" y1="12" x2="19" y2="12" stroke="currentColor" strokeWidth="2" />
          <circle cx="5" cy="12" r="2" fill="currentColor" />
          <circle cx="19" cy="12" r="2" fill="currentColor" />
        </svg>
      );
    // 垂直线工具
    case "verticalStraightLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="12" y1="3" x2="12" y2="21" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "verticalRayLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="12" y1="6" x2="12" y2="21" stroke="currentColor" strokeWidth="2" />
          <circle cx="12" cy="6" r="2" fill="currentColor" />
        </svg>
      );
    case "verticalSegment":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="12" y1="5" x2="12" y2="19" stroke="currentColor" strokeWidth="2" />
          <circle cx="12" cy="5" r="2" fill="currentColor" />
          <circle cx="12" cy="19" r="2" fill="currentColor" />
        </svg>
      );
    // 斜线工具
    case "straightLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="20" x2="20" y2="4" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "rayLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="6" y1="18" x2="20" y2="4" stroke="currentColor" strokeWidth="2" />
          <circle cx="6" cy="18" r="2" fill="currentColor" />
        </svg>
      );
    case "segment":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="5" y1="19" x2="19" y2="5" stroke="currentColor" strokeWidth="2" />
          <circle cx="5" cy="19" r="2" fill="currentColor" />
          <circle cx="19" cy="5" r="2" fill="currentColor" />
        </svg>
      );
    case "priceLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="3" y1="12" x2="17" y2="12" stroke="currentColor" strokeWidth="2" />
          <rect x="17" y="9" width="4" height="6" rx="1" fill="currentColor" />
        </svg>
      );
    // 平行线工具
    case "priceChannelLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="8" x2="20" y2="5" stroke="currentColor" strokeWidth="1.5" />
          <line x1="4" y1="16" x2="20" y2="13" stroke="currentColor" strokeWidth="1.5" />
          <line x1="4" y1="19" x2="20" y2="16" stroke="currentColor" strokeWidth="1.5" />
        </svg>
      );
    case "parallelStraightLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="8" x2="20" y2="8" stroke="currentColor" strokeWidth="2" />
          <line x1="4" y1="16" x2="20" y2="16" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    // 形状工具
    case "circle":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="8" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "rect":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="4" y="6" width="16" height="12" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "parallelogram":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polygon points="6,18 10,6 18,6 14,18" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "triangle":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polygon points="12,5 4,19 20,19" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    // 斐波那契工具
    case "fibonacciLine":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="5" x2="20" y2="5" stroke="currentColor" strokeWidth="1.5" />
          <line x1="4" y1="9" x2="20" y2="9" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <line x1="4" y1="13" x2="20" y2="13" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <line x1="4" y1="17" x2="20" y2="17" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <line x1="4" y1="19" x2="20" y2="19" stroke="currentColor" strokeWidth="1.5" />
        </svg>
      );
    case "fibonacciSegment":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="18" x2="20" y2="6" stroke="currentColor" strokeWidth="2" />
          <line x1="4" y1="14" x2="16" y2="6" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <line x1="4" y1="10" x2="12" y2="6" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
        </svg>
      );
    case "fibonacciCircle":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <circle cx="12" cy="12" r="6" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" fill="none" />
          <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" fill="none" />
        </svg>
      );
    case "fibonacciSpiral":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 12 Q16 12 16 8 Q16 4 12 4 Q6 4 6 10 Q6 18 14 18 Q20 18 20 12" stroke="currentColor" strokeWidth="1.5" fill="none" />
        </svg>
      );
    case "fibonacciSpeedResistanceFan":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="20" x2="20" y2="4" stroke="currentColor" strokeWidth="1.5" />
          <line x1="4" y1="20" x2="20" y2="10" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <line x1="4" y1="20" x2="20" y2="16" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
          <circle cx="4" cy="20" r="1.5" fill="currentColor" />
        </svg>
      );
    case "fibonacciExtension":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <line x1="4" y1="18" x2="12" y2="6" stroke="currentColor" strokeWidth="2" />
          <line x1="12" y1="6" x2="20" y2="14" stroke="currentColor" strokeWidth="2" />
          <line x1="4" y1="4" x2="20" y2="4" stroke="currentColor" strokeWidth="1" strokeDasharray="2,2" />
        </svg>
      );
    case "gannBox":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="4" y="4" width="16" height="16" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <line x1="4" y1="4" x2="20" y2="20" stroke="currentColor" strokeWidth="1" />
          <line x1="12" y1="4" x2="12" y2="20" stroke="currentColor" strokeWidth="1" />
          <line x1="4" y1="12" x2="20" y2="12" stroke="currentColor" strokeWidth="1" />
        </svg>
      );
    // 波浪工具
    case "xabcd":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="3,16 7,8 11,14 15,6 21,12" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <circle cx="3" cy="16" r="1.5" fill="currentColor" />
          <circle cx="7" cy="8" r="1.5" fill="currentColor" />
          <circle cx="11" cy="14" r="1.5" fill="currentColor" />
          <circle cx="15" cy="6" r="1.5" fill="currentColor" />
          <circle cx="21" cy="12" r="1.5" fill="currentColor" />
        </svg>
      );
    case "abcd":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="4,16 8,6 16,14 20,4" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <circle cx="4" cy="16" r="1.5" fill="currentColor" />
          <circle cx="8" cy="6" r="1.5" fill="currentColor" />
          <circle cx="16" cy="14" r="1.5" fill="currentColor" />
          <circle cx="20" cy="4" r="1.5" fill="currentColor" />
        </svg>
      );
    case "threeWaves":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="4,18 10,6 14,14 20,4" stroke="currentColor" strokeWidth="1.5" fill="none" />
        </svg>
      );
    case "fiveWaves":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="2,16 5,8 8,12 12,4 16,10 20,6" stroke="currentColor" strokeWidth="1.5" fill="none" />
        </svg>
      );
    case "eightWaves":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="2,14 4,10 6,12 8,6 10,10 13,4 16,8 19,6 22,10" stroke="currentColor" strokeWidth="1.5" fill="none" />
        </svg>
      );
    case "anyWaves":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <polyline points="3,15 7,8 11,13 15,6 19,11 22,8" stroke="currentColor" strokeWidth="1.5" fill="none" strokeDasharray="3,2" />
        </svg>
      );
    // 功能图标
    case "lock":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="6" y="11" width="12" height="9" rx="1" stroke="currentColor" strokeWidth="2" fill="none" />
          <path d="M8 11V7a4 4 0 0 1 8 0v4" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "unlock":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="6" y="11" width="12" height="9" rx="1" stroke="currentColor" strokeWidth="2" fill="none" />
          <path d="M8 11V7a4 4 0 0 1 8 0" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "visible":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <ellipse cx="12" cy="12" rx="9" ry="5" stroke="currentColor" strokeWidth="2" fill="none" />
          <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "invisible":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <ellipse cx="12" cy="12" rx="9" ry="5" stroke="currentColor" strokeWidth="2" fill="none" />
          <line x1="4" y1="20" x2="20" y2="4" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "trash":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="7" y="9" width="10" height="11" rx="1" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <line x1="5" y1="6" x2="19" y2="6" stroke="currentColor" strokeWidth="1.5" />
          <line x1="10" y1="6" x2="10" y2="4" stroke="currentColor" strokeWidth="1.5" />
          <line x1="14" y1="6" x2="14" y2="4" stroke="currentColor" strokeWidth="1.5" />
          <line x1="10" y1="12" x2="10" y2="17" stroke="currentColor" strokeWidth="1" />
          <line x1="14" y1="12" x2="14" y2="17" stroke="currentColor" strokeWidth="1" />
        </svg>
      );
    default:
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="6" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
  }
}
