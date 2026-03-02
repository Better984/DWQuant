import React, { useEffect, useMemo, useRef, useState } from 'react';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';
import './IndicatorModule.css';
import { getIndicatorLatest, type IndicatorLatestItem } from '../../network/indicatorClient';

interface ChartPalette {
  textPrimary: string;
  textSecondary: string;
  axisLine: string;
  splitLine: string;
  tooltipBg: string;
  tooltipBorder: string;
  colorPrimary: string;
  colorSecondary: string;
  colorTertiary: string;
  colorDanger: string;
  colorWarning: string;
  colorSuccess: string;
}

interface IndicatorCardData {
  id: string;
  name: string;
  category: string;
  sample: string;
  description: string;
  note: string;
  chartOption: EChartsOption;
  chartWrapClassName?: string;
}

interface FearGreedRuntime {
  value: number;
  classification: string;
  stale: boolean;
  sourceTs?: number;
}

interface EtfFlowRuntime {
  latestNetFlowUsd: number;
  stale: boolean;
  sourceTs?: number;
  series: Array<{
    ts: number;
    netFlowUsd: number;
  }>;
}

interface LiquidationCandlestick {
  ts: number;
  open: number;
  high: number;
  low: number;
  close: number;
}

interface LiquidationHeatmapRuntime {
  exchange: string;
  symbol: string;
  range: string;
  stale: boolean;
  sourceTs?: number;
  yAxis: number[];
  xAxisTimestamps: number[];
  points: Array<{
    xIndex: number;
    yIndex: number;
    liquidationUsd: number;
  }>;
  candlesticks: LiquidationCandlestick[];
  currentPrice?: number;
  maxLiquidationUsd: number;
}

const createPalette = (isDarkMode: boolean): ChartPalette => ({
  textPrimary: isDarkMode ? '#F3F4F6' : '#0F172A',
  textSecondary: isDarkMode ? 'rgba(243, 244, 246, 0.72)' : '#64748B',
  axisLine: isDarkMode ? 'rgba(148, 163, 184, 0.45)' : 'rgba(15, 23, 42, 0.18)',
  splitLine: isDarkMode ? 'rgba(148, 163, 184, 0.18)' : 'rgba(15, 23, 42, 0.08)',
  tooltipBg: isDarkMode ? 'rgba(15, 23, 42, 0.95)' : 'rgba(255, 255, 255, 0.96)',
  tooltipBorder: isDarkMode ? 'rgba(148, 163, 184, 0.5)' : 'rgba(15, 23, 42, 0.16)',
  colorPrimary: '#3B82F6',
  colorSecondary: '#06B6D4',
  colorTertiary: '#A855F7',
  colorDanger: '#EF4444',
  colorWarning: '#F59E0B',
  colorSuccess: '#22C55E',
});

const buildTooltip = (palette: ChartPalette): EChartsOption['tooltip'] => ({
  trigger: 'axis',
  backgroundColor: palette.tooltipBg,
  borderColor: palette.tooltipBorder,
  borderWidth: 1,
  textStyle: {
    color: palette.textPrimary,
  },
});

const formatCompactUsd = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  if (Math.abs(value) >= 100_000_000) {
    return `${(value / 100_000_000).toFixed(2)}亿 USD`;
  }
  if (Math.abs(value) >= 10_000) {
    return `${(value / 10_000).toFixed(2)}万 USD`;
  }

  return `${value.toFixed(2)} USD`;
};

