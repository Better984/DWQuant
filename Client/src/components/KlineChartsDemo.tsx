import React, { useEffect, useMemo, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { dispose, init } from 'klinecharts';
import './KlineChartsDemo.css';

const KlineChartsDemo: React.FC = () => {
  const navigate = useNavigate();
  const containerRef = useRef<HTMLDivElement | null>(null);

  const data = useMemo(() => {
    const start = Date.UTC(2025, 0, 1);
    const candles = [];
    let base = 100;
    for (let i = 0; i < 40; i += 1) {
      const open = base + (Math.sin(i / 3) * 2);
      const close = open + (Math.cos(i / 5) * 2);
      const high = Math.max(open, close) + 1.5;
      const low = Math.min(open, close) - 1.5;
      candles.push({
        timestamp: start + i * 24 * 60 * 60 * 1000,
        open: Number(open.toFixed(2)),
        high: Number(high.toFixed(2)),
        low: Number(low.toFixed(2)),
        close: Number(close.toFixed(2)),
        volume: 1000 + i * 20,
      });
      base = close;
    }
    return candles;
  }, []);

  useEffect(() => {
    if (!containerRef.current) {
      return undefined;
    }

    const chart = init(containerRef.current);
    if (!chart) {
      return undefined;
    }
    chart.applyNewData(data);

    return () => {
      dispose(chart);
    };
  }, [data]);

  return (
    <div className="kline-demo-page">
      <div className="kline-demo-header">
        <h1 className="kline-demo-title">Klinecharts 示范</h1>
        <button type="button" className="kline-demo-back" onClick={() => navigate('/')}>
          返回首页
        </button>
      </div>
      <div className="kline-demo-card">
        <div className="kline-demo-chart" ref={containerRef} />
      </div>
    </div>
  );
};

export default KlineChartsDemo;
