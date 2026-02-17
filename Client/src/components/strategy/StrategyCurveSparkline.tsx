import React, { useMemo } from 'react';
import './StrategyCurveSparkline.css';

export type StrategyCurveSparklineProps = {
  series?: number[] | null;
  curveSource?: string | null;
  isBacktestCurve?: boolean;
};

const WIDTH = 112;
const HEIGHT = 36;

const StrategyCurveSparkline: React.FC<StrategyCurveSparklineProps> = ({
  series,
  curveSource,
  isBacktestCurve,
}) => {
  const normalizedSeries = useMemo(() => {
    if (!Array.isArray(series) || series.length === 0) {
      return [0, 0];
    }

    const values = series
      .map((item) => (Number.isFinite(item) ? Number(item) : 0))
      .filter((item) => Number.isFinite(item));
    if (values.length === 0) {
      return [0, 0];
    }

    return values.length === 1 ? [values[0], values[0]] : values;
  }, [series]);

  const points = useMemo(() => {
    const min = Math.min(...normalizedSeries);
    const max = Math.max(...normalizedSeries);
    const range = max - min || 1;
    return normalizedSeries
      .map((value, index) => {
        const x = (index / (normalizedSeries.length - 1)) * WIDTH;
        const y = HEIGHT - ((value - min) / range) * HEIGHT;
        return `${x.toFixed(2)},${y.toFixed(2)}`;
      })
      .join(' ');
  }, [normalizedSeries]);

  const areaPoints = useMemo(() => `0,${HEIGHT} ${points} ${WIDTH},${HEIGHT}`, [points]);
  const delta = normalizedSeries[normalizedSeries.length - 1] - normalizedSeries[0];
  const trendClass = delta > 0 ? 'is-up' : delta < 0 ? 'is-down' : 'is-flat';
  const isBacktest = isBacktestCurve || curveSource?.toLowerCase() === 'backtest';

  return (
    <div className={`strategy-curve-sparkline ${trendClass}`}>
      <div className="strategy-curve-sparkline__meta">
        <span className="strategy-curve-sparkline__window">30日</span>
        <span className={`strategy-curve-sparkline__source ${isBacktest ? 'is-backtest' : 'is-live'}`}>
          {isBacktest ? '回测数据' : '实盘数据'}
        </span>
      </div>
      <svg className="strategy-curve-sparkline__chart" viewBox={`0 0 ${WIDTH} ${HEIGHT}`} preserveAspectRatio="none">
        <polygon className="strategy-curve-sparkline__area" points={areaPoints} />
        <polyline className="strategy-curve-sparkline__line" points={points} />
      </svg>
    </div>
  );
};

export default StrategyCurveSparkline;
