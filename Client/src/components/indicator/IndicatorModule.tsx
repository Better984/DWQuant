import React, { useEffect, useMemo, useRef, useState } from 'react';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';
import './IndicatorModule.css';

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

const buildIndicators = (palette: ChartPalette): IndicatorCardData[] => {
  const categoryAxisStyle = {
    axisLabel: { color: palette.textSecondary, fontSize: 11 },
    axisLine: { lineStyle: { color: palette.axisLine } },
    axisTick: { show: false },
  };

  const valueAxisStyle = {
    axisLabel: { color: palette.textSecondary, fontSize: 11 },
    splitLine: { lineStyle: { color: palette.splitLine } },
  };

  return [
    {
      id: 'fear-greed',
      name: '贪婪恐慌指数 (Fear & Greed Index)',
      category: '情绪',
      sample: '样例值：73（极度贪婪）',
      description:
        '0–100 的情绪刻度，综合波动率、成交量、社交媒体情绪等维度，数值越高代表市场越贪婪。',
      note: '常用于判断情绪是否处于极端区间，可作为减仓或反向布局的辅助信号。',
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
            data: [{ value: 73, name: '极度贪婪' }],
          },
        ],
      },
    },
    {
      id: 'etf-flow',
      name: '比特币现货 ETF 净流入',
      category: '资金流向',
      sample: '样例：+1.20 亿 USD / 日',
      description:
        '统计主流 BTC 现货 ETF 的申赎资金，正值表示资金净流入，负值表示资金净流出。',
      note: '持续净流入通常代表传统资金加仓比特币，净流出则可能对应情绪降温或获利了结。',
      chartOption: {
        tooltip: buildTooltip(palette),
        grid: { left: 36, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: ['周一', '周二', '周三', '周四', '周五', '周六', '周日'],
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
            data: [
              { value: 0.62, itemStyle: { color: palette.colorPrimary } },
              { value: 0.48, itemStyle: { color: palette.colorSecondary } },
              { value: -0.12, itemStyle: { color: palette.colorDanger } },
              { value: 1.2, itemStyle: { color: palette.colorSuccess } },
              { value: 0.86, itemStyle: { color: palette.colorPrimary } },
              { value: 0.31, itemStyle: { color: palette.colorSecondary } },
              { value: 0.73, itemStyle: { color: palette.colorPrimary } },
            ],
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
    if (!containerRef.current) {
      return;
    }

    const chart = echarts.init(containerRef.current);
    chartRef.current = chart;
    chart.setOption(option, true);

    // 通过容器尺寸监听保证侧栏收缩、窗口变化时图表自适应。
    const resizeObserver = new ResizeObserver(() => {
      chart.resize();
    });
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
    <div className="indicator-module-chart-wrap">
      <IndicatorChart option={indicator.chartOption} />
    </div>
  </article>
);

type IndicatorModuleProps = {
  focusIndicatorId?: string;
  onFocusHandled?: () => void;
};

const IndicatorModule: React.FC<IndicatorModuleProps> = ({ focusIndicatorId, onFocusHandled }) => {
  const [isDarkMode, setIsDarkMode] = useState(() =>
    document.documentElement.classList.contains('dark-theme'),
  );

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
  const indicators = useMemo(() => buildIndicators(palette), [palette]);

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