const formatHeatmapAxisTime = (ts: number): string => {
  const date = new Date(ts);
  const day = date.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' });
  const time = date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${day} ${time}`;
};

const LIQUIDATION_BUCKET_COUNT = 100;

const clampNumber = (value: number, min: number, max: number): number => Math.max(min, Math.min(max, value));

const formatPriceAxisValue = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }
  if (Math.abs(value) >= 1000) {
    return value.toFixed(0);
  }
  return value.toFixed(2);
};

const buildIndicators = (
  palette: ChartPalette,
  fearGreedRuntime?: FearGreedRuntime | null,
  etfFlowRuntime?: EtfFlowRuntime | null,
  liquidationHeatmapRuntime?: LiquidationHeatmapRuntime | null,
): IndicatorCardData[] => {
  const categoryAxisStyle = {
    axisLabel: { color: palette.textSecondary, fontSize: 11 },
    axisLine: { lineStyle: { color: palette.axisLine } },
    axisTick: { show: false },
  };

  const valueAxisStyle = {
    axisLabel: { color: palette.textSecondary, fontSize: 11 },
    splitLine: { lineStyle: { color: palette.splitLine } },
  };

  const fearGreedValue = fearGreedRuntime ? Math.max(0, Math.min(100, fearGreedRuntime.value)) : 73;
  const fearGreedLabel = fearGreedRuntime?.classification ?? '极度贪婪';
  const fearGreedSample = fearGreedRuntime
    ? `实时值：${fearGreedValue.toFixed(0)}（${fearGreedLabel}）${fearGreedRuntime.stale ? ' · 过期缓存' : ''}`
    : '样例值：73（极度贪婪）';
  const fearGreedSourceText = fearGreedRuntime?.sourceTs
    ? `数据时间：${new Date(fearGreedRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const fearGreedNote = fearGreedRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${fearGreedSourceText}`
    : `常用于判断情绪是否处于极端区间，可作为减仓或反向布局的辅助信号。${fearGreedSourceText}`;

  const etfSeriesFallback = [0.62, 0.48, -0.12, 1.2, 0.86, 0.31, 0.73];
  const etfSeries = etfFlowRuntime?.series && etfFlowRuntime.series.length > 0
    ? etfFlowRuntime.series.slice(-7)
    : null;
  const etfXAxis = etfSeries
    ? etfSeries.map((item) => new Date(item.ts).toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' }))
    : ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];
  const etfBarValues = etfSeries
    ? etfSeries.map((item) => Number((item.netFlowUsd / 100_000_000).toFixed(2)))
    : etfSeriesFallback;
  const etfLatestFlowUsd = etfFlowRuntime?.latestNetFlowUsd ?? 120_000_000;
  const etfLatestFlowYi = etfLatestFlowUsd / 100_000_000;
  const etfSample = etfFlowRuntime
    ? `实时：${etfLatestFlowYi >= 0 ? '+' : ''}${etfLatestFlowYi.toFixed(2)} 亿 USD / 日${etfFlowRuntime.stale ? ' · 过期缓存' : ''}`
    : '样例：+1.20 亿 USD / 日';
  const etfSourceText = etfFlowRuntime?.sourceTs
    ? `数据时间：${new Date(etfFlowRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const etfNote = etfFlowRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${etfSourceText}`
    : `持续净流入通常代表传统资金加仓比特币，净流出则可能对应情绪降温或获利了结。${etfSourceText}`;
  const etfBarData = etfBarValues.map((value) => ({
    value,
    itemStyle: {
      color: value >= 0 ? palette.colorSuccess : palette.colorDanger,
    },
  }));

  const fallbackHeatmapXAxisLabels = Array.from({ length: 24 }, (_, index) => `${String(index).padStart(2, '0')}:00`);
  const fallbackHeatmapPriceValues = [62000, 62500, 63000, 63500, 64000, 64500, 65000];
  const heatmapXAxisLabels = liquidationHeatmapRuntime?.xAxisTimestamps && liquidationHeatmapRuntime.xAxisTimestamps.length > 0
    ? liquidationHeatmapRuntime.xAxisTimestamps.map((item) => formatHeatmapAxisTime(item))
    : fallbackHeatmapXAxisLabels;
  const heatmapPriceValues = liquidationHeatmapRuntime?.yAxis && liquidationHeatmapRuntime.yAxis.length > 0
    ? liquidationHeatmapRuntime.yAxis
    : fallbackHeatmapPriceValues;
  const heatmapYAxisLabels = heatmapPriceValues.map((price) => price.toFixed(2));
  const heatmapPointPriceData = liquidationHeatmapRuntime?.points && liquidationHeatmapRuntime.points.length > 0
    ? liquidationHeatmapRuntime.points
      .filter((point) => (
        point.xIndex >= 0
        && point.xIndex < heatmapXAxisLabels.length
        && point.yIndex >= 0
        && point.yIndex < heatmapPriceValues.length
      ))
      .map((point) => [point.xIndex, heatmapPriceValues[point.yIndex], point.liquidationUsd])
    : [
      [2, 62500, 1200000],
      [8, 63200, 3000000],
      [12, 63800, 1500000],
      [15, 64400, 900000],
    ];
  const heatmapPointData = heatmapPointPriceData
    .map((point) => {
      const xIndex = Number(point[0]);
      const price = Number(point[1]);
      const liquidationUsd = Number(point[2]);
      const yIndex = heatmapPriceValues.findIndex((value) => value === price);
      if (yIndex < 0) {
        return null;
      }
      return [xIndex, heatmapYAxisLabels[yIndex], liquidationUsd];
    })
    .filter((item): item is [number, string, number] => item !== null);
  const heatmapMaxLiquidationUsd = Math.max(
    liquidationHeatmapRuntime?.maxLiquidationUsd ?? 0,
    heatmapPointPriceData.reduce((max, item) => Math.max(max, Number(item[2]) || 0), 0),
    1,
  );
  const heatmapPriceMin = Math.min(...heatmapPriceValues);
  const heatmapPriceMax = Math.max(...heatmapPriceValues);
  const heatmapCandlestickData = liquidationHeatmapRuntime?.candlesticks && liquidationHeatmapRuntime.candlesticks.length > 0
    ? liquidationHeatmapRuntime.candlesticks.map((candle) => [candle.open, candle.close, candle.low, candle.high])
    : [];
  const fallbackCurrentPrice = heatmapPriceValues[Math.floor(heatmapPriceValues.length / 2)] ?? 0;
  const currentPrice = liquidationHeatmapRuntime?.currentPrice ?? fallbackCurrentPrice;
  const priceSpan = Math.max(heatmapPriceMax - heatmapPriceMin, 1);
  const bucketStep = priceSpan / LIQUIDATION_BUCKET_COUNT;
  const bucketTotals = Array.from({ length: LIQUIDATION_BUCKET_COUNT }, () => 0);
  heatmapPointPriceData.forEach((point) => {
    const price = Number(point[1]);
    const liquidationUsd = Number(point[2]);
    if (!Number.isFinite(price) || !Number.isFinite(liquidationUsd) || liquidationUsd <= 0) {
      return;
    }

    const bucketIndex = clampNumber(
      Math.floor(((price - heatmapPriceMin) / priceSpan) * LIQUIDATION_BUCKET_COUNT),
      0,
      LIQUIDATION_BUCKET_COUNT - 1,
    );
    bucketTotals[bucketIndex] += liquidationUsd;
  });
  const bucketCenters = bucketTotals.map((_, index) => heatmapPriceMin + (index + 0.5) * bucketStep);
  const bucketRanges = bucketTotals.map((_, index) => {
    const start = heatmapPriceMin + index * bucketStep;
    const end = start + bucketStep;
    return `${formatPriceAxisValue(start)} - ${formatPriceAxisValue(end)}`;
  });
  const currentBucketIndex = clampNumber(
    Math.floor(((currentPrice - heatmapPriceMin) / priceSpan) * LIQUIDATION_BUCKET_COUNT),
    0,
    LIQUIDATION_BUCKET_COUNT - 1,
  );
  const upperBucketCount = LIQUIDATION_BUCKET_COUNT - currentBucketIndex;
  const lowerBucketCount = currentBucketIndex;
  const upperTotalLiquidationUsd = bucketTotals
    .slice(currentBucketIndex)
    .reduce((sum, value) => sum + value, 0);
  const lowerTotalLiquidationUsd = bucketTotals
    .slice(0, currentBucketIndex)
    .reduce((sum, value) => sum + value, 0);
  const upperPain = {
    bucketIndex: currentBucketIndex,
    liquidationUsd: 0,
  };
  for (let index = currentBucketIndex; index < bucketTotals.length; index += 1) {
    if (bucketTotals[index] > upperPain.liquidationUsd) {
      upperPain.bucketIndex = index;
      upperPain.liquidationUsd = bucketTotals[index];
    }
  }
  const lowerPain = {
    bucketIndex: Math.max(0, currentBucketIndex - 1),
    liquidationUsd: 0,
  };
  for (let index = 0; index < currentBucketIndex; index += 1) {
    if (bucketTotals[index] > lowerPain.liquidationUsd) {
      lowerPain.bucketIndex = index;
      lowerPain.liquidationUsd = bucketTotals[index];
    }
  }
  const upperCumulative: Array<number | null> = Array.from({ length: LIQUIDATION_BUCKET_COUNT }, () => null);
  let upperRunningTotal = 0;
  for (let index = currentBucketIndex; index < bucketTotals.length; index += 1) {
    upperRunningTotal += bucketTotals[index];
    upperCumulative[index] = upperRunningTotal;
  }
  const lowerCumulative: Array<number | null> = Array.from({ length: LIQUIDATION_BUCKET_COUNT }, () => null);
  let lowerRunningTotal = 0;
  for (let index = currentBucketIndex - 1; index >= 0; index -= 1) {
    lowerRunningTotal += bucketTotals[index];
    lowerCumulative[index] = lowerRunningTotal;
  }
  const profileYAxisLabels = bucketCenters.map((price) => formatPriceAxisValue(price));
  const profileBarData = bucketTotals.map((value, index) => ({
    value,
    itemStyle: {
      color: index < currentBucketIndex ? 'rgba(239, 68, 68, 0.45)' : 'rgba(34, 197, 94, 0.45)',
    },
  }));
  const profileAxisMax = Math.max(
    bucketTotals.reduce((max, value) => Math.max(max, value), 0),
    upperCumulative.reduce<number>((max, value) => Math.max(max, value ?? 0), 0),
    lowerCumulative.reduce<number>((max, value) => Math.max(max, value ?? 0), 0),
    1,
  );
  const heatmapRangeText = liquidationHeatmapRuntime?.range ?? '3d';
  const heatmapPairText = liquidationHeatmapRuntime
    ? `${liquidationHeatmapRuntime.exchange} ${liquidationHeatmapRuntime.symbol}`
    : 'Binance BTCUSDT';
  const heatmapSample = liquidationHeatmapRuntime
    ? `实时：${heatmapPairText} · ${heatmapRangeText} · 当前价 ${formatPriceAxisValue(currentPrice)} · 最大清算 ${formatCompactUsd(heatmapMaxLiquidationUsd)}${liquidationHeatmapRuntime.stale ? ' · 过期缓存' : ''}`
    : '样例：Binance BTCUSDT · 3d · 最大清算 300.00万 USD';
  const heatmapSourceText = liquidationHeatmapRuntime?.sourceTs
    ? `数据时间：${new Date(liquidationHeatmapRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const heatmapPainText = `上涨最大痛点：${bucketRanges[upperPain.bucketIndex]}（${formatCompactUsd(upperPain.liquidationUsd)}），下跌最大痛点：${bucketRanges[lowerPain.bucketIndex]}（${formatCompactUsd(lowerPain.liquidationUsd)}）。`;
  const heatmapOrderbookText = `100 桶订单簿：当前位于第 ${currentBucketIndex + 1} 桶（下方 ${lowerBucketCount} 桶 / 上方 ${upperBucketCount} 桶），下方累计 ${formatCompactUsd(lowerTotalLiquidationUsd)}，上方累计 ${formatCompactUsd(upperTotalLiquidationUsd)}。`;
  const heatmapNote = liquidationHeatmapRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${heatmapPainText}${heatmapOrderbookText}${heatmapSourceText}`
    : `黄色方块透明度按区间最小值到最大值线性映射，值越大越接近纯黄色。${heatmapPainText}${heatmapOrderbookText}${heatmapSourceText}`;

  return [
    {
      id: 'fear-greed',
      name: '贪婪恐慌指数 (Fear & Greed Index)',
      category: '情绪',
      sample: fearGreedSample,
      description:
        '0–100 的情绪刻度，综合波动率、成交量、社交媒体情绪等维度，数值越高代表市场越贪婪。',
      note: fearGreedNote,
      chartOption: {
        tooltip: { formatter: '情绪指数：{c}' },
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
            data: [{ value: fearGreedValue, name: fearGreedLabel }],
          },
        ],
      },
    },
    {
      id: 'etf-flow',
      name: '比特币现货 ETF 净流入',
      category: '资金流向',
      sample: etfSample,
      description:
        '统计主流 BTC 现货 ETF 的申赎资金，正值表示资金净流入，负值表示资金净流出。',
      note: etfNote,
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 36, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: etfXAxis,
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          name: '亿美元',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'bar',
            barWidth: '56%',
            data: etfBarData,
          },
        ],
      },
    },
    {
      id: 'liquidation-heatmap',
      name: '交易对爆仓热力图（模型1）',
      category: '风险事件',
      sample: heatmapSample,
      description:
        'X 轴为时间、Y 轴为价格，网格方块亮度（黄色透明度）表示该时间与价格附近的清算强度。',
      note: heatmapNote,
      chartWrapClassName: 'indicator-module-chart-wrap--heatmap',
      chartOption: {
        animation: false,
        tooltip: {
          trigger: 'item',
          backgroundColor: palette.tooltipBg,
          borderColor: palette.tooltipBorder,
          borderWidth: 1,
          textStyle: { color: palette.textPrimary },
          formatter: (params: any) => {
            const seriesName = typeof params?.seriesName === 'string' ? params.seriesName : '';
            if (seriesName === '清算热力') {
              const row = Array.isArray(params?.data) ? params.data : [];
              const xIndex = Number(row[0] ?? 0);
              const price = Number(row[1] ?? 0);
              const liquidationUsd = Number(row[2] ?? 0);
              const timeText = heatmapXAxisLabels[xIndex] ?? '--';
              return [
                `时间：${timeText}`,
                `价格：${Number.isFinite(price) ? formatPriceAxisValue(price) : String(row[1] ?? '--')}`,
                `清算强度：${formatCompactUsd(liquidationUsd)}`,
              ].join('<br/>');
            }

            if (seriesName === 'K线') {
              const row = Array.isArray(params?.data) ? params.data : [];
              const timeText = heatmapXAxisLabels[Number(params?.dataIndex ?? 0)] ?? '--';
              const open = Number(row[0] ?? 0);
              const close = Number(row[1] ?? 0);
              const low = Number(row[2] ?? 0);
              const high = Number(row[3] ?? 0);
              return [
                `时间：${timeText}`,
                `开：${formatPriceAxisValue(open)} · 收：${formatPriceAxisValue(close)}`,
                `低：${formatPriceAxisValue(low)} · 高：${formatPriceAxisValue(high)}`,
              ].join('<br/>');
            }

            const bucketIndex = Number(params?.dataIndex ?? -1);
            if (bucketIndex < 0 || bucketIndex >= bucketRanges.length) {
              return '--';
            }

            const bucketRange = bucketRanges[bucketIndex];
            if (seriesName === '100桶分布') {
              const liquidationUsd = Number(params?.data?.value ?? params?.data ?? 0);
              return [
                `价格桶：${bucketRange}`,
                `桶内总清算：${formatCompactUsd(liquidationUsd)}`,
              ].join('<br/>');
            }

            if (seriesName === '上方累计' || seriesName === '下方累计') {
              const cumulativeUsd = Number(params?.data ?? 0);
              return [
                `价格桶：${bucketRange}`,
                `${seriesName}：${formatCompactUsd(cumulativeUsd)}`,
              ].join('<br/>');
            }

            return '--';
          },
        },
        grid: [
          { left: 50, right: '26%', top: 12, bottom: 46 },
          { left: 50, right: '26%', top: 12, bottom: 46 },
          { left: '76%', right: 10, top: 12, bottom: 46 },
        ],
        xAxis: [
          {
            type: 'category',
            gridIndex: 0,
            data: heatmapXAxisLabels,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 10,
              interval: 'auto',
              formatter: (value: string) => value.split(' ')[1] ?? value,
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitArea: { show: false },
          },
          {
            type: 'category',
            gridIndex: 1,
            data: heatmapXAxisLabels,
            show: false,
          },
          {
            type: 'value',
            gridIndex: 2,
            min: 0,
            max: profileAxisMax,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 9,
              formatter: (value: number) => formatCompactUsd(value),
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitLine: { lineStyle: { color: palette.splitLine } },
          },
        ],
        yAxis: [
          {
            type: 'category',
            gridIndex: 0,
            data: heatmapYAxisLabels,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 10,
              formatter: (value: string) => formatPriceAxisValue(Number(value)),
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitLine: { lineStyle: { color: palette.splitLine } },
          },
          {
            type: 'value',
            gridIndex: 1,
            min: heatmapPriceMin,
            max: heatmapPriceMax,
            scale: true,
            show: false,
          },
          {
            type: 'category',
            gridIndex: 2,
            data: profileYAxisLabels,
            inverse: true,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 9,
              interval: 9,
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitLine: { show: false },
          },
        ],
        visualMap: {
          min: 0,
          max: heatmapMaxLiquidationUsd,
          dimension: 2,
          seriesIndex: 0,
          show: false,
          calculable: false,
          inRange: {
            color: ['rgba(250, 204, 21, 0)', 'rgba(250, 204, 21, 1)'],
          },
        },
        series: [
          {
            name: '清算热力',
            type: 'heatmap',
            xAxisIndex: 0,
            yAxisIndex: 0,
            data: heatmapPointData,
            progressive: 1000,
            emphasis: {
              itemStyle: {
                borderColor: 'rgba(250, 204, 21, 0.75)',
                borderWidth: 1,
              },
            },
          },
          {
            name: 'K线',
            type: 'candlestick',
            xAxisIndex: 1,
            yAxisIndex: 1,
            data: heatmapCandlestickData,
            itemStyle: {
              color: '#00E5C0',
              color0: '#FF5B6E',
              borderColor: '#00E5C0',
              borderColor0: '#FF5B6E',
            },
            z: 3,
            emphasis: {
              itemStyle: {
                borderWidth: 1.2,
              },
            },
          },
          {
            name: '100桶分布',
            type: 'bar',
            xAxisIndex: 2,
            yAxisIndex: 2,
            data: profileBarData,
            barWidth: '70%',
            markLine: {
              symbol: 'none',
              silent: true,
              lineStyle: {
                width: 1,
                type: 'dashed',
                color: 'rgba(250, 204, 21, 0.8)',
              },
              label: {
                color: palette.textSecondary,
                formatter: '当前价桶',
              },
              data: [{ yAxis: profileYAxisLabels[currentBucketIndex] }],
            },
          },
          {
            name: '上方累计',
            type: 'line',
            xAxisIndex: 2,
            yAxisIndex: 2,
            data: upperCumulative,
            showSymbol: false,
            connectNulls: false,
            smooth: 0.25,
            lineStyle: {
              width: 2,
              color: '#2DD4BF',
            },
          },
          {
            name: '下方累计',
            type: 'line',
            xAxisIndex: 2,
            yAxisIndex: 2,
            data: lowerCumulative,
            showSymbol: false,
            connectNulls: false,
            smooth: 0.25,
            lineStyle: {
              width: 2,
              color: '#F87171',
            },
          },
        ],
      },
    },
    {
      id: 'exchange-flow',
      name: '交易所资金净流入 / 流出',
      category: '资金流向',
      sample: '样例：-8,500 BTC / 24h',
      description:
        '追踪主流现货与合约交易所的链上净流入量，负值说明更多币被提走，正值说明更多币被充入交易所。',
      note: '大量净流入往往预示潜在的抛压，大量净流出则更倾向于长期持有或机构托管。',
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 42, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['02/04', '02/05', '02/06', '02/07', '02/08', '02/09', '02/10'],
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          name: 'BTC',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'line',
            smooth: true,
            symbolSize: 6,
            lineStyle: { width: 2, color: palette.colorSecondary },
            areaStyle: { color: 'rgba(6, 182, 212, 0.15)' },
            itemStyle: { color: palette.colorSecondary },
            data: [4200, 2100, -1300, -4600, -8500, -3200, -5100],
          },
        ],
      },
    },
    {
      id: 'long-short',
      name: '多空持仓比 (Long / Short Ratio)',
      category: '合约杠杆',
      sample: '样例：1.35',
      description:
        '根据不同交易所的合约账户数据统计多头账户数与空头账户数的比例，用于衡量情绪偏多还是偏空。',
      note: '极端高位的多头占比容易在剧烈波动中引发多头挤压 (Long Squeeze)。',
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 42, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00'],
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          min: 0.8,
          max: 1.8,
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'line',
            smooth: true,
            symbolSize: 6,
            lineStyle: { width: 2, color: palette.colorTertiary },
            itemStyle: { color: palette.colorTertiary },
            data: [1.08, 1.15, 1.22, 1.18, 1.29, 1.35, 1.32],
            markLine: {
              symbol: 'none',
              lineStyle: { type: 'dashed', color: palette.colorWarning },
              label: { color: palette.textSecondary, formatter: '多空分界线' },
              data: [{ yAxis: 1 }],
            },
          },
        ],
      },
    },
    {
      id: 'funding-rate',
      name: '永续合约资金费率 (Funding Rate)',
      category: '合约杠杆',
      sample: '样例：+0.032% / 8h',
      description:
        '反映永续合约价格相对现货的溢价或贴水程度，正值代表多头付费给空头，负值则相反。',
      note: '持续高正资金费率通常意味着市场高度乐观且杠杆偏多，容易形成多头过度拥挤。',
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 42, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['D-6', 'D-5', 'D-4', 'D-3', 'D-2', 'D-1', '今日'],
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          name: '%',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'line',
            smooth: true,
            symbolSize: 6,
            lineStyle: { width: 2, color: palette.colorWarning },
            itemStyle: { color: palette.colorWarning },
            areaStyle: { color: 'rgba(245, 158, 11, 0.14)' },
            data: [0.006, 0.011, 0.018, 0.014, 0.027, 0.03, 0.032],
          },
        ],
      },
    },
    {
      id: 'open-interest',
      name: '未平仓合约总量 (Open Interest)',
      category: '合约杠杆',
      sample: '样例：15.3B USD',
      description:
        '统计所有尚未平仓的合约名义价值，用来观察杠杆资金整体规模的变化。',
      note: 'OI 快速攀升叠加价格单边走势，往往意味着潜在的大规模强平风险。',
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 42, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['02/04', '02/05', '02/06', '02/07', '02/08', '02/09', '02/10'],
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          name: 'B USD',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'line',
            smooth: true,
            symbolSize: 6,
            lineStyle: { width: 2, color: palette.colorPrimary },
            itemStyle: { color: palette.colorPrimary },
            areaStyle: { color: 'rgba(59, 130, 246, 0.15)' },
            data: [12.1, 12.8, 13.4, 13.9, 14.7, 15.1, 15.3],
          },
        ],
      },
    },
    {
      id: 'liquidations',
      name: '24 小时强平金额 (Liquidations)',
      category: '风险事件',
      sample: '样例：多头强平 3.1B / 空头 0.8B',
      description:
        '展示最近 24 小时内多头与空头被动平仓的金额，反映市场是否刚经历过一轮清算。',
      note: '在极端行情后强平金额放大，往往意味着短期杠杆已大幅出清，后续波动可能趋于缓和。',
      chartOption: {
        tooltip: buildTooltip(palette),
        legend: {
          top: 2,
          textStyle: { color: palette.textSecondary, fontSize: 11 },
        },
        grid: { left: 42, right: 12, top: 34, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['00h', '04h', '08h', '12h', '16h', '20h', '24h'],
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          name: 'M USD',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            name: '多头强平',
            type: 'bar',
            stack: 'liquidation',
            data: [120, 180, 460, 790, 910, 620, 3100],
            itemStyle: { color: palette.colorDanger },
          },
          {
            name: '空头强平',
            type: 'bar',
            stack: 'liquidation',
            data: [80, 95, 110, 160, 220, 340, 800],
            itemStyle: { color: palette.colorSuccess },
          },
        ],
      },
    },
    {
      id: 'stablecoin',
      name: '稳定币净流入与流通市值',
      category: '资金流向',
      sample: '样例：稳定币净流入 +650M USD / 流通市值 140B',
      description:
        '观察 USDT、USDC 等主流稳定币的发行与流通变化，衡量场内「干火药」的多少。',
      note: '稳定币持续净发行与净流入，通常意味着有更多潜在买盘等待进入市场。',
      chartOption: {
        tooltip: buildTooltip(palette),
        legend: {
          top: 2,
          textStyle: { color: palette.textSecondary, fontSize: 11 },
        },
        grid: { left: 42, right: 42, top: 34, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['D-6', 'D-5', 'D-4', 'D-3', 'D-2', 'D-1', '今日'],
          ...categoryAxisStyle,
        },
        yAxis: [
          {
            type: 'value',
            name: 'M USD',
            nameTextStyle: { color: palette.textSecondary },
            ...valueAxisStyle,
          },
          {
            type: 'value',
            name: 'B USD',
            nameTextStyle: { color: palette.textSecondary },
            ...valueAxisStyle,
          },
        ],
        series: [
          {
            name: '净流入',
            type: 'bar',
            data: [120, 310, 280, 460, 520, 590, 650],
            itemStyle: { color: palette.colorSecondary },
          },
          {
            name: '流通市值',
            type: 'line',
            yAxisIndex: 1,
            smooth: true,
            symbolSize: 6,
            lineStyle: { width: 2, color: palette.colorPrimary },
            itemStyle: { color: palette.colorPrimary },
            data: [133.2, 134.1, 135.6, 136.4, 137.2, 138.5, 140.0],
          },
        ],
      },
    },
  ];
};

const IndicatorChart: React.FC<{ option: EChartsOption }> = ({ option }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<ECharts | null>(null);

  useEffect(() => {
    const dom = containerRef.current;
    if (!dom) {
      return;
    }

    // 避免严格模式与热更新下重复初始化同一个 DOM。
    const chart = echarts.getInstanceByDom(dom) ?? echarts.init(dom);
    chartRef.current = chart;
    chart.setOption(option, true);

    // 通过容器尺寸监听保证侧栏收缩、窗口变化时图表自适应。
    const resizeObserver = new ResizeObserver(() => {
      chart.resize();
    });
    resizeObserver.observe(dom);

    return () => {
      resizeObserver.disconnect();
      chart.dispose();
      chartRef.current = null;
    };
  }, []);

  useEffect(() => {
    chartRef.current?.setOption(option, true);
  }, [option]);

  return <div ref={containerRef} className="indicator-module-chart" />;
};

const IndicatorCard: React.FC<{ indicator: IndicatorCardData }> = ({ indicator }) => (
  <article id={`indicator-card-${indicator.id}`} className="indicator-module-card">
    <header className="indicator-module-card-header">
      <div className="indicator-module-card-headline">
        <h2 className="indicator-module-card-title">{indicator.name}</h2>
        <p className="indicator-module-card-sample">{indicator.sample}</p>
      </div>
      <span className="indicator-module-tag">{indicator.category}</span>
    </header>
    <p className="indicator-module-card-text">{indicator.description}</p>
    <p className="indicator-module-card-note">{indicator.note}</p>
    <div className={`indicator-module-chart-wrap ${indicator.chartWrapClassName ?? ''}`}>
      <IndicatorChart option={indicator.chartOption} />
    </div>
  </article>
);

const toNumber = (input: unknown): number | null => {
  if (typeof input === 'number' && Number.isFinite(input)) {
    return input;
  }

  if (typeof input === 'string') {
    const parsed = Number(input);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
};

const resolveFearGreedLabel = (value: number): string => {
  if (value <= 24) {
    return '极度恐慌';
  }
  if (value <= 49) {
    return '恐慌';
  }
  if (value <= 74) {
    return '贪婪';
  }
  return '极度贪婪';
};

const parseFearGreedRuntime = (latest: IndicatorLatestItem): FearGreedRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const value = toNumber(payload.value);
  if (value === null) {
    return null;
  }

  const classificationRaw = payload.classification;
  const classification = typeof classificationRaw === 'string' && classificationRaw.trim()
    ? classificationRaw.trim()
    : resolveFearGreedLabel(value);

  const payloadSourceTs = toNumber(payload.sourceTs);
  return {
    value,
    classification,
    stale: latest.stale,
    sourceTs: payloadSourceTs ?? latest.sourceTs,
  };
};

const parseEtfFlowRuntime = (latest: IndicatorLatestItem): EtfFlowRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const value = toNumber(payload.netFlowUsd) ?? toNumber(payload.value);

  const seriesRaw = Array.isArray(payload.series)
    ? payload.series
    : [];
  const parsedSeries = seriesRaw
    .map((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const point = item as Record<string, unknown>;
      const ts = toNumber(point.ts) ?? toNumber(point.timestamp) ?? toNumber(point.time);
      const netFlowUsd = toNumber(point.netFlowUsd) ?? toNumber(point.value) ?? toNumber(point.flowUsd) ?? toNumber(point.flow_usd);
      if (ts === null || netFlowUsd === null) {
        return null;
      }

      return {
        ts,
        netFlowUsd,
      };
    })
    .filter((item): item is { ts: number; netFlowUsd: number } => item !== null)
    .sort((left, right) => left.ts - right.ts);

  const payloadSourceTs = toNumber(payload.sourceTs);
  const latestPoint = parsedSeries.length > 0 ? parsedSeries[parsedSeries.length - 1] : null;
  const latestNetFlowUsd = value ?? latestPoint?.netFlowUsd;
  if (latestNetFlowUsd === null || latestNetFlowUsd === undefined) {
    return null;
  }

  const sourceTs = payloadSourceTs ?? latestPoint?.ts ?? latest.sourceTs;
  const normalizedSeries = parsedSeries.length > 0
    ? parsedSeries
    : sourceTs
      ? [{ ts: sourceTs, netFlowUsd: latestNetFlowUsd }]
      : [];

  return {
    latestNetFlowUsd,
    stale: latest.stale,
    sourceTs,
    series: normalizedSeries,
  };
};

const toNonEmptyString = (input: unknown): string | null => {
  if (typeof input !== 'string') {
    return null;
  }

  const trimmed = input.trim();
  return trimmed ? trimmed : null;
};

const normalizeTimestampMs = (timestamp: number): number => (timestamp < 100_000_000_000 ? timestamp * 1000 : timestamp);

const parseLiquidationCandlestick = (input: unknown): LiquidationCandlestick | null => {
  if (Array.isArray(input)) {
    const ts = toNumber(input[0]);
    const open = toNumber(input[1]);
    const high = toNumber(input[2]);
    const low = toNumber(input[3]);
    const close = toNumber(input[4]);
    if (ts === null || open === null || high === null || low === null || close === null) {
      return null;
    }

    const normalizedHigh = Math.max(high, open, close, low);
    const normalizedLow = Math.min(low, open, close, high);
    return {
      ts: normalizeTimestampMs(ts),
      open,
      high: normalizedHigh,
      low: normalizedLow,
      close,
    };
  }

  if (!input || typeof input !== 'object') {
    return null;
  }

  const row = input as Record<string, unknown>;
  const ts = toNumber(row.ts) ?? toNumber(row.timestamp) ?? toNumber(row.time);
  const open = toNumber(row.open) ?? toNumber(row.openPrice) ?? toNumber(row.o);
  const high = toNumber(row.high) ?? toNumber(row.highPrice) ?? toNumber(row.h);
  const low = toNumber(row.low) ?? toNumber(row.lowPrice) ?? toNumber(row.l);
  const close = toNumber(row.close) ?? toNumber(row.closePrice) ?? toNumber(row.c);
  if (ts === null || open === null || high === null || low === null || close === null) {
    return null;
  }

  const normalizedHigh = Math.max(high, open, close, low);
  const normalizedLow = Math.min(low, open, close, high);
  return {
    ts: normalizeTimestampMs(ts),
    open,
    high: normalizedHigh,
    low: normalizedLow,
    close,
  };
};

const parseLiquidationHeatmapRuntime = (latest: IndicatorLatestItem): LiquidationHeatmapRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const yAxisRaw = Array.isArray(payload.yAxis)
    ? payload.yAxis
    : Array.isArray(payload.y_axis)
      ? payload.y_axis
      : [];
  const yAxis = yAxisRaw
    .map((item) => toNumber(item))
    .filter((item): item is number => item !== null);

  const pointsRaw = Array.isArray(payload.liquidationLeverageData)
    ? payload.liquidationLeverageData
    : Array.isArray(payload.liquidation_leverage_data)
      ? payload.liquidation_leverage_data
      : [];
  const points = pointsRaw
    .map((item) => {
      if (Array.isArray(item)) {
        const xIndex = toNumber(item[0]);
        const yIndex = toNumber(item[1]);
        const liquidationUsd = toNumber(item[2]);
        if (xIndex === null || yIndex === null || liquidationUsd === null) {
          return null;
        }

        return {
          xIndex,
          yIndex,
          liquidationUsd,
        };
      }

      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const xIndex = toNumber(row.xIndex) ?? toNumber(row.x_index) ?? toNumber(row.x);
      const yIndex = toNumber(row.yIndex) ?? toNumber(row.y_index) ?? toNumber(row.y);
      const liquidationUsd = toNumber(row.liquidationUsd) ?? toNumber(row.liquidation_usd) ?? toNumber(row.value);
      if (xIndex === null || yIndex === null || liquidationUsd === null) {
        return null;
      }

      return {
        xIndex,
        yIndex,
        liquidationUsd,
      };
    })
    .filter((item): item is { xIndex: number; yIndex: number; liquidationUsd: number } => item !== null)
    .map((item) => ({
      xIndex: Math.floor(item.xIndex),
      yIndex: Math.floor(item.yIndex),
      liquidationUsd: item.liquidationUsd,
    }))
    .filter((item) => item.xIndex >= 0 && item.yIndex >= 0 && item.liquidationUsd >= 0);

  if (yAxis.length === 0 || points.length === 0) {
    return null;
  }

  const candlesticksRaw = Array.isArray(payload.priceCandlesticks)
    ? payload.priceCandlesticks
    : Array.isArray(payload.price_candlesticks)
      ? payload.price_candlesticks
      : [];
  const candlesticks = candlesticksRaw
    .map((item) => parseLiquidationCandlestick(item))
    .filter((item): item is LiquidationCandlestick => item !== null);

  const xAxisTimestamps = candlesticks.map((item) => item.ts);
  const maxXIndex = points.reduce((max, item) => Math.max(max, item.xIndex), 0);
  const payloadSourceTs = toNumber(payload.sourceTs);
  if (xAxisTimestamps.length === 0) {
    const fallbackSourceTs = payloadSourceTs ?? latest.sourceTs ?? Date.now();
    const rebuilt = Array.from({ length: maxXIndex + 1 }, (_, index) => fallbackSourceTs - (maxXIndex - index) * 60_000);
    xAxisTimestamps.push(...rebuilt);
  } else if (xAxisTimestamps.length <= maxXIndex) {
    const diff = xAxisTimestamps.length > 1
      ? Math.abs(xAxisTimestamps[xAxisTimestamps.length - 1] - xAxisTimestamps[xAxisTimestamps.length - 2])
      : 60_000;
    const stepMs = diff > 0 ? diff : 60_000;
    while (xAxisTimestamps.length <= maxXIndex) {
      const previous = xAxisTimestamps[xAxisTimestamps.length - 1];
      xAxisTimestamps.push(previous + stepMs);
    }
  }

  const maxLiquidationUsd = points.reduce((max, item) => Math.max(max, item.liquidationUsd), 0);
  const sourceTs = payloadSourceTs ?? latest.sourceTs ?? xAxisTimestamps[xAxisTimestamps.length - 1];
  const currentPrice = candlesticks.length > 0 ? candlesticks[candlesticks.length - 1].close : undefined;

  return {
    exchange: toNonEmptyString(payload.exchange) ?? 'Binance',
    symbol: toNonEmptyString(payload.symbol) ?? 'BTCUSDT',
    range: toNonEmptyString(payload.range) ?? '3d',
    stale: latest.stale,
    sourceTs,
    yAxis,
    xAxisTimestamps,
    points,
    candlesticks,
    currentPrice,
    maxLiquidationUsd,
  };
};

type IndicatorModuleProps = {
  focusIndicatorId?: string;
  onFocusHandled?: () => void;
};

const IndicatorModule: React.FC<IndicatorModuleProps> = ({ focusIndicatorId, onFocusHandled }) => {
  const [isDarkMode, setIsDarkMode] = useState(() =>
    document.documentElement.classList.contains('dark-theme'),
  );
  const [fearGreedRuntime, setFearGreedRuntime] = useState<FearGreedRuntime | null>(null);
  const [etfFlowRuntime, setEtfFlowRuntime] = useState<EtfFlowRuntime | null>(null);
  const [liquidationHeatmapRuntime, setLiquidationHeatmapRuntime] = useState<LiquidationHeatmapRuntime | null>(null);

  useEffect(() => {
    const root = document.documentElement;
    const updateTheme = () => {
      setIsDarkMode(root.classList.contains('dark-theme'));
    };

    // 监听根节点 class 变化，确保切换主题后图表配色即时同步。
    const observer = new MutationObserver(updateTheme);
    observer.observe(root, { attributes: true, attributeFilter: ['class'] });

    return () => {
      observer.disconnect();
    };
  }, []);

  const palette = useMemo(() => createPalette(isDarkMode), [isDarkMode]);
  const indicators = useMemo(
    () => buildIndicators(palette, fearGreedRuntime, etfFlowRuntime, liquidationHeatmapRuntime),
    [palette, fearGreedRuntime, etfFlowRuntime, liquidationHeatmapRuntime],
  );

  useEffect(() => {
    let disposed = false;

    const loadIndicatorRuntime = async () => {
      const [fearGreedResult, etfFlowResult, liquidationHeatmapResult] = await Promise.allSettled([
        getIndicatorLatest('coinglass.fear_greed', undefined, { allowStale: true }),
        getIndicatorLatest('coinglass.etf_flow', undefined, { allowStale: true }),
        getIndicatorLatest('coinglass.liquidation_heatmap_model1', undefined, { allowStale: true }),
      ]);
      if (disposed) {
        return;
      }

      if (fearGreedResult.status === 'fulfilled') {
        const runtime = parseFearGreedRuntime(fearGreedResult.value);
        if (runtime) {
          setFearGreedRuntime(runtime);
        }
      }

      if (etfFlowResult.status === 'fulfilled') {
        const runtime = parseEtfFlowRuntime(etfFlowResult.value);
        if (runtime) {
          setEtfFlowRuntime(runtime);
        }
      }

      if (liquidationHeatmapResult.status === 'fulfilled') {
        const runtime = parseLiquidationHeatmapRuntime(liquidationHeatmapResult.value);
        if (runtime) {
          setLiquidationHeatmapRuntime(runtime);
        }
      }
    };

    void loadIndicatorRuntime();
    const timer = window.setInterval(() => {
      void loadIndicatorRuntime();
    }, 60_000);

    return () => {
      disposed = true;
      window.clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    if (!focusIndicatorId) {
      return;
    }

    // 等当前页面完全渲染后，再滚动并触发动画，避免与菜单切换同时进行。
    const scrollTimer = window.setTimeout(() => {
      const element = document.getElementById(`indicator-card-${focusIndicatorId}`);
      if (!element) {
        if (onFocusHandled) {
          onFocusHandled();
        }
        return;
      }

      // 先平滑滚动到目标卡片附近
      element.scrollIntoView({ behavior: 'smooth', block: 'center' });

      // 再稍作延迟触发放大-回弹动画，保证滚动动作完成后再进行
      const animationTimer = window.setTimeout(() => {
        element.classList.add('indicator-module-card--focused');
        const duration = 800;
        const cleanupTimer = window.setTimeout(() => {
          element.classList.remove('indicator-module-card--focused');
          if (onFocusHandled) {
            onFocusHandled();
          }
        }, duration);

        // 在 effect 清理时同时清理动画定时器
        return () => {
          window.clearTimeout(cleanupTimer);
        };
      }, 500);

      // 在 effect 清理时清理动画启动定时器
      return () => {
        window.clearTimeout(animationTimer);
      };
    }, 0);

    return () => {
      window.clearTimeout(scrollTimer);
    };
  }, [focusIndicatorId, onFocusHandled, indicators.length]);

  return (
    <div className="module-container indicator-module-container">
      <div className="indicator-module-header">
        <h1 className="indicator-module-title">指标中心</h1>
      </div>
      <div className="indicator-module-grid">
        {indicators.map((indicator) => (
          <IndicatorCard key={indicator.id} indicator={indicator} />
        ))}
      </div>
    </div>
  );
};

export default IndicatorModule;

