import React, { useEffect, useMemo, useRef, useState } from 'react';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';
import './DiscoverModule.css';

interface ChartPalette {
  textPrimary: string;
  textSecondary: string;
  axisLine: string;
  colorPrimary: string;
  colorWarning: string;
}

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
  const newsItems = [
    {
      id: 'backpack-ipo',
      time: '22:34',
      title: '代币经济学新范式？当 Backpack 开始让 VC「延迟满足」',
      source: 'BLOCKBEATS',
      summary:
        '原文标题：《长期主义：Backpack 的 IPO 豪赌》。Backpack 通过 IPO 约束 VC 与团队的变现节奏，对传统「短期套现」模式形成对冲。',
    },
    {
      id: 'jump-prediction',
      time: '22:30',
      title: '华尔街顶级量化机构 Jump Trading 杀入预测市场，散户时代结束了？',
      source: 'chaincatcher',
      summary:
        'Jump Trading 将通过为 Kalshi 与 Polymarket 提供流动性换取股权，机构化做市有望显著提升预测市场的深度与效率。',
    },
    {
      id: 'megaeth-launch',
      time: '21:12',
      title: 'L2 疲软当头、Vitalik 转向悲观，MegaETH 此时上线胜算几何？',
      source: 'chaincatcher',
      summary:
        'Layer 2 网络 MegaETH 在 L2 叙事降温时选择上线主网，主打「即时区块链」，但能否在性能与开发者生态上突围仍存不确定性。',
    },
    {
      id: 'rootdata-transparency',
      time: '21:08',
      title: 'RootData：2026 年 1 月加密交易所透明度研究报告',
      source: 'chaincatcher',
      summary:
        '报告指出 2026 年 1 月全球交易所整体交易量同比下滑超 50%，市值回落 20%+，交易所资产证明与真实流动性成为行业焦点。',
    },
    {
      id: 'ark-stablecoin',
      time: '21:00',
      title: 'ARK Invest：稳定币，下一代货币体系的基石？',
      source: 'ODAILY',
      summary:
        'ARK 数字资产研究团队认为，在 GENIUS 法案等监管进展推动下，稳定币有望从加密原生资产升级为全球支付与结算网络的关键基础设施。',
    },
    {
      id: 'openclaw-deploy',
      time: '20:00',
      title: 'OpenClaw 极简部署：最快 1 分钟搞定，纯小白友好教程',
      source: 'ODAILY',
      summary:
        'Biteye 团队总结 6 种 OpenClaw 部署路径，从云端到本地一键脚本，帮助非技术用户用最低门槛拥有自己的 AI 员工。',
    },
    {
      id: 'crypto-2002-analogy',
      time: '19:30',
      title: '黎明前的黑暗：2026 年的 Crypto = 2002 年的互联网',
      source: 'PANews',
      summary:
        '作者将当下加密市场的沉寂比作 2002 年互联网泡沫破裂后的重建期，认为真正具备长期价值的项目正在这一阶段完成打磨。',
    },
    {
      id: 'daily-intel-0210',
      time: '19:18',
      title: '2 月 10 日市场关键情报，你错过了多少？',
      source: 'BLOCKBEATS',
      summary:
        '盘点修复性反弹后的横盘走势、Base 生态代币 BNKR 异动、美政府停摆风险等宏观与微观事件，作为当日情绪与风险偏好的快照。',
    },
    {
      id: 'kalshi-nba',
      time: '19:00',
      title: '字母哥入股 Kalshi：当 NBA 巨星成为「利益相关方」',
      source: 'ODAILY',
      summary:
        'NBA 球星字母哥入股预测市场平台 Kalshi，被视为体育明星与金融基础设施深度绑定的新样本，或将带动更广泛的散户参与。',
    },
    {
      id: 'bithumb-2000btc',
      time: '18:34',
      title: '2000 枚 BTC 的险情背后：CEX 账本的根本问题',
      source: 'ODAILY',
      summary:
        '韩国交易所 Bithumb 在一次营销活动中意外引发 2000 BTC 级别资金错配事故，暴露中心化交易所内部账本与风控流程的结构性风险。',
    },
  ];

  const flashItems = [
    {
      time: '21:00',
      title: 'ARK Invest：稳定币，下一代货币体系的基石？',
      source: 'ODAILY',
      summary:
        '稳定币供应与使用数据创新高，ARK 认为其有望成为跨境支付与结算的新基础设施。',
    },
    {
      time: '20:00',
      title: 'OpenClaw 极简部署：最快 1 分钟搞定，纯小白友好教程',
      source: 'ODAILY',
      summary:
        '横评 6 种 OpenClaw 部署方式，从 Docker 到一键脚本，帮助非技术用户快速搭建 AI 代理。',
    },
    {
      time: '19:30',
      title: '黎明前的黑暗：2026 年的 Crypto = 2002 年的互联网',
      source: 'PANews',
      summary: '作者将当前加密寒冬类比 2002 年互联网谷底，强调长周期创新与耐心的重要性。',
    },
    {
      time: '19:18',
      title: '2 月 10 日市场关键情报，你错过了多少？',
      source: 'BLOCKBEATS',
      summary: '汇总当日宏观要闻、交易所动向与项目进展，作为盘后情报笔记。',
    },
  ];

  const fearGreed = {
    today: 10,
    yesterday: 8,
    lastWeek: 41,
    label: '极度恐惧',
  };

  useEffect(() => {
    if (!focusNewsId) {
      return;
    }

    // 先等待当前页面与列表渲染完成，再执行滚动与高亮动画，保证切换动作线性进行
    const scrollTimer = window.setTimeout(() => {
      const element = document.getElementById(`discover-news-${focusNewsId}`);
      if (!element) {
        onFocusHandled?.();
        return;
      }

      element.scrollIntoView({ behavior: 'smooth', block: 'center' });

      const animationTimer = window.setTimeout(() => {
        element.classList.add('discover-news-item--focused');
        const cleanupTimer = window.setTimeout(() => {
          element.classList.remove('discover-news-item--focused');
          onFocusHandled?.();
        }, 800);

        return () => {
          window.clearTimeout(cleanupTimer);
        };
      }, 500);

      return () => {
        window.clearTimeout(animationTimer);
      };
    }, 0);

    return () => {
      window.clearTimeout(scrollTimer);
    };
  }, [focusNewsId, onFocusHandled]);

  return (
    <div className="module-container discover-module-container">
      <div className="page-title">
        <h1 className="title-text">市场资讯</h1>
      </div>

      <div className="discover-layout">
        {/* 左侧新闻列表：约 2/3 宽度 */}
        <section className="discover-news-section">
          <h2 className="discover-section-title">新闻</h2>
          <ul className="discover-news-list">
            {newsItems.map((item) => (
              <li
                key={item.id}
                id={`discover-news-${item.id}`}
                className="discover-news-item"
              >
                <div className="discover-news-time">{item.time}</div>
                <div className="discover-news-main">
                  <div className="discover-news-title">{item.title}</div>
                  <p className="discover-news-summary">{item.summary}</p>
                  <div className="discover-news-meta">
                    <span className="discover-news-source-chip">{item.source}</span>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </section>

        {/* 右侧：恐惧&贪婪指数 + 快讯 */}
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
            <div className="discover-flash-section">
              <ul className="discover-flash-list">
                {flashItems.map((item, index) => (
                  <li key={index} className="discover-flash-item">
                    <div className="discover-flash-time">{item.time}</div>
                    <div className="discover-flash-content">
                      <div className="discover-flash-title">{item.title}</div>
                      <p className="discover-flash-summary">{item.summary}</p>
                      <div className="discover-flash-meta">
                        <span className="discover-flash-source-chip">{item.source}</span>
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
};

export default DiscoverModule;

