import React, { useEffect, useRef } from "react";
import { TradingViewDatafeed } from "./TradingViewDatafeed";
import "./MarketChart.css";

declare global {
  interface Window {
    TradingView?: {
      widget: new (options: Record<string, unknown>) => {
        remove: () => void;
      };
    };
  }
}

type MarketChartProps = {
  symbol?: string;
  interval?: string;
  height?: string | number;
  theme?: "light" | "dark";
};

const DEFAULT_SYMBOL = "Binance:BTC/USDT";
const DEFAULT_INTERVAL = "1";

const MarketChart: React.FC<MarketChartProps> = ({
  symbol = DEFAULT_SYMBOL,
  interval = DEFAULT_INTERVAL,
  height = 420,
  theme = "light",
}) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const widgetRef = useRef<{ remove: () => void } | null>(null);
  const datafeedRef = useRef<TradingViewDatafeed | null>(null);

  useEffect(() => {
    let disposed = false;
    datafeedRef.current = new TradingViewDatafeed();

    const initWidget = () => {
      if (disposed || !containerRef.current || !window.TradingView) {
        return;
      }

      widgetRef.current?.remove();

      widgetRef.current = new window.TradingView.widget({
        symbol,
        interval,
        container: containerRef.current,
        datafeed: datafeedRef.current,
        library_path: "/charting_library/",
        locale: "zh",
        autosize: true,
        theme,
        disabled_features: ["use_localstorage_for_settings"],
        enabled_features: ["study_templates"],
      });
    };

    loadTradingView().then(initWidget);

    return () => {
      disposed = true;
      widgetRef.current?.remove();
      widgetRef.current = null;
      datafeedRef.current?.destroy();
      datafeedRef.current = null;
    };
  }, [symbol, interval, theme]);

  return (
    <div className="market-chart-wrapper" style={{ height }}>
      <div ref={containerRef} className="market-chart-container" />
    </div>
  );
};

export default MarketChart;

let tvScriptPromise: Promise<void> | null = null;

function loadTradingView(): Promise<void> {
  if (window.TradingView) {
    return Promise.resolve();
  }

  if (tvScriptPromise) {
    return tvScriptPromise;
  }

  tvScriptPromise = new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "/charting_library/charting_library.standalone.js";
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Failed to load charting library"));
    document.head.appendChild(script);
  });

  return tvScriptPromise;
}
