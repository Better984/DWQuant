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
  defaultLayout?: IndicatorCardLayout;
  chartOption?: EChartsOption;
  customView?: IndicatorCardCustomView;
  chartWrapClassName?: string;
  controls?: IndicatorCardControl;
}

interface IndicatorCardLayout {
  cols: number;
  rows: number;
}

interface IndicatorGridMetrics {
  columnCount: number;
  columnWidth: number;
  rowHeight: number;
  gap: number;
}

type IndicatorCardLayoutMap = Record<string, IndicatorCardLayout>;

type EtfAsset = 'BTC' | 'ETH' | 'SOL' | 'XRP';
type FootprintAsset = 'BTC' | 'ETH';
type LongShortAsset = 'BTC' | 'ETH';
type ExchangeAssetView = 'assets' | 'balance-list' | 'balance-chart';
type HyperliquidView =
  | 'whale-alert'
  | 'whale-position'
  | 'position'
  | 'user-position'
  | 'wallet-position-distribution'
  | 'wallet-pnl-distribution';

type IndicatorCardCustomView = {
  type: 'grayscale-holdings';
  runtime?: GrayscaleHoldingsRuntime | null;
} | {
  type: 'coin-unlock-panel';
  runtime?: CoinUnlockPanelRuntime | null;
  selectedSymbol?: string;
  onSelectSymbol?: (symbol: string) => void;
} | {
  type: 'exchange-asset-panel';
  runtime?: ExchangeAssetPanelRuntime | null;
  palette: ChartPalette;
} | {
  type: 'hyperliquid-panel';
  runtime?: HyperliquidPanelRuntime | null;
  palette: ChartPalette;
};

type IndicatorCardControl = {
  type: 'asset-switch';
  indicatorId: 'etf-flow' | 'futures-footprint' | 'long-short';
  ariaLabel: string;
  activeAsset: string;
  items: Array<{
    asset: string;
    label: string;
    subtitle: string;
  }>;
};

interface FearGreedRuntime {
  value: number;
  classification: string;
  stale: boolean;
  sourceTs?: number;
}

interface EtfFlowRuntime {
  asset: EtfAsset;
  latestNetFlowUsd: number;
  stale: boolean;
  sourceTs?: number;
  series: Array<{
    ts: number;
    netFlowUsd: number;
  }>;
}

interface LongShortRatioRuntime {
  asset: LongShortAsset;
  exchange: string;
  symbol: string;
  interval: string;
  latestRatio: number;
  latestLongPercent: number;
  latestShortPercent: number;
  stale: boolean;
  sourceTs?: number;
  series: Array<{
    ts: number;
    ratio: number;
    longPercent: number;
    shortPercent: number;
  }>;
}

interface FuturesFootprintBin {
  priceFrom: number;
  priceTo: number;
  buyVolume: number;
  sellVolume: number;
  buyUsd: number;
  sellUsd: number;
  deltaUsd: number;
  buyTradeCount: number;
  sellTradeCount: number;
}

interface FuturesFootprintRuntime {
  asset: FootprintAsset;
  exchange: string;
  symbol: string;
  interval: string;
  latestNetDeltaUsd: number;
  latestBuyUsd: number;
  latestSellUsd: number;
  latestBuyVolume: number;
  latestSellVolume: number;
  latestTotalTradeCount: number;
  latestPriceLow: number;
  latestPriceHigh: number;
  stale: boolean;
  sourceTs?: number;
  series: Array<{
    ts: number;
    netDeltaUsd: number;
    buyUsd: number;
    sellUsd: number;
    buyVolume: number;
    sellVolume: number;
    totalTradeCount: number;
    priceLow: number;
    priceHigh: number;
  }>;
  latestBins: FuturesFootprintBin[];
}

interface GrayscaleHoldingItem {
  symbol: string;
  primaryMarketPrice: number;
  secondaryMarketPrice: number;
  premiumRate: number;
  holdingsAmount: number;
  holdingsUsd: number;
  holdingsAmountChange1d: number;
  holdingsAmountChange7d: number;
  holdingsAmountChange30d: number;
  closeTime?: number;
  updateTime?: number;
}

interface GrayscaleHoldingsRuntime {
  stale: boolean;
  sourceTs?: number;
  totalHoldingsUsd: number;
  assetCount: number;
  maxHoldingSymbol?: string;
  maxHoldingUsd?: number;
  items: GrayscaleHoldingItem[];
}

interface CoinUnlockItem {
  symbol: string;
  name?: string;
  iconUrl?: string;
  price?: number;
  priceChange24h?: number;
  marketCap?: number;
  unlockedSupply?: number;
  lockedSupply?: number;
  unlockedPercent?: number;
  lockedPercent?: number;
  nextUnlockTime?: number;
  nextUnlockAmount?: number;
  nextUnlockPercent?: number;
  nextUnlockValue?: number;
  updateTime?: number;
}

interface CoinUnlockListRuntime {
  stale: boolean;
  sourceTs?: number;
  totalCoinCount: number;
  totalMarketCap: number;
  totalNextUnlockValue: number;
  nextUnlockSymbol?: string;
  nextUnlockTime?: number;
  nextUnlockValue?: number;
  items: CoinUnlockItem[];
}

interface CoinVestingAllocationItem {
  label: string;
  unlockedPercent?: number;
  lockedPercent?: number;
  unlockedAmount?: number;
  lockedAmount?: number;
  nextUnlockTime?: number;
  nextUnlockAmount?: number;
}

interface CoinVestingScheduleItem {
  label?: string;
  unlockTime?: number;
  unlockAmount?: number;
  unlockPercent?: number;
  unlockValue?: number;
}

interface CoinVestingRuntime {
  symbol: string;
  name?: string;
  iconUrl?: string;
  price?: number;
  priceChange24h?: number;
  marketCap?: number;
  circulatingSupply?: number;
  totalSupply?: number;
  unlockedSupply?: number;
  lockedSupply?: number;
  unlockedPercent?: number;
  lockedPercent?: number;
  nextUnlockTime?: number;
  nextUnlockAmount?: number;
  nextUnlockPercent?: number;
  nextUnlockValue?: number;
  stale: boolean;
  sourceTs?: number;
  allocationItems: CoinVestingAllocationItem[];
  scheduleItems: CoinVestingScheduleItem[];
}

interface CoinUnlockPanelRuntime {
  list?: CoinUnlockListRuntime | null;
  vesting?: CoinVestingRuntime | null;
}

interface ExchangeAssetItem {
  walletAddress?: string;
  symbol: string;
  assetsName?: string;
  balance: number;
  balanceUsd: number;
  price?: number;
}

interface ExchangeAssetsRuntime {
  exchangeName: string;
  stale: boolean;
  sourceTs?: number;
  totalBalanceUsd: number;
  totalAssetCount: number;
  items: ExchangeAssetItem[];
}

interface ExchangeBalanceListItem {
  exchangeName: string;
  balance: number;
  change1d?: number;
  changePercent1d?: number;
  change7d?: number;
  changePercent7d?: number;
  change30d?: number;
  changePercent30d?: number;
}

interface ExchangeBalanceListRuntime {
  symbol: string;
  stale: boolean;
  sourceTs?: number;
  totalBalance: number;
  totalExchangeCount: number;
  items: ExchangeBalanceListItem[];
}

interface ExchangeBalanceChartSeries {
  exchangeName: string;
  latestBalance?: number;
  values: Array<number | null>;
}

interface ExchangeBalanceChartRuntime {
  symbol: string;
  stale: boolean;
  sourceTs?: number;
  latestTotalBalance: number;
  totalSeriesCount: number;
  timeList: number[];
  priceList: Array<number | null>;
  series: ExchangeBalanceChartSeries[];
}

interface ExchangeAssetPanelRuntime {
  assets?: ExchangeAssetsRuntime | null;
  balanceList?: ExchangeBalanceListRuntime | null;
  balanceChart?: ExchangeBalanceChartRuntime | null;
}

interface HyperliquidWhaleAlertItem {
  user: string;
  symbol: string;
  positionSize: number;
  entryPrice?: number;
  liqPrice?: number;
  positionValueUsd: number;
  positionAction?: number;
  createTime?: number;
}

interface HyperliquidWhaleAlertRuntime {
  stale: boolean;
  sourceTs?: number;
  totalPositionValueUsd: number;
  totalAlertCount: number;
  longAlertCount: number;
  shortAlertCount: number;
  items: HyperliquidWhaleAlertItem[];
}

interface HyperliquidPositionItem {
  user: string;
  symbol: string;
  positionSize: number;
  entryPrice?: number;
  markPrice?: number;
  liqPrice?: number;
  leverage?: number;
  marginBalance?: number;
  positionValueUsd: number;
  unrealizedPnl?: number;
  fundingFee?: number;
  marginMode?: string;
  createTime?: number;
  updateTime?: number;
}

interface HyperliquidWhalePositionRuntime {
  stale: boolean;
  sourceTs?: number;
  totalPositionValueUsd: number;
  totalMarginBalance: number;
  totalPositionCount: number;
  longCount: number;
  shortCount: number;
  items: HyperliquidPositionItem[];
}

interface HyperliquidPositionRuntime {
  symbol: string;
  stale: boolean;
  sourceTs?: number;
  totalPages: number;
  currentPage: number;
  totalPositionValueUsd: number;
  totalPositionCount: number;
  longCount: number;
  shortCount: number;
  items: HyperliquidPositionItem[];
}

interface HyperliquidUserMarginSummary {
  accountValue: number;
  totalNtlPos?: number;
  totalRawUsd?: number;
  totalMarginUsed?: number;
}

interface HyperliquidUserAssetPosition {
  type?: string;
  coin: string;
  size: number;
  leverageType?: string;
  leverageValue?: number;
  entryPrice?: number;
  positionValue: number;
  unrealizedPnl?: number;
  returnOnEquity?: number;
  liquidationPrice?: number;
  maxLeverage?: number;
  cumFundingAllTime?: number;
  cumFundingSinceOpen?: number;
  cumFundingSinceChange?: number;
}

interface HyperliquidUserPositionRuntime {
  userAddress: string;
  stale: boolean;
  sourceTs?: number;
  accountValue: number;
  withdrawable?: number;
  totalNotionalPosition?: number;
  totalMarginUsed?: number;
  crossMaintenanceMarginUsed?: number;
  marginSummary?: HyperliquidUserMarginSummary;
  crossMarginSummary?: HyperliquidUserMarginSummary;
  assetPositions: HyperliquidUserAssetPosition[];
}

interface HyperliquidWalletDistributionItem {
  groupName: string;
  allAddressCount?: number;
  positionAddressCount?: number;
  positionAddressPercent?: number;
  biasScore?: number;
  biasRemark?: string;
  minimumAmount?: number;
  maximumAmount?: number;
  longPositionUsd: number;
  shortPositionUsd: number;
  longPositionUsdPercent?: number;
  shortPositionUsdPercent?: number;
  positionUsd: number;
  profitAddressCount?: number;
  lossAddressCount?: number;
  profitAddressPercent?: number;
  lossAddressPercent?: number;
}

interface HyperliquidWalletDistributionRuntime {
  stale: boolean;
  sourceTs?: number;
  totalPositionUsd: number;
  totalGroupCount: number;
  totalPositionAddressCount: number;
  items: HyperliquidWalletDistributionItem[];
}

interface HyperliquidPanelRuntime {
  whaleAlert?: HyperliquidWhaleAlertRuntime | null;
  whalePosition?: HyperliquidWhalePositionRuntime | null;
  position?: HyperliquidPositionRuntime | null;
  userPosition?: HyperliquidUserPositionRuntime | null;
  walletPositionDistribution?: HyperliquidWalletDistributionRuntime | null;
  walletPnlDistribution?: HyperliquidWalletDistributionRuntime | null;
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

const ETF_ASSET_OPTIONS: Array<{
  asset: EtfAsset;
  label: string;
  subtitle: string;
  displayName: string;
}> = [
  { asset: 'BTC', label: 'BTC', subtitle: '比特币', displayName: '比特币' },
  { asset: 'ETH', label: 'ETH', subtitle: '以太坊', displayName: '以太坊' },
  { asset: 'SOL', label: 'SOL', subtitle: 'Solana', displayName: 'Solana' },
  { asset: 'XRP', label: 'XRP', subtitle: '瑞波币', displayName: 'XRP' },
];

const FOOTPRINT_ASSET_OPTIONS: Array<{
  asset: FootprintAsset;
  label: string;
  subtitle: string;
  displayName: string;
}> = [
  { asset: 'BTC', label: 'BTC', subtitle: '比特币', displayName: '比特币' },
  { asset: 'ETH', label: 'ETH', subtitle: '以太坊', displayName: '以太坊' },
];

const LONG_SHORT_ASSET_OPTIONS: Array<{
  asset: LongShortAsset;
  label: string;
  subtitle: string;
  displayName: string;
}> = [
  { asset: 'BTC', label: 'BTC', subtitle: '比特币', displayName: '比特币' },
  { asset: 'ETH', label: 'ETH', subtitle: '以太坊', displayName: '以太坊' },
];

const LONG_SHORT_VISIBLE_POINT_LIMIT = 192;
const FOOTPRINT_VISIBLE_CANDLE_LIMIT = 96;
const DEFAULT_COIN_UNLOCK_SYMBOL = 'HYPE';
const INDICATOR_LAYOUT_STORAGE_KEY = 'dwquant.indicator-module.layout.v1';
const INDICATOR_GRID_MIN_COLUMN_WIDTH = 320;
const INDICATOR_GRID_MAX_COLUMNS = 4;
const INDICATOR_GRID_GAP = 16;
const INDICATOR_GRID_BASE_ROW_HEIGHT = 480;
const INDICATOR_CARD_MAX_ROW_SPAN = 3;
const INDICATOR_GRID_FALLBACK_METRICS: IndicatorGridMetrics = {
  columnCount: 1,
  columnWidth: INDICATOR_GRID_MIN_COLUMN_WIDTH,
  rowHeight: INDICATOR_GRID_BASE_ROW_HEIGHT,
  gap: INDICATOR_GRID_GAP,
};
const INDICATOR_CARD_FALLBACK_LAYOUT: IndicatorCardLayout = {
  cols: 1,
  rows: 1,
};

const GRAYSCALE_HOLDINGS_FALLBACK_ITEMS: GrayscaleHoldingItem[] = [
  {
    symbol: 'BTC',
    primaryMarketPrice: 52.13,
    secondaryMarketPrice: 51.48,
    premiumRate: -1.25,
    holdingsAmount: 219530.42,
    holdingsUsd: 4_010_000_000,
    holdingsAmountChange1d: 0,
    holdingsAmountChange7d: -18.25,
    holdingsAmountChange30d: -412.8,
    closeTime: Date.now() - 3_600_000,
    updateTime: Date.now() - 3_600_000,
  },
  {
    symbol: 'ETH',
    primaryMarketPrice: 29.89,
    secondaryMarketPrice: 29.71,
    premiumRate: -0.6,
    holdingsAmount: 2_630_007.61,
    holdingsUsd: 4_290_752_215.83,
    holdingsAmountChange1d: 0,
    holdingsAmountChange7d: 0,
    holdingsAmountChange30d: 0,
    closeTime: Date.now() - 3_600_000,
    updateTime: Date.now() - 3_600_000,
  },
  {
    symbol: 'ETC',
    primaryMarketPrice: 11.99,
    secondaryMarketPrice: 6.63,
    premiumRate: -44.7,
    holdingsAmount: 11_181_376.73,
    holdingsUsd: 181_440_200.25,
    holdingsAmountChange1d: 0,
    holdingsAmountChange7d: -4_596.12,
    holdingsAmountChange30d: -20_697.67,
    closeTime: Date.now() - 3_600_000,
    updateTime: Date.now() - 3_600_000,
  },
];

const COIN_UNLOCK_LIST_FALLBACK_ITEMS: CoinUnlockItem[] = [
  {
    symbol: 'HYPE',
    name: 'Hyperliquid',
    price: 30.52,
    priceChange24h: -3.42,
    marketCap: 10_120_000_000,
    unlockedSupply: 333_928_180,
    lockedSupply: 666_071_820,
    unlockedPercent: 33.39,
    lockedPercent: 66.61,
    nextUnlockTime: Date.now() + 5 * 86_400_000,
    nextUnlockAmount: 12_500_000,
    nextUnlockPercent: 1.25,
    nextUnlockValue: 381_500_000,
    updateTime: Date.now() - 30 * 60_000,
  },
  {
    symbol: 'ARB',
    name: 'Arbitrum',
    price: 1.06,
    priceChange24h: 2.14,
    marketCap: 2_920_000_000,
    unlockedSupply: 3_627_500_000,
    lockedSupply: 6_372_500_000,
    unlockedPercent: 36.28,
    lockedPercent: 63.72,
    nextUnlockTime: Date.now() + 12 * 86_400_000,
    nextUnlockAmount: 92_650_000,
    nextUnlockPercent: 0.93,
    nextUnlockValue: 98_209_000,
    updateTime: Date.now() - 30 * 60_000,
  },
  {
    symbol: 'STRK',
    name: 'Starknet',
    price: 1.85,
    priceChange24h: -1.72,
    marketCap: 1_960_000_000,
    unlockedSupply: 1_728_000_000,
    lockedSupply: 8_272_000_000,
    unlockedPercent: 17.28,
    lockedPercent: 82.72,
    nextUnlockTime: Date.now() + 18 * 86_400_000,
    nextUnlockAmount: 64_000_000,
    nextUnlockPercent: 0.64,
    nextUnlockValue: 118_400_000,
    updateTime: Date.now() - 30 * 60_000,
  },
  {
    symbol: 'SUI',
    name: 'Sui',
    price: 1.56,
    priceChange24h: 1.08,
    marketCap: 4_260_000_000,
    unlockedSupply: 3_176_000_000,
    lockedSupply: 6_824_000_000,
    unlockedPercent: 31.76,
    lockedPercent: 68.24,
    nextUnlockTime: Date.now() + 25 * 86_400_000,
    nextUnlockAmount: 34_620_000,
    nextUnlockPercent: 0.35,
    nextUnlockValue: 54_007_200,
    updateTime: Date.now() - 30 * 60_000,
  },
];

const COIN_VESTING_FALLBACK_RUNTIME_MAP: Record<string, CoinVestingRuntime> = {
  HYPE: {
    symbol: 'HYPE',
    name: 'Hyperliquid',
    price: 30.52,
    priceChange24h: -3.42,
    marketCap: 10_120_000_000,
    circulatingSupply: 333_928_180,
    totalSupply: 1_000_000_000,
    unlockedSupply: 333_928_180,
    lockedSupply: 666_071_820,
    unlockedPercent: 33.39,
    lockedPercent: 66.61,
    nextUnlockTime: Date.now() + 5 * 86_400_000,
    nextUnlockAmount: 12_500_000,
    nextUnlockPercent: 1.25,
    nextUnlockValue: 381_500_000,
    stale: false,
    sourceTs: Date.now() - 30 * 60_000,
    allocationItems: [
      { label: '社区激励', unlockedPercent: 12.8, lockedPercent: 17.2, unlockedAmount: 128_000_000, lockedAmount: 172_000_000, nextUnlockTime: Date.now() + 5 * 86_400_000, nextUnlockAmount: 6_000_000 },
      { label: '核心贡献者', unlockedPercent: 8.1, lockedPercent: 21.9, unlockedAmount: 81_000_000, lockedAmount: 219_000_000, nextUnlockTime: Date.now() + 32 * 86_400_000, nextUnlockAmount: 3_500_000 },
      { label: '生态基金', unlockedPercent: 7.4, lockedPercent: 22.6, unlockedAmount: 74_000_000, lockedAmount: 226_000_000, nextUnlockTime: Date.now() + 12 * 86_400_000, nextUnlockAmount: 2_100_000 },
      { label: '投资人', unlockedPercent: 5.09, lockedPercent: 5.91, unlockedAmount: 50_928_180, lockedAmount: 59_071_820, nextUnlockTime: Date.now() + 18 * 86_400_000, nextUnlockAmount: 900_000 },
    ],
    scheduleItems: [
      { label: '下一次解锁', unlockTime: Date.now() + 5 * 86_400_000, unlockAmount: 12_500_000, unlockPercent: 1.25, unlockValue: 381_500_000 },
      { label: '社区轮次', unlockTime: Date.now() + 12 * 86_400_000, unlockAmount: 4_200_000, unlockPercent: 0.42, unlockValue: 128_184_000 },
      { label: '团队轮次', unlockTime: Date.now() + 32 * 86_400_000, unlockAmount: 3_500_000, unlockPercent: 0.35, unlockValue: 106_820_000 },
    ],
  },
  ARB: {
    symbol: 'ARB',
    name: 'Arbitrum',
    price: 1.06,
    priceChange24h: 2.14,
    marketCap: 2_920_000_000,
    circulatingSupply: 3_627_500_000,
    totalSupply: 10_000_000_000,
    unlockedSupply: 3_627_500_000,
    lockedSupply: 6_372_500_000,
    unlockedPercent: 36.28,
    lockedPercent: 63.72,
    nextUnlockTime: Date.now() + 12 * 86_400_000,
    nextUnlockAmount: 92_650_000,
    nextUnlockPercent: 0.93,
    nextUnlockValue: 98_209_000,
    stale: false,
    sourceTs: Date.now() - 30 * 60_000,
    allocationItems: [
      { label: '生态 DAO', unlockedPercent: 11.78, lockedPercent: 30.22, unlockedAmount: 1_178_000_000, lockedAmount: 3_022_000_000, nextUnlockTime: Date.now() + 12 * 86_400_000, nextUnlockAmount: 32_000_000 },
      { label: '团队与顾问', unlockedPercent: 9.4, lockedPercent: 16.6, unlockedAmount: 940_000_000, lockedAmount: 1_660_000_000, nextUnlockTime: Date.now() + 20 * 86_400_000, nextUnlockAmount: 24_500_000 },
      { label: '投资人', unlockedPercent: 6.1, lockedPercent: 13.9, unlockedAmount: 610_000_000, lockedAmount: 1_390_000_000, nextUnlockTime: Date.now() + 20 * 86_400_000, nextUnlockAmount: 18_000_000 },
    ],
    scheduleItems: [
      { label: '月度解锁', unlockTime: Date.now() + 12 * 86_400_000, unlockAmount: 92_650_000, unlockPercent: 0.93, unlockValue: 98_209_000 },
      { label: '顾问释放', unlockTime: Date.now() + 20 * 86_400_000, unlockAmount: 42_500_000, unlockPercent: 0.43, unlockValue: 45_050_000 },
    ],
  },
};

const EXCHANGE_ASSET_FALLBACK_ITEMS: ExchangeAssetItem[] = [
  { symbol: 'USDT', assetsName: 'Tether USDt', balance: 18_383_085_128.34, balanceUsd: 18_382_419_512.11, price: 1 },
  { symbol: 'BTC', assetsName: 'Bitcoin', balance: 248_597.58, balanceUsd: 17_671_474_334.67, price: 71_084.66 },
  { symbol: 'BNB', assetsName: 'BNB', balance: 17_195_730.36, balanceUsd: 11_127_107_952.6, price: 647.09 },
  { symbol: 'ETH', assetsName: 'Ethereum', balance: 1_996_008.37, balanceUsd: 4_157_761_775.15, price: 2_083.04 },
  { symbol: 'USDC', assetsName: 'USDC', balance: 2_559_482_885, balanceUsd: 2_559_324_851.93, price: 1 },
];

const EXCHANGE_BALANCE_LIST_FALLBACK_ITEMS: ExchangeBalanceListItem[] = [
  { exchangeName: 'Coinbase Pro', balance: 793_311.6, changePercent1d: 0.22, changePercent7d: 0.01, changePercent30d: -0.05 },
  { exchangeName: 'Binance', balance: 654_343.12, changePercent1d: -0.53, changePercent7d: -1.41, changePercent30d: 1.14 },
  { exchangeName: 'Bitfinex', balance: 402_710.99, changePercent1d: -5.67, changePercent7d: -6.32, changePercent30d: -4.74 },
  { exchangeName: 'Kraken', balance: 150_743.74, changePercent1d: -0.26, changePercent7d: 0.07, changePercent30d: 4.33 },
  { exchangeName: 'OKX', balance: 120_777.31, changePercent1d: -0.32, changePercent7d: 0.04, changePercent30d: -0.42 },
];

const EXCHANGE_BALANCE_CHART_FALLBACK_TIMES = Array.from({ length: 7 }, (_, index) => Date.now() - (6 - index) * 86_400_000);
const EXCHANGE_BALANCE_CHART_FALLBACK_PRICES = [68_200, 69_150, 70_320, 71_080, 70_540, 69_880, 70_956];
const EXCHANGE_BALANCE_CHART_FALLBACK_SERIES: ExchangeBalanceChartSeries[] = [
  { exchangeName: 'Coinbase Pro', latestBalance: 793_311.6, values: [792_100, 792_480, 792_960, 793_210, 793_005, 793_140, 793_311.6] },
  { exchangeName: 'Binance', latestBalance: 654_343.12, values: [658_240, 657_810, 656_950, 655_880, 655_210, 654_780, 654_343.12] },
  { exchangeName: 'Bitfinex', latestBalance: 402_710.99, values: [408_920, 407_860, 406_220, 404_510, 403_980, 403_120, 402_710.99] },
];

const HYPERLIQUID_DEFAULT_USER_ADDRESS = '0xa5b0edf6b55128e0ddae8e51ac538c3188401d41';

const HYPERLIQUID_WHALE_ALERT_FALLBACK_ITEMS: HyperliquidWhaleAlertItem[] = [
  { user: '0xcab59c7a92b8f7c4d5cde72bb7669ee7d75b6e6e', symbol: 'BTC', positionSize: 150, entryPrice: 70_599, liqPrice: 65_224.45, positionValueUsd: 10_594_200, positionAction: 1, createTime: Date.now() - 900_000 },
  { user: '0xcab59c7a92b8f7c4d5cde72bb7669ee7d75b6e6e', symbol: 'ETH', positionSize: 4_000, entryPrice: 2_065.61, liqPrice: 2_934.49, positionValueUsd: 8_390_400, positionAction: 2, createTime: Date.now() - 1_100_000 },
  { user: '0x3bcae23e8c380dab4732e9a159c0456f12d866f3', symbol: 'BTC', positionSize: -206.54, entryPrice: 70_933.2, liqPrice: 1_640_398.6, positionValueUsd: 14_687_237.89, positionAction: 2, createTime: Date.now() - 1_300_000 },
  { user: '0xa958b707a457d149cea979e5988f9f90a2d78e39', symbol: 'ETH', positionSize: 1_789.51, entryPrice: 2_080.67, liqPrice: 1_903.92, positionValueUsd: 3_690_871.59, positionAction: 1, createTime: Date.now() - 1_800_000 },
  { user: '0x687feda45b6847763f5bf5c01a2f6c1a3d727f5c', symbol: 'BTC', positionSize: 100, entryPrice: 70_643.4, liqPrice: 58_606.76, positionValueUsd: 7_064_700, positionAction: 1, createTime: Date.now() - 2_400_000 },
];

const HYPERLIQUID_WHALE_POSITION_FALLBACK_ITEMS: HyperliquidPositionItem[] = [
  { user: HYPERLIQUID_DEFAULT_USER_ADDRESS, symbol: 'ETH', positionSize: 70_000.67, entryPrice: 1_991.53, markPrice: 2_063, liqPrice: 1_500.09, leverage: 15, marginBalance: 9_626_025.87, positionValueUsd: 144_390_387.99, unrealizedPnl: 4_981_633.28, fundingFee: 530_484.77, marginMode: 'cross', createTime: Date.now() - 86_400_000, updateTime: Date.now() - 180_000 },
  { user: '0x6c8512516ce5669d35113a11ca8b8de322fd84f6', symbol: 'ETH', positionSize: 50_000.01, entryPrice: 2_012.11, markPrice: 2_063, liqPrice: 1_455.93, leverage: 20, marginBalance: 5_156_750.61, positionValueUsd: 103_135_012.17, unrealizedPnl: 2_529_238.95, fundingFee: 373_861.69, marginMode: 'cross', createTime: Date.now() - 78_000_000, updateTime: Date.now() - 240_000 },
  { user: '0x0ddf9bae2af4b874b96d287a5ad42eb47138a902', symbol: 'BTC', positionSize: -1_000, entryPrice: 68_182.7, markPrice: 70_618, liqPrice: 100_389.83, leverage: 3, marginBalance: 23_543_666.67, positionValueUsd: 70_631_000, unrealizedPnl: -2_448_287.36, fundingFee: 8_893.96, marginMode: 'cross', createTime: Date.now() - 7_200_000, updateTime: Date.now() - 300_000 },
  { user: '0x082e843a431aef031264dc232693dd710aedca88', symbol: 'HYPE', positionSize: 1_380_042.66, entryPrice: 38.6755, markPrice: 30.52, liqPrice: 23.9124, leverage: 5, marginBalance: 8_422_676.36, positionValueUsd: 42_113_381.81, unrealizedPnl: -11_260_548.23, fundingFee: 1_637_990.84, marginMode: 'cross', createTime: Date.now() - 120_000_000, updateTime: Date.now() - 360_000 },
  { user: '0xb581d667c53fd8a50bf7ffd817be0e62daa16f4f', symbol: 'SOL', positionSize: -254_771.48, entryPrice: 76.8984, markPrice: 87.674, liqPrice: 117.0449, leverage: 3, marginBalance: 7_449_857.77, positionValueUsd: 22_349_573.31, unrealizedPnl: -2_758_029.19, fundingFee: 35_662.09, marginMode: 'cross', createTime: Date.now() - 48_000_000, updateTime: Date.now() - 420_000 },
];

const HYPERLIQUID_USER_POSITION_FALLBACK_ASSETS: HyperliquidUserAssetPosition[] = [
  { type: 'oneWay', coin: 'BTC', size: 700, leverageType: 'cross', leverageValue: 20, entryPrice: 68_420.2, positionValue: 49_432_600, unrealizedPnl: 1_538_427.89, returnOnEquity: 0.64, liquidationPrice: 15_514.18, maxLeverage: 20, cumFundingAllTime: 312.55, cumFundingSinceOpen: 312.55, cumFundingSinceChange: 7_256.41 },
  { type: 'oneWay', coin: 'ETH', size: 70_000.67, leverageType: 'cross', leverageValue: 15, entryPrice: 1_991.53, positionValue: 144_404_388.13, unrealizedPnl: 4_995_633.42, returnOnEquity: 0.54, liquidationPrice: 1_499.99, maxLeverage: 15, cumFundingAllTime: 1_108_708.5, cumFundingSinceOpen: 530_484.77, cumFundingSinceChange: 263_963.1 },
];

const HYPERLIQUID_WALLET_POSITION_DISTRIBUTION_FALLBACK_ITEMS: HyperliquidWalletDistributionItem[] = [
  { groupName: 'shrimp', allAddressCount: 382_331, positionAddressCount: 21_329, positionAddressPercent: 5.58, biasScore: -0.42, biasRemark: 'bearish', minimumAmount: 0, maximumAmount: 250, longPositionUsd: 5_548_902.22, shortPositionUsd: 7_833_949.97, longPositionUsdPercent: 41.46, shortPositionUsdPercent: 58.54, positionUsd: 13_382_852.19, profitAddressCount: 10_106, lossAddressCount: 11_703, profitAddressPercent: 46.34, lossAddressPercent: 53.66 },
  { groupName: 'fish', allAddressCount: 47_466, positionAddressCount: 22_621, positionAddressPercent: 47.66, biasScore: 0.36, biasRemark: 'slightly_bullish', minimumAmount: 250, maximumAmount: 10_000, longPositionUsd: 83_925_944.53, shortPositionUsd: 50_517_821.19, longPositionUsdPercent: 62.42, shortPositionUsdPercent: 37.58, positionUsd: 134_443_765.72, profitAddressCount: 10_588, lossAddressCount: 12_202, profitAddressPercent: 46.46, lossAddressPercent: 53.54 },
  { groupName: 'dolphin', allAddressCount: 7_641, positionAddressCount: 4_522, positionAddressPercent: 59.19, biasScore: 0.23, biasRemark: 'slightly_bullish', minimumAmount: 10_000, maximumAmount: 50_000, longPositionUsd: 145_210_179.02, shortPositionUsd: 109_198_753.58, longPositionUsdPercent: 57.08, shortPositionUsdPercent: 42.92, positionUsd: 254_408_932.6, profitAddressCount: 2_100, lossAddressCount: 2_431, profitAddressPercent: 46.35, lossAddressPercent: 53.65 },
  { groupName: 'small_whale', allAddressCount: 2_087, positionAddressCount: 1_454, positionAddressPercent: 69.67, biasScore: -0.03, biasRemark: 'indecisive', minimumAmount: 100_000, maximumAmount: 500_000, longPositionUsd: 337_878_491.13, shortPositionUsd: 349_291_597.45, longPositionUsdPercent: 49.17, shortPositionUsdPercent: 50.83, positionUsd: 687_170_088.58, profitAddressCount: 705, lossAddressCount: 749, profitAddressPercent: 48.49, lossAddressPercent: 51.51 },
  { groupName: 'tidal_whale', allAddressCount: 359, positionAddressCount: 282, positionAddressPercent: 78.56, biasScore: -0.26, biasRemark: 'slightly_bearish', minimumAmount: 1_000_000, maximumAmount: 50_000_000, longPositionUsd: 507_822_149.14, shortPositionUsd: 702_159_208.44, longPositionUsdPercent: 41.97, shortPositionUsdPercent: 58.03, positionUsd: 1_209_981_357.58, profitAddressCount: 159, lossAddressCount: 123, profitAddressPercent: 56.38, lossAddressPercent: 43.62 },
  { groupName: 'leviathan', allAddressCount: 97, positionAddressCount: 83, positionAddressPercent: 85.57, biasScore: 0.03, biasRemark: 'indecisive', minimumAmount: 50_000_000, longPositionUsd: 902_234_755.75, shortPositionUsd: 873_520_605.1, longPositionUsdPercent: 50.81, shortPositionUsdPercent: 49.19, positionUsd: 1_775_755_360.85, profitAddressCount: 55, lossAddressCount: 28, profitAddressPercent: 66.27, lossAddressPercent: 33.73 },
];

const HYPERLIQUID_WALLET_PNL_DISTRIBUTION_FALLBACK_ITEMS: HyperliquidWalletDistributionItem[] = [
  { groupName: 'money_printer', allAddressCount: 583, positionAddressCount: 291, positionAddressPercent: 49.92, biasScore: -0.56, biasRemark: 'bearish', minimumAmount: 1_000_000, longPositionUsd: 578_072_307.34, shortPositionUsd: 1_321_188_688.67, longPositionUsdPercent: 30.44, shortPositionUsdPercent: 69.56, positionUsd: 1_899_260_996.01, profitAddressCount: 199, lossAddressCount: 92, profitAddressPercent: 68.38, lossAddressPercent: 31.62 },
  { groupName: 'smart_money', allAddressCount: 2_440, positionAddressCount: 898, positionAddressPercent: 36.81, biasScore: -0.19, biasRemark: 'slightly_bearish', minimumAmount: 100_000, maximumAmount: 1_000_000, longPositionUsd: 242_528_213.44, shortPositionUsd: 330_430_049.53, longPositionUsdPercent: 42.33, shortPositionUsdPercent: 57.67, positionUsd: 572_958_262.96, profitAddressCount: 485, lossAddressCount: 411, profitAddressPercent: 54.13, lossAddressPercent: 45.87 },
  { groupName: 'grinder', allAddressCount: 7_198, positionAddressCount: 1_861, positionAddressPercent: 25.86, biasScore: -0.21, biasRemark: 'slightly_bearish', minimumAmount: 10_000, maximumAmount: 100_000, longPositionUsd: 94_289_780.36, shortPositionUsd: 137_107_703.6, longPositionUsdPercent: 40.75, shortPositionUsdPercent: 59.25, positionUsd: 231_397_483.96, profitAddressCount: 1_006, lossAddressCount: 862, profitAddressPercent: 53.85, lossAddressPercent: 46.15 },
  { groupName: 'humble_earner', allAddressCount: 99_261, positionAddressCount: 16_358, positionAddressPercent: 16.48, biasScore: -0.02, biasRemark: 'indecisive', minimumAmount: 0, maximumAmount: 10_000, longPositionUsd: 50_813_534.81, shortPositionUsd: 52_191_632.71, longPositionUsdPercent: 49.33, shortPositionUsdPercent: 50.67, positionUsd: 103_005_167.53, profitAddressCount: 10_573, lossAddressCount: 5_939, profitAddressPercent: 64.03, lossAddressPercent: 35.97 },
  { groupName: 'full_rekt', allAddressCount: 5_363, positionAddressCount: 1_061, positionAddressPercent: 19.79, biasScore: 0.72, biasRemark: 'bullish', minimumAmount: -100_000, maximumAmount: -1_000_000, longPositionUsd: 546_248_352.29, shortPositionUsd: 251_187_891.67, longPositionUsdPercent: 68.5, shortPositionUsdPercent: 31.5, positionUsd: 797_436_243.96, profitAddressCount: 408, lossAddressCount: 657, profitAddressPercent: 38.31, lossAddressPercent: 61.69 },
  { groupName: 'giga_rekt', allAddressCount: 813, positionAddressCount: 179, positionAddressPercent: 22.02, biasScore: 1.58, biasRemark: 'very_bullish', minimumAmount: -1_000_000, longPositionUsd: 596_177_946.24, shortPositionUsd: 118_538_507.32, longPositionUsdPercent: 83.41, shortPositionUsdPercent: 16.59, positionUsd: 714_716_453.56, profitAddressCount: 66, lossAddressCount: 114, profitAddressPercent: 36.67, lossAddressPercent: 63.33 },
];

const FOOTPRINT_FALLBACK_SERIES = [
  { timeLabel: '09:00', netDeltaUsd: -580_000, buyUsd: 2_600_000, sellUsd: 3_180_000, totalTradeCount: 1780, priceLow: 69_840, priceHigh: 70_020 },
  { timeLabel: '09:15', netDeltaUsd: 920_000, buyUsd: 3_860_000, sellUsd: 2_940_000, totalTradeCount: 2015, priceLow: 69_920, priceHigh: 70_110 },
  { timeLabel: '09:30', netDeltaUsd: 1_460_000, buyUsd: 4_520_000, sellUsd: 3_060_000, totalTradeCount: 2284, priceLow: 69_980, priceHigh: 70_160 },
  { timeLabel: '09:45', netDeltaUsd: -240_000, buyUsd: 3_080_000, sellUsd: 3_320_000, totalTradeCount: 1942, priceLow: 70_020, priceHigh: 70_210 },
  { timeLabel: '10:00', netDeltaUsd: 1_120_000, buyUsd: 4_340_000, sellUsd: 3_220_000, totalTradeCount: 2106, priceLow: 70_080, priceHigh: 70_260 },
  { timeLabel: '10:15', netDeltaUsd: 1_880_000, buyUsd: 5_110_000, sellUsd: 3_230_000, totalTradeCount: 2478, priceLow: 70_120, priceHigh: 70_320 },
];

const FOOTPRINT_FALLBACK_BINS: FuturesFootprintBin[] = [
  { priceFrom: 70_300, priceTo: 70_320, buyVolume: 9.2, sellVolume: 4.1, buyUsd: 647_000, sellUsd: 288_000, deltaUsd: 359_000, buyTradeCount: 102, sellTradeCount: 84 },
  { priceFrom: 70_280, priceTo: 70_300, buyVolume: 11.4, sellVolume: 8.6, buyUsd: 801_000, sellUsd: 604_000, deltaUsd: 197_000, buyTradeCount: 120, sellTradeCount: 117 },
  { priceFrom: 70_260, priceTo: 70_280, buyVolume: 15.1, sellVolume: 14.4, buyUsd: 1_060_000, sellUsd: 1_011_000, deltaUsd: 49_000, buyTradeCount: 163, sellTradeCount: 158 },
  { priceFrom: 70_240, priceTo: 70_260, buyVolume: 18.2, sellVolume: 22.7, buyUsd: 1_279_000, sellUsd: 1_595_000, deltaUsd: -316_000, buyTradeCount: 177, sellTradeCount: 186 },
  { priceFrom: 70_220, priceTo: 70_240, buyVolume: 24.6, sellVolume: 17.8, buyUsd: 1_728_000, sellUsd: 1_250_000, deltaUsd: 478_000, buyTradeCount: 241, sellTradeCount: 214 },
  { priceFrom: 70_200, priceTo: 70_220, buyVolume: 27.1, sellVolume: 12.4, buyUsd: 1_903_000, sellUsd: 871_000, deltaUsd: 1_032_000, buyTradeCount: 256, sellTradeCount: 181 },
];

const normalizeEtfAsset = (input: unknown): EtfAsset | null => {
  if (typeof input !== 'string') {
    return null;
  }

  const normalized = input.trim().toUpperCase();
  if (normalized === 'BITCOIN') {
    return 'BTC';
  }
  if (normalized === 'ETHEREUM') {
    return 'ETH';
  }
  if (normalized === 'SOLANA') {
    return 'SOL';
  }
  if (normalized === 'BTC' || normalized === 'ETH' || normalized === 'SOL' || normalized === 'XRP') {
    return normalized;
  }

  return null;
};

const normalizeFootprintAsset = (input: unknown): FootprintAsset | null => {
  if (typeof input !== 'string') {
    return null;
  }

  const normalized = input.trim().toUpperCase();
  if (normalized === 'BITCOIN' || normalized === 'BTC' || normalized === 'BTCUSDT') {
    return 'BTC';
  }
  if (normalized === 'ETHEREUM' || normalized === 'ETH' || normalized === 'ETHUSDT') {
    return 'ETH';
  }

  return null;
};

const normalizeLongShortAsset = (input: unknown): LongShortAsset | null => {
  if (typeof input !== 'string') {
    return null;
  }

  const normalized = input.trim().toUpperCase();
  if (normalized === 'BITCOIN' || normalized === 'BTC' || normalized === 'BTCUSDT') {
    return 'BTC';
  }
  if (normalized === 'ETHEREUM' || normalized === 'ETH' || normalized === 'ETHUSDT') {
    return 'ETH';
  }

  return null;
};

const normalizeCoinUnlockSymbol = (input: unknown): string | null => {
  if (typeof input !== 'string') {
    return null;
  }

  const normalized = input.trim().toUpperCase();
  return normalized ? normalized : null;
};

const resolveEtfAssetMeta = (asset: EtfAsset) => (
  ETF_ASSET_OPTIONS.find((item) => item.asset === asset) ?? ETF_ASSET_OPTIONS[0]
);

const resolveFootprintAssetMeta = (asset: FootprintAsset) => (
  FOOTPRINT_ASSET_OPTIONS.find((item) => item.asset === asset) ?? FOOTPRINT_ASSET_OPTIONS[0]
);

const resolveLongShortAssetMeta = (asset: LongShortAsset) => (
  LONG_SHORT_ASSET_OPTIONS.find((item) => item.asset === asset) ?? LONG_SHORT_ASSET_OPTIONS[0]
);

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

const formatUsdValue = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  return `${value.toLocaleString('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} USD`;
};

const formatPercentValue = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  return `${value.toFixed(2)}%`;
};

const formatRatioValue = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  return value.toFixed(2);
};

const formatHoldingAmount = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  return value.toLocaleString('zh-CN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  });
};

const formatSignedHoldingChange = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  const prefix = value > 0 ? '+' : '';
  return `${prefix}${formatHoldingAmount(value)}`;
};

const formatSignedPercent = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  const prefix = value > 0 ? '+' : '';
  return `${prefix}${value.toFixed(2)}%`;
};

const formatDateTimeValue = (value?: number): string => {
  if (!value || !Number.isFinite(value)) {
    return '--';
  }

  return new Date(value).toLocaleString('zh-CN', { hour12: false });
};

const formatCoinAmountValue = (value?: number): string => {
  if (value == null || !Number.isFinite(value)) {
    return '--';
  }

  if (Math.abs(value) >= 100_000_000) {
    return `${(value / 100_000_000).toFixed(2)}亿`;
  }
  if (Math.abs(value) >= 10_000) {
    return `${(value / 10_000).toFixed(2)}万`;
  }

  return formatHoldingAmount(value);
};

const formatCountValue = (value: number): string => {
  if (!Number.isFinite(value)) {
    return '--';
  }

  return value.toLocaleString('zh-CN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  });
};

const formatWalletAddress = (value?: string): string => {
  if (!value) {
    return '--';
  }

  if (value.length <= 12) {
    return value;
  }

  return `${value.slice(0, 6)}...${value.slice(-4)}`;
};

const formatDirectionLabel = (positionSize: number): string => (
  positionSize >= 0 ? '多头' : '空头'
);

const getSignedStateClassName = (value?: number): string => {
  if (value == null || !Number.isFinite(value)) {
    return 'is-neutral';
  }

  if (value > 0) {
    return 'is-positive';
  }

  if (value < 0) {
    return 'is-negative';
  }

  return 'is-neutral';
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

const normalizeIndicatorCardLayout = (
  input: Partial<IndicatorCardLayout> | null | undefined,
): IndicatorCardLayout | null => {
  if (!input) {
    return null;
  }

  const cols = Number(input.cols);
  const rows = Number(input.rows);
  if (!Number.isFinite(cols) || !Number.isFinite(rows)) {
    return null;
  }

  return {
    cols: clampNumber(Math.round(cols), 1, INDICATOR_GRID_MAX_COLUMNS),
    rows: clampNumber(Math.round(rows), 1, INDICATOR_CARD_MAX_ROW_SPAN),
  };
};

const readStoredIndicatorCardLayouts = (): IndicatorCardLayoutMap => {
  if (typeof window === 'undefined') {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(INDICATOR_LAYOUT_STORAGE_KEY);
    if (!raw) {
      return {};
    }

    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') {
      return {};
    }

    const nextLayouts: IndicatorCardLayoutMap = {};
    Object.entries(parsed as Record<string, unknown>).forEach(([indicatorId, value]) => {
      if (!value || typeof value !== 'object') {
        return;
      }

      const normalized = normalizeIndicatorCardLayout(value as Partial<IndicatorCardLayout>);
      if (normalized) {
        nextLayouts[indicatorId] = normalized;
      }
    });

    return nextLayouts;
  } catch {
    return {};
  }
};

const buildIndicatorGridMetrics = (containerWidth: number): IndicatorGridMetrics => {
  const safeWidth = Math.max(containerWidth, 1);
  const rawColumnCount = Math.floor((safeWidth + INDICATOR_GRID_GAP) / (INDICATOR_GRID_MIN_COLUMN_WIDTH + INDICATOR_GRID_GAP));
  const columnCount = clampNumber(rawColumnCount, 1, INDICATOR_GRID_MAX_COLUMNS);
  const columnWidth = Math.max(0, (safeWidth - INDICATOR_GRID_GAP * (columnCount - 1)) / columnCount);

  return {
    columnCount,
    columnWidth,
    rowHeight: INDICATOR_GRID_BASE_ROW_HEIGHT,
    gap: INDICATOR_GRID_GAP,
  };
};

const resolveIndicatorCardLayout = (
  indicator: IndicatorCardData,
  storedLayout: IndicatorCardLayout | undefined,
  availableColumnCount: number,
): IndicatorCardLayout => {
  const preferred = storedLayout ?? indicator.defaultLayout ?? INDICATOR_CARD_FALLBACK_LAYOUT;
  const normalized = normalizeIndicatorCardLayout(preferred) ?? INDICATOR_CARD_FALLBACK_LAYOUT;

  return {
    cols: clampNumber(normalized.cols, 1, Math.max(1, availableColumnCount)),
    rows: clampNumber(normalized.rows, 1, INDICATOR_CARD_MAX_ROW_SPAN),
  };
};

const buildIndicators = (
  palette: ChartPalette,
  fearGreedRuntime?: FearGreedRuntime | null,
  etfFlowRuntime?: EtfFlowRuntime | null,
  selectedEtfAsset: EtfAsset = 'BTC',
  footprintRuntime?: FuturesFootprintRuntime | null,
  selectedFootprintAsset: FootprintAsset = 'BTC',
  longShortRatioRuntime?: LongShortRatioRuntime | null,
  selectedLongShortAsset: LongShortAsset = 'BTC',
  grayscaleHoldingsRuntime?: GrayscaleHoldingsRuntime | null,
  coinUnlockPanelRuntime?: CoinUnlockPanelRuntime | null,
  selectedCoinUnlockSymbol: string = DEFAULT_COIN_UNLOCK_SYMBOL,
  onSelectCoinUnlockSymbol?: (symbol: string) => void,
  exchangeAssetPanelRuntime?: ExchangeAssetPanelRuntime | null,
  hyperliquidPanelRuntime?: HyperliquidPanelRuntime | null,
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

  const etfAssetMeta = resolveEtfAssetMeta(etfFlowRuntime?.asset ?? selectedEtfAsset);
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
    ? `${etfAssetMeta.label} 实时：${etfLatestFlowYi >= 0 ? '+' : ''}${etfLatestFlowYi.toFixed(2)} 亿 USD / 日${etfFlowRuntime.stale ? ' · 过期缓存' : ''}`
    : `${etfAssetMeta.label} 样例：+1.20 亿 USD / 日`;
  const etfSourceText = etfFlowRuntime?.sourceTs
    ? `数据时间：${new Date(etfFlowRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const etfNote = etfFlowRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${etfSourceText}`
    : `${etfAssetMeta.displayName} 现货 ETF 持续净流入通常代表传统资金加仓，净流出则可能对应情绪降温或获利了结。${etfSourceText}`;
  const etfBarData = etfBarValues.map((value) => ({
    value,
    itemStyle: {
      color: value >= 0 ? palette.colorSuccess : palette.colorDanger,
    },
  }));

  const longShortAssetMeta = resolveLongShortAssetMeta(longShortRatioRuntime?.asset ?? selectedLongShortAsset);
  const longShortSeriesFallback = [
    { timeLabel: '08:00', ratio: 1.21, longPercent: 54.77, shortPercent: 45.23 },
    { timeLabel: '09:00', ratio: 1.24, longPercent: 55.36, shortPercent: 44.64 },
    { timeLabel: '10:00', ratio: 1.28, longPercent: 56.13, shortPercent: 43.87 },
    { timeLabel: '11:00', ratio: 1.31, longPercent: 56.72, shortPercent: 43.28 },
    { timeLabel: '12:00', ratio: 1.35, longPercent: 57.45, shortPercent: 42.55 },
    { timeLabel: '13:00', ratio: 1.33, longPercent: 57.03, shortPercent: 42.97 },
    { timeLabel: '14:00', ratio: 1.29, longPercent: 56.27, shortPercent: 43.73 },
  ];
  const longShortSeries = longShortRatioRuntime?.series && longShortRatioRuntime.series.length > 0
    ? longShortRatioRuntime.series.slice(-LONG_SHORT_VISIBLE_POINT_LIMIT)
    : null;
  const longShortXAxis = longShortSeries
    ? longShortSeries.map((item) => new Date(item.ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false }))
    : longShortSeriesFallback.map((item) => item.timeLabel);
  const longShortLineData = longShortSeries
    ? longShortSeries.map((item) => ({
      value: Number(item.ratio.toFixed(4)),
      longPercent: item.longPercent,
      shortPercent: item.shortPercent,
      ts: item.ts,
    }))
    : longShortSeriesFallback.map((item) => ({
      value: item.ratio,
      longPercent: item.longPercent,
      shortPercent: item.shortPercent,
      timeLabel: item.timeLabel,
    }));
  const longShortLatestRatio = longShortRatioRuntime?.latestRatio ?? longShortSeriesFallback[longShortSeriesFallback.length - 1].ratio;
  const longShortLatestLongPercent = longShortRatioRuntime?.latestLongPercent ?? longShortSeriesFallback[longShortSeriesFallback.length - 1].longPercent;
  const longShortLatestShortPercent = longShortRatioRuntime?.latestShortPercent ?? longShortSeriesFallback[longShortSeriesFallback.length - 1].shortPercent;
  const longShortRatioValues = longShortLineData
    .map((item) => Number(item.value))
    .filter((value) => Number.isFinite(value));
  const longShortAxisMinRaw = longShortRatioValues.length > 0
    ? Math.min(...longShortRatioValues, 1)
    : 1;
  const longShortAxisMaxRaw = longShortRatioValues.length > 0
    ? Math.max(...longShortRatioValues, 1)
    : 1.4;
  const longShortAxisMin = Math.max(0, Math.floor((longShortAxisMinRaw - 0.08) * 20) / 20);
  const longShortAxisMax = Math.ceil((longShortAxisMaxRaw + 0.08) * 20) / 20;
  const longShortSample = longShortRatioRuntime
    ? `${longShortAssetMeta.label} 实时：${formatRatioValue(longShortLatestRatio)} · 多 ${formatPercentValue(longShortLatestLongPercent)} / 空 ${formatPercentValue(longShortLatestShortPercent)}${longShortRatioRuntime.stale ? ' · 过期缓存' : ''}`
    : `${longShortAssetMeta.label} 样例：${formatRatioValue(longShortLatestRatio)} · 多 ${formatPercentValue(longShortLatestLongPercent)} / 空 ${formatPercentValue(longShortLatestShortPercent)}`;
  const longShortSourceText = longShortRatioRuntime?.sourceTs
    ? `数据时间：${new Date(longShortRatioRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const longShortNote = longShortRatioRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${longShortSourceText}`
    : `当前按 Binance 的 ${longShortAssetMeta.label}USDT 合约大户账户数多空比输出，固定展示 15 分钟级别数据。${longShortSourceText}`;

  const footprintAssetMeta = resolveFootprintAssetMeta(footprintRuntime?.asset ?? selectedFootprintAsset);
  const footprintSeries = footprintRuntime?.series && footprintRuntime.series.length > 0
    ? footprintRuntime.series.slice(-FOOTPRINT_VISIBLE_CANDLE_LIMIT)
    : null;
  const footprintXAxis = footprintSeries
    ? footprintSeries.map((item) => new Date(item.ts).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false }))
    : FOOTPRINT_FALLBACK_SERIES.map((item) => item.timeLabel);
  const footprintDeltaData = footprintSeries
    ? footprintSeries.map((item) => ({
      value: item.netDeltaUsd,
      ts: item.ts,
      buyUsd: item.buyUsd,
      sellUsd: item.sellUsd,
      totalTradeCount: item.totalTradeCount,
      priceLow: item.priceLow,
      priceHigh: item.priceHigh,
    }))
    : FOOTPRINT_FALLBACK_SERIES.map((item) => ({
      value: item.netDeltaUsd,
      timeLabel: item.timeLabel,
      buyUsd: item.buyUsd,
      sellUsd: item.sellUsd,
      totalTradeCount: item.totalTradeCount,
      priceLow: item.priceLow,
      priceHigh: item.priceHigh,
    }));
  const footprintLatestNetDeltaUsd = footprintRuntime?.latestNetDeltaUsd ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].netDeltaUsd;
  const footprintLatestBuyUsd = footprintRuntime?.latestBuyUsd ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].buyUsd;
  const footprintLatestSellUsd = footprintRuntime?.latestSellUsd ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].sellUsd;
  const footprintLatestTradeCount = footprintRuntime?.latestTotalTradeCount ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].totalTradeCount;
  const footprintLatestPriceLow = footprintRuntime?.latestPriceLow ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].priceLow;
  const footprintLatestPriceHigh = footprintRuntime?.latestPriceHigh ?? FOOTPRINT_FALLBACK_SERIES[FOOTPRINT_FALLBACK_SERIES.length - 1].priceHigh;
  const footprintLatestBins = footprintRuntime?.latestBins && footprintRuntime.latestBins.length > 0
    ? [...footprintRuntime.latestBins].sort((left, right) => right.priceFrom - left.priceFrom)
    : [...FOOTPRINT_FALLBACK_BINS].sort((left, right) => right.priceFrom - left.priceFrom);
  const footprintBinLabels = footprintLatestBins.map((item) => `${formatPriceAxisValue(item.priceFrom)}-${formatPriceAxisValue(item.priceTo)}`);
  const footprintBuyBinData = footprintLatestBins.map((item) => ({
    value: item.buyUsd,
    priceFrom: item.priceFrom,
    priceTo: item.priceTo,
    buyUsd: item.buyUsd,
    sellUsd: item.sellUsd,
    deltaUsd: item.deltaUsd,
    buyTradeCount: item.buyTradeCount,
    sellTradeCount: item.sellTradeCount,
  }));
  const footprintSellBinData = footprintLatestBins.map((item) => ({
    value: -item.sellUsd,
    priceFrom: item.priceFrom,
    priceTo: item.priceTo,
    buyUsd: item.buyUsd,
    sellUsd: item.sellUsd,
    deltaUsd: item.deltaUsd,
    buyTradeCount: item.buyTradeCount,
    sellTradeCount: item.sellTradeCount,
  }));
  const footprintBinAxisMax = Math.max(
    1,
    ...footprintLatestBins.flatMap((item) => [item.buyUsd, item.sellUsd]),
  );
  const footprintDominantBuyBin = footprintLatestBins.reduce<FuturesFootprintBin | null>(
    (current, item) => (!current || item.buyUsd > current.buyUsd ? item : current),
    null,
  );
  const footprintDominantSellBin = footprintLatestBins.reduce<FuturesFootprintBin | null>(
    (current, item) => (!current || item.sellUsd > current.sellUsd ? item : current),
    null,
  );
  const footprintSample = footprintRuntime
    ? `${footprintAssetMeta.label} 实时：净差 ${footprintLatestNetDeltaUsd >= 0 ? '+' : ''}${formatCompactUsd(footprintLatestNetDeltaUsd)} · 买 ${formatCompactUsd(footprintLatestBuyUsd)} / 卖 ${formatCompactUsd(footprintLatestSellUsd)}${footprintRuntime.stale ? ' · 过期缓存' : ''}`
    : `${footprintAssetMeta.label} 样例：净差 +${formatCompactUsd(footprintLatestNetDeltaUsd)} · 成交 ${footprintLatestTradeCount.toLocaleString('zh-CN')} 笔`;
  const footprintSourceText = footprintRuntime?.sourceTs
    ? `数据时间：${new Date(footprintRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const footprintRangeText = `最新价格区间：${formatPriceAxisValue(footprintLatestPriceLow)} - ${formatPriceAxisValue(footprintLatestPriceHigh)}。`;
  const footprintDominantText = `最强主动买入桶：${footprintDominantBuyBin ? `${formatPriceAxisValue(footprintDominantBuyBin.priceFrom)}-${formatPriceAxisValue(footprintDominantBuyBin.priceTo)}（${formatCompactUsd(footprintDominantBuyBin.buyUsd)}）` : '--'}；最强主动卖出桶：${footprintDominantSellBin ? `${formatPriceAxisValue(footprintDominantSellBin.priceFrom)}-${formatPriceAxisValue(footprintDominantSellBin.priceTo)}（${formatCompactUsd(footprintDominantSellBin.sellUsd)}）` : '--'}。`;
  const footprintNote = footprintRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${footprintRangeText}${footprintDominantText}${footprintSourceText}`
    : `上半区展示最近 ${footprintXAxis.length} 根 15 分钟 K 线的净主动买卖额，下半区展示最新一根 K 线的价格桶主动买卖分布。${footprintRangeText}${footprintDominantText}${footprintSourceText}`;

  const grayscaleItems = grayscaleHoldingsRuntime?.items && grayscaleHoldingsRuntime.items.length > 0
    ? grayscaleHoldingsRuntime.items.slice(0, 8)
    : GRAYSCALE_HOLDINGS_FALLBACK_ITEMS;
  const grayscaleTotalHoldingsUsd = grayscaleHoldingsRuntime?.totalHoldingsUsd
    ?? grayscaleItems.reduce((sum, item) => sum + item.holdingsUsd, 0);
  const grayscaleAssetCount = grayscaleHoldingsRuntime?.assetCount ?? grayscaleItems.length;
  const grayscaleTopHolding = grayscaleItems[0] ?? null;
  const grayscaleSample = grayscaleHoldingsRuntime
    ? `实时：${grayscaleAssetCount} 个资产 · 总持仓 ${formatCompactUsd(grayscaleTotalHoldingsUsd)} · 最大仓位 ${grayscaleTopHolding?.symbol ?? '--'} ${formatCompactUsd(grayscaleTopHolding?.holdingsUsd ?? 0)}${grayscaleHoldingsRuntime.stale ? ' · 过期缓存' : ''}`
    : '样例：灰度资产持仓、市值、溢价与近 1/7/30 天持仓变化';
  const grayscaleSourceText = grayscaleHoldingsRuntime?.sourceTs
    ? `数据时间：${new Date(grayscaleHoldingsRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}`
    : '数据时间：样例';
  const grayscaleNote = grayscaleHoldingsRuntime?.stale
    ? `当前返回的是缓存快照，系统已在后台自动刷新。${grayscaleSourceText}`
    : `展示灰度旗下资产的当前持仓市值、溢价率以及近 1 / 7 / 30 天持仓数量变化。${grayscaleSourceText}`;

  const coinUnlockListRuntime = coinUnlockPanelRuntime?.list ?? null;
  const coinVestingRuntime = coinUnlockPanelRuntime?.vesting ?? null;
  const coinUnlockItems = coinUnlockListRuntime?.items && coinUnlockListRuntime.items.length > 0
    ? coinUnlockListRuntime.items.slice(0, 12)
    : COIN_UNLOCK_LIST_FALLBACK_ITEMS;
  const fallbackCoinUnlockSymbol = coinUnlockItems[0]?.symbol ?? DEFAULT_COIN_UNLOCK_SYMBOL;
  const activeCoinUnlockSymbol = normalizeCoinUnlockSymbol(
    coinVestingRuntime?.symbol ?? selectedCoinUnlockSymbol ?? fallbackCoinUnlockSymbol,
  ) ?? fallbackCoinUnlockSymbol;
  const fallbackCoinVestingRuntime = COIN_VESTING_FALLBACK_RUNTIME_MAP[activeCoinUnlockSymbol]
    ?? COIN_VESTING_FALLBACK_RUNTIME_MAP[DEFAULT_COIN_UNLOCK_SYMBOL];
  const activeCoinVestingRuntime = coinVestingRuntime ?? fallbackCoinVestingRuntime;
  const coinUnlockSample = coinUnlockListRuntime
    ? `实时：${coinUnlockListRuntime.totalCoinCount} 个币种 · 近期解锁 ${formatCompactUsd(coinUnlockListRuntime.totalNextUnlockValue)} · 当前查看 ${activeCoinUnlockSymbol}${coinUnlockListRuntime.stale || coinVestingRuntime?.stale ? ' · 过期缓存' : ''}`
    : '样例：代币解锁列表 + 单币锁仓详情 + 未来解锁计划';
  const coinUnlockSourceParts = [
    coinUnlockListRuntime?.sourceTs ? `列表 ${formatDateTimeValue(coinUnlockListRuntime.sourceTs)}` : null,
    coinVestingRuntime?.sourceTs ? `详情 ${formatDateTimeValue(coinVestingRuntime.sourceTs)}` : null,
  ].filter((item): item is string => item !== null);
  const coinUnlockNote = (
    coinUnlockListRuntime?.stale
    || coinVestingRuntime?.stale
  )
    ? `左侧展示解锁列表，右侧展示选中币种的锁仓详情。当前部分视图返回的是缓存快照，系统已在后台自动刷新。${coinUnlockSourceParts.length > 0 ? `数据时间：${coinUnlockSourceParts.join(' · ')}` : ''}`
    : `左侧展示即将解锁的代币列表，右侧展示当前币种的流通/锁仓概况、分配结构和未来解锁计划。${coinUnlockSourceParts.length > 0 ? `数据时间：${coinUnlockSourceParts.join(' · ')}` : ''}`;

  const exchangeAssetsRuntime = exchangeAssetPanelRuntime?.assets ?? null;
  const exchangeBalanceListRuntime = exchangeAssetPanelRuntime?.balanceList ?? null;
  const exchangeBalanceChartRuntime = exchangeAssetPanelRuntime?.balanceChart ?? null;
  const hasExchangeAssetRuntime = Boolean(
    exchangeAssetsRuntime
    || exchangeBalanceListRuntime
    || exchangeBalanceChartRuntime,
  );
  const exchangeAssetSourceParts = [
    exchangeAssetsRuntime?.sourceTs ? `资产 ${new Date(exchangeAssetsRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    exchangeBalanceListRuntime?.sourceTs ? `排行 ${new Date(exchangeBalanceListRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    exchangeBalanceChartRuntime?.sourceTs ? `趋势 ${new Date(exchangeBalanceChartRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
  ].filter((item): item is string => item !== null);
  const exchangeAssetSample = hasExchangeAssetRuntime
    ? `${exchangeAssetsRuntime?.exchangeName ?? 'Binance'} 资产 ${formatCompactUsd(exchangeAssetsRuntime?.totalBalanceUsd ?? 0)} · ${(exchangeBalanceListRuntime?.symbol ?? exchangeBalanceChartRuntime?.symbol ?? 'BTC')} 排行 ${formatHoldingAmount(exchangeBalanceListRuntime?.totalBalance ?? exchangeBalanceChartRuntime?.latestTotalBalance ?? 0)}`
    : '样例：Binance 资产分布 / BTC 余额排行 / BTC 余额趋势';
  const exchangeAssetNote = (
    exchangeAssetsRuntime?.stale
    || exchangeBalanceListRuntime?.stale
    || exchangeBalanceChartRuntime?.stale
  )
    ? `卡片内可切换资产明细、余额排行与余额趋势。当前部分视图返回的是缓存快照，系统已在后台自动刷新。${exchangeAssetSourceParts.length > 0 ? `数据时间：${exchangeAssetSourceParts.join(' · ')}` : ''}`
    : `卡片内可切换资产明细、余额排行与余额趋势。默认展示 Binance 资产分布以及 BTC 在交易所的余额排行/趋势。${exchangeAssetSourceParts.length > 0 ? `数据时间：${exchangeAssetSourceParts.join(' · ')}` : ''}`;

  const hyperliquidWhaleAlertRuntime = hyperliquidPanelRuntime?.whaleAlert ?? null;
  const hyperliquidWhalePositionRuntime = hyperliquidPanelRuntime?.whalePosition ?? null;
  const hyperliquidPositionRuntime = hyperliquidPanelRuntime?.position ?? null;
  const hyperliquidUserPositionRuntime = hyperliquidPanelRuntime?.userPosition ?? null;
  const hyperliquidWalletPositionDistributionRuntime = hyperliquidPanelRuntime?.walletPositionDistribution ?? null;
  const hyperliquidWalletPnlDistributionRuntime = hyperliquidPanelRuntime?.walletPnlDistribution ?? null;
  const hasHyperliquidRuntime = Boolean(
    hyperliquidWhaleAlertRuntime
    || hyperliquidWhalePositionRuntime
    || hyperliquidPositionRuntime
    || hyperliquidUserPositionRuntime
    || hyperliquidWalletPositionDistributionRuntime
    || hyperliquidWalletPnlDistributionRuntime,
  );
  const hyperliquidSourceParts = [
    hyperliquidWhaleAlertRuntime?.sourceTs ? `提醒 ${new Date(hyperliquidWhaleAlertRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    hyperliquidWhalePositionRuntime?.sourceTs ? `鲸鱼 ${new Date(hyperliquidWhalePositionRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    hyperliquidPositionRuntime?.sourceTs ? `榜单 ${new Date(hyperliquidPositionRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    hyperliquidUserPositionRuntime?.sourceTs ? `用户 ${new Date(hyperliquidUserPositionRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    hyperliquidWalletPositionDistributionRuntime?.sourceTs ? `持仓分层 ${new Date(hyperliquidWalletPositionDistributionRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
    hyperliquidWalletPnlDistributionRuntime?.sourceTs ? `盈亏分层 ${new Date(hyperliquidWalletPnlDistributionRuntime.sourceTs).toLocaleString('zh-CN', { hour12: false })}` : null,
  ].filter((item): item is string => item !== null);
  const hyperliquidSample = hasHyperliquidRuntime
    ? `鲸鱼提醒 ${formatCompactUsd(hyperliquidWhaleAlertRuntime?.totalPositionValueUsd ?? 0)} · 鲸鱼持仓 ${formatCompactUsd(hyperliquidWhalePositionRuntime?.totalPositionValueUsd ?? 0)} · ${(hyperliquidPositionRuntime?.symbol ?? 'BTC')} 榜单 ${formatCompactUsd(hyperliquidPositionRuntime?.totalPositionValueUsd ?? 0)}`
    : '样例：鲸鱼提醒 / 鲸鱼持仓 / 持仓排行 / 用户持仓 / 钱包持仓分布 / 钱包盈亏分布';
  const hyperliquidNote = (
    hyperliquidWhaleAlertRuntime?.stale
    || hyperliquidWhalePositionRuntime?.stale
    || hyperliquidPositionRuntime?.stale
    || hyperliquidUserPositionRuntime?.stale
    || hyperliquidWalletPositionDistributionRuntime?.stale
    || hyperliquidWalletPnlDistributionRuntime?.stale
  )
    ? `单卡片内可切换 Hyperliquid 六组指标。当前部分视图返回的是缓存快照，系统已在后台自动刷新。${hyperliquidSourceParts.length > 0 ? `数据时间：${hyperliquidSourceParts.join(' · ')}` : ''}`
    : `单卡片内可切换 Hyperliquid 鲸鱼提醒、鲸鱼持仓、市场持仓排行、用户持仓、钱包持仓分布与钱包盈亏分布。默认展示 BTC 榜单与示例地址持仓。${hyperliquidSourceParts.length > 0 ? `数据时间：${hyperliquidSourceParts.join(' · ')}` : ''}`;

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
      defaultLayout: { cols: 1, rows: 1 },
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
      name: `${etfAssetMeta.displayName}现货 ETF 净流入`,
      category: '资金流向',
      defaultLayout: { cols: 1, rows: 1 },
      sample: etfSample,
      description:
        `统计主流 ${etfAssetMeta.label} 现货 ETF 的申赎资金，正值表示资金净流入，负值表示资金净流出。`,
      note: etfNote,
      controls: {
        type: 'asset-switch',
        indicatorId: 'etf-flow',
        ariaLabel: 'ETF 资产切换',
        activeAsset: etfAssetMeta.asset,
        items: ETF_ASSET_OPTIONS.map((item) => ({
          asset: item.asset,
          label: item.label,
          subtitle: item.subtitle,
        })),
      },
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
      id: 'grayscale-holdings',
      name: '灰度持仓',
      category: '机构持仓',
      defaultLayout: { cols: 1, rows: 2 },
      sample: grayscaleSample,
      description:
        '按资产展示 Grayscale Investments 当前持仓、市值、溢价率与近 1 / 7 / 30 天持仓变化，便于观察其产品篮子调整。',
      note: grayscaleNote,
      chartWrapClassName: 'indicator-module-chart-wrap--grayscale',
      customView: {
        type: 'grayscale-holdings',
        runtime: grayscaleHoldingsRuntime ?? {
          stale: false,
          sourceTs: undefined,
          totalHoldingsUsd: grayscaleTotalHoldingsUsd,
          assetCount: grayscaleAssetCount,
          maxHoldingSymbol: grayscaleTopHolding?.symbol,
          maxHoldingUsd: grayscaleTopHolding?.holdingsUsd,
          items: grayscaleItems,
        },
      },
    },
    {
      id: 'coin-unlock',
      name: '代币解锁',
      category: '代币供给',
      defaultLayout: { cols: 2, rows: 2 },
      sample: coinUnlockSample,
      description:
        '整合 CoinGlass 代币解锁列表与单币详情，便于跟踪大额解锁窗口、流通比例和锁仓分配结构。',
      note: coinUnlockNote,
      chartWrapClassName: 'indicator-module-chart-wrap--coin-unlock',
      customView: {
        type: 'coin-unlock-panel',
        runtime: {
          list: coinUnlockListRuntime ?? null,
          vesting: activeCoinVestingRuntime ?? null,
        },
        selectedSymbol: activeCoinUnlockSymbol,
        onSelectSymbol: onSelectCoinUnlockSymbol,
      },
    },
    {
      id: 'exchange-assets',
      name: '交易所资产',
      category: '储备观察',
      defaultLayout: { cols: 2, rows: 2 },
      sample: exchangeAssetSample,
      description:
        '整合交易所资产明细、币种余额排行与余额趋势，用于观察交易所储备结构、集中度和阶段性变化。',
      note: exchangeAssetNote,
      chartWrapClassName: 'indicator-module-chart-wrap--exchange-assets',
      customView: {
        type: 'exchange-asset-panel',
        runtime: exchangeAssetPanelRuntime ?? null,
        palette,
      },
    },
    {
      id: 'hyperliquid',
      name: 'Hyperliquid',
      category: '链上衍生品',
      defaultLayout: { cols: 2, rows: 2 },
      sample: hyperliquidSample,
      description:
        '整合 Hyperliquid 鲸鱼提醒、鲸鱼持仓、市场持仓排行、指定地址持仓与钱包多空/盈亏分布，用于观察资金层级与仓位结构。',
      note: hyperliquidNote,
      chartWrapClassName: 'indicator-module-chart-wrap--hyperliquid',
      customView: {
        type: 'hyperliquid-panel',
        runtime: hyperliquidPanelRuntime ?? null,
        palette,
      },
    },
    {
      id: 'liquidation-heatmap',
      name: '交易对爆仓热力图（模型1）',
      category: '风险事件',
      defaultLayout: { cols: 2, rows: 2 },
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
      id: 'futures-footprint',
      name: `${footprintAssetMeta.displayName}合约足迹图`,
      category: '合约杠杆',
      defaultLayout: { cols: 2, rows: 2 },
      sample: footprintSample,
      description:
        `统计 ${footprintAssetMeta.label} 在 Binance 合约市场最近若干根 15 分钟 K 线内的主动买卖分布，并拆解最新一根 K 线的价格桶明细。`,
      note: footprintNote,
      chartWrapClassName: 'indicator-module-chart-wrap--footprint',
      controls: {
        type: 'asset-switch',
        indicatorId: 'futures-footprint',
        ariaLabel: '合约足迹图币种切换',
        activeAsset: footprintAssetMeta.asset,
        items: FOOTPRINT_ASSET_OPTIONS.map((item) => ({
          asset: item.asset,
          label: item.label,
          subtitle: item.subtitle,
        })),
      },
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
            const row = params?.data && typeof params.data === 'object'
              ? params.data as Record<string, unknown>
              : null;

            if (seriesName === '净主动买卖额') {
              const ts = typeof row?.ts === 'number'
                ? row.ts
                : null;
              const timeText = ts
                ? new Date(ts).toLocaleString('zh-CN', { hour12: false })
                : (typeof row?.timeLabel === 'string' ? row.timeLabel : '--');
              return [
                `时间：${timeText}`,
                `净差：${formatCompactUsd(toNumber(row?.value) ?? 0)}`,
                `主动买入：${formatCompactUsd(toNumber(row?.buyUsd) ?? 0)}`,
                `主动卖出：${formatCompactUsd(toNumber(row?.sellUsd) ?? 0)}`,
                `成交笔数：${Math.round(toNumber(row?.totalTradeCount) ?? 0).toLocaleString('zh-CN')}`,
                `价格区间：${formatPriceAxisValue(toNumber(row?.priceLow) ?? 0)} - ${formatPriceAxisValue(toNumber(row?.priceHigh) ?? 0)}`,
              ].join('<br/>');
            }

            const priceFrom = toNumber(row?.priceFrom);
            const priceTo = toNumber(row?.priceTo);
            return [
              `价格桶：${formatPriceAxisValue(priceFrom ?? 0)} - ${formatPriceAxisValue(priceTo ?? 0)}`,
              `主动买入：${formatCompactUsd(toNumber(row?.buyUsd) ?? 0)}`,
              `主动卖出：${formatCompactUsd(toNumber(row?.sellUsd) ?? 0)}`,
              `净差：${formatCompactUsd(toNumber(row?.deltaUsd) ?? 0)}`,
              `成交笔数：${Math.round((toNumber(row?.buyTradeCount) ?? 0) + (toNumber(row?.sellTradeCount) ?? 0)).toLocaleString('zh-CN')}`,
            ].join('<br/>');
          },
        },
        grid: [
          { left: 52, right: 16, top: 18, height: '38%' },
          { left: 88, right: 16, top: '60%', bottom: 24 },
        ],
        xAxis: [
          {
            type: 'category',
            gridIndex: 0,
            data: footprintXAxis,
            ...categoryAxisStyle,
          },
          {
            type: 'value',
            gridIndex: 1,
            min: -footprintBinAxisMax,
            max: footprintBinAxisMax,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 9,
              formatter: (value: number) => {
                const abs = Math.abs(value);
                if (abs >= 100_000_000) {
                  return `${(abs / 100_000_000).toFixed(1)}亿`;
                }
                if (abs >= 10_000) {
                  return `${(abs / 10_000).toFixed(0)}万`;
                }
                return abs.toFixed(0);
              },
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitLine: { lineStyle: { color: palette.splitLine } },
          },
        ],
        yAxis: [
          {
            type: 'value',
            gridIndex: 0,
            name: '净差',
            nameTextStyle: { color: palette.textSecondary },
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 10,
              formatter: (value: number) => {
                if (Math.abs(value) >= 100_000_000) {
                  return `${(value / 100_000_000).toFixed(1)}亿`;
                }
                if (Math.abs(value) >= 10_000) {
                  return `${(value / 10_000).toFixed(0)}万`;
                }
                return value.toFixed(0);
              },
            },
            splitLine: { lineStyle: { color: palette.splitLine } },
          },
          {
            type: 'category',
            gridIndex: 1,
            data: footprintBinLabels,
            axisLabel: {
              color: palette.textSecondary,
              fontSize: 9,
              interval: footprintBinLabels.length > 18 ? 1 : 0,
            },
            axisLine: { lineStyle: { color: palette.axisLine } },
            axisTick: { show: false },
            splitLine: { show: false },
          },
        ],
        series: [
          {
            name: '净主动买卖额',
            type: 'bar',
            xAxisIndex: 0,
            yAxisIndex: 0,
            barWidth: '62%',
            markLine: {
              symbol: 'none',
              silent: true,
              lineStyle: {
                width: 1,
                type: 'dashed',
                color: palette.axisLine,
              },
              data: [{ yAxis: 0 }],
            },
            data: footprintDeltaData.map((item) => ({
              ...item,
              itemStyle: {
                color: item.value >= 0 ? palette.colorSuccess : palette.colorDanger,
              },
            })),
          },
          {
            name: '主动买入额',
            type: 'bar',
            xAxisIndex: 1,
            yAxisIndex: 1,
            barWidth: 10,
            itemStyle: {
              color: 'rgba(34, 197, 94, 0.78)',
            },
            data: footprintBuyBinData,
          },
          {
            name: '主动卖出额',
            type: 'bar',
            xAxisIndex: 1,
            yAxisIndex: 1,
            barWidth: 10,
            itemStyle: {
              color: 'rgba(239, 68, 68, 0.74)',
            },
            data: footprintSellBinData,
          },
        ],
      },
    },
    {
      id: 'long-short',
      name: `${longShortAssetMeta.displayName}大户账户数多空比`,
      category: '合约杠杆',
      defaultLayout: { cols: 1, rows: 1 },
      sample: longShortSample,
      description:
        `统计 ${longShortAssetMeta.label} 在 Binance 合约市场的大户账户多单占比、空单占比与多空比，目前固定展示 15 分钟级别。`,
      note: longShortNote,
      controls: {
        type: 'asset-switch',
        indicatorId: 'long-short',
        ariaLabel: '多空比币种切换',
        activeAsset: longShortAssetMeta.asset,
        items: LONG_SHORT_ASSET_OPTIONS.map((item) => ({
          asset: item.asset,
          label: item.label,
          subtitle: item.subtitle,
        })),
      },
      chartOption: {
        tooltip: {
          trigger: 'axis',
          backgroundColor: palette.tooltipBg,
          borderColor: palette.tooltipBorder,
          borderWidth: 1,
          textStyle: { color: palette.textPrimary },
          formatter: (params: any) => {
            const first = Array.isArray(params) ? params[0] : params;
            const point = first?.data && typeof first.data === 'object'
              ? first.data as {
                value?: number;
                longPercent?: number;
                shortPercent?: number;
                ts?: number;
                timeLabel?: string;
              }
              : null;
            const timeText = point?.ts
              ? new Date(point.ts).toLocaleString('zh-CN', { hour12: false })
              : point?.timeLabel
                ?? '--';
            const ratioValue = point?.value ?? first?.value;
            return [
              `时间：${timeText}`,
              `多空比：${formatRatioValue(Number(ratioValue))}`,
              `多单占比：${formatPercentValue(Number(point?.longPercent ?? NaN))}`,
              `空单占比：${formatPercentValue(Number(point?.shortPercent ?? NaN))}`,
            ].join('<br/>');
          },
        },
        grid: { left: 42, right: 12, top: 28, bottom: 24 },
        xAxis: {
          type: 'category',
          data: longShortXAxis,
          ...categoryAxisStyle,
        },
        yAxis: {
          type: 'value',
          min: longShortAxisMin,
          max: longShortAxisMax,
          name: 'Ratio',
          nameTextStyle: { color: palette.textSecondary },
          ...valueAxisStyle,
        },
        series: [
          {
            type: 'line',
            smooth: true,
            showSymbol: false,
            lineStyle: { width: 2, color: palette.colorTertiary },
            areaStyle: { color: 'rgba(168, 85, 247, 0.12)' },
            itemStyle: { color: palette.colorTertiary },
            data: longShortLineData,
            markLine: {
              symbol: 'none',
              lineStyle: { type: 'dashed', color: palette.colorWarning },
              label: { color: palette.textSecondary, formatter: '多空平衡线' },
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
      defaultLayout: { cols: 1, rows: 1 },
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
      defaultLayout: { cols: 1, rows: 1 },
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
      defaultLayout: { cols: 1, rows: 1 },
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
      defaultLayout: { cols: 1, rows: 1 },
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

const GrayscaleHoldingsPanel: React.FC<{
  runtime?: GrayscaleHoldingsRuntime | null;
}> = ({ runtime }) => {
  const items = runtime?.items ?? [];
  const maxHoldingUsd = items.reduce((max, item) => Math.max(max, item.holdingsUsd), 0);

  return (
    <div className="indicator-module-grayscale-panel">
      <div className="indicator-module-grayscale-summary">
        <div className="indicator-module-grayscale-summary-item">
          <span className="indicator-module-grayscale-summary-label">总持仓</span>
          <strong className="indicator-module-grayscale-summary-value">
            {formatCompactUsd(runtime?.totalHoldingsUsd ?? 0)}
          </strong>
        </div>
        <div className="indicator-module-grayscale-summary-item">
          <span className="indicator-module-grayscale-summary-label">资产数</span>
          <strong className="indicator-module-grayscale-summary-value">
            {runtime?.assetCount ?? items.length}
          </strong>
        </div>
        <div className="indicator-module-grayscale-summary-item">
          <span className="indicator-module-grayscale-summary-label">最大仓位</span>
          <strong className="indicator-module-grayscale-summary-value">
            {runtime?.maxHoldingSymbol ?? items[0]?.symbol ?? '--'}
          </strong>
        </div>
      </div>

      <div className="indicator-module-grayscale-list ui-scrollable">
        {items.map((item) => {
          const widthPercent = maxHoldingUsd > 0
            ? Math.max(8, (item.holdingsUsd / maxHoldingUsd) * 100)
            : 8;
          const premiumClassName = item.premiumRate > 0
            ? 'is-positive'
            : item.premiumRate < 0
              ? 'is-negative'
              : 'is-neutral';

          return (
            <div key={`${item.symbol}-${item.updateTime ?? item.closeTime ?? 0}`} className="indicator-module-grayscale-row">
              <div className="indicator-module-grayscale-row-main">
                <div className="indicator-module-grayscale-row-head">
                  <div className="indicator-module-grayscale-symbol">{item.symbol}</div>
                  <div className="indicator-module-grayscale-holdings">{formatCompactUsd(item.holdingsUsd)}</div>
                  <div className={`indicator-module-grayscale-premium ${premiumClassName}`}>
                    {formatSignedPercent(item.premiumRate)}
                  </div>
                </div>
                <div className="indicator-module-grayscale-bar">
                  <div
                    className="indicator-module-grayscale-bar-fill"
                    style={{ width: `${Math.min(widthPercent, 100)}%` }}
                  />
                </div>
              </div>
              <div className="indicator-module-grayscale-row-meta">
                <span>持仓 {formatHoldingAmount(item.holdingsAmount)}</span>
                <span>一级 {formatUsdValue(item.primaryMarketPrice)}</span>
                <span>二级 {formatUsdValue(item.secondaryMarketPrice)}</span>
              </div>
              <div className="indicator-module-grayscale-row-changes">
                <span>1D {formatSignedHoldingChange(item.holdingsAmountChange1d)}</span>
                <span>7D {formatSignedHoldingChange(item.holdingsAmountChange7d)}</span>
                <span>30D {formatSignedHoldingChange(item.holdingsAmountChange30d)}</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

const CoinUnlockPanel: React.FC<{
  runtime?: CoinUnlockPanelRuntime | null;
  selectedSymbol?: string;
  onSelectSymbol?: (symbol: string) => void;
}> = ({ runtime, selectedSymbol, onSelectSymbol }) => {
  const listRuntime = runtime?.list ?? null;
  const vestingRuntime = runtime?.vesting ?? null;
  const items = listRuntime?.items && listRuntime.items.length > 0
    ? listRuntime.items
    : COIN_UNLOCK_LIST_FALLBACK_ITEMS;
  const activeSymbol = normalizeCoinUnlockSymbol(vestingRuntime?.symbol ?? selectedSymbol ?? items[0]?.symbol)
    ?? DEFAULT_COIN_UNLOCK_SYMBOL;
  const activeItem = items.find((item) => item.symbol === activeSymbol) ?? items[0] ?? null;
  const detailRuntime = (vestingRuntime && vestingRuntime.symbol === activeSymbol
    ? vestingRuntime
    : COIN_VESTING_FALLBACK_RUNTIME_MAP[activeSymbol])
    ?? COIN_VESTING_FALLBACK_RUNTIME_MAP[DEFAULT_COIN_UNLOCK_SYMBOL];
  const allocationItems = detailRuntime?.allocationItems?.slice(0, 6) ?? [];
  const scheduleItems = detailRuntime?.scheduleItems?.slice(0, 6) ?? [];
  const totalCoinCount = listRuntime?.totalCoinCount ?? items.length;
  const totalNextUnlockValue = listRuntime?.totalNextUnlockValue
    ?? items.reduce((sum, item) => sum + (item.nextUnlockValue ?? 0), 0);
  const totalMarketCap = listRuntime?.totalMarketCap
    ?? items.reduce((sum, item) => sum + (item.marketCap ?? 0), 0);
  const nextUnlockSymbol = listRuntime?.nextUnlockSymbol ?? activeItem?.symbol ?? '--';
  const nextUnlockTime = listRuntime?.nextUnlockTime ?? activeItem?.nextUnlockTime;
  const nextUnlockValue = listRuntime?.nextUnlockValue ?? activeItem?.nextUnlockValue;
  const maxNextUnlockValue = items.reduce((max, item) => Math.max(max, item.nextUnlockValue ?? 0), 0);

  return (
    <div className="indicator-module-coin-unlock-panel">
      <div className="indicator-module-coin-unlock-summary">
        <div className="indicator-module-coin-unlock-summary-item">
          <span className="indicator-module-coin-unlock-summary-label">币种数</span>
          <strong className="indicator-module-coin-unlock-summary-value">{formatCountValue(totalCoinCount)}</strong>
        </div>
        <div className="indicator-module-coin-unlock-summary-item">
          <span className="indicator-module-coin-unlock-summary-label">近期解锁总价值</span>
          <strong className="indicator-module-coin-unlock-summary-value">{formatCompactUsd(totalNextUnlockValue)}</strong>
        </div>
        <div className="indicator-module-coin-unlock-summary-item">
          <span className="indicator-module-coin-unlock-summary-label">覆盖市值</span>
          <strong className="indicator-module-coin-unlock-summary-value">{formatCompactUsd(totalMarketCap)}</strong>
        </div>
        <div className="indicator-module-coin-unlock-summary-item">
          <span className="indicator-module-coin-unlock-summary-label">最近解锁</span>
          <strong className="indicator-module-coin-unlock-summary-value">
            {nextUnlockSymbol} · {formatDateTimeValue(nextUnlockTime)}
          </strong>
        </div>
      </div>

      <div className="indicator-module-coin-unlock-body">
        <div className="indicator-module-coin-unlock-list ui-scrollable">
          {items.map((item) => {
            const isActive = item.symbol === activeSymbol;
            const widthPercent = maxNextUnlockValue > 0
              ? Math.max(8, ((item.nextUnlockValue ?? 0) / maxNextUnlockValue) * 100)
              : 8;
            const changeClassName = getSignedStateClassName(item.priceChange24h);

            return (
              <button
                key={`${item.symbol}-${item.updateTime ?? item.nextUnlockTime ?? 0}`}
                type="button"
                className={`indicator-module-coin-unlock-row ${isActive ? 'indicator-module-coin-unlock-row--active' : ''}`}
                onClick={() => onSelectSymbol?.(item.symbol)}
              >
                <div className="indicator-module-coin-unlock-row-head">
                  <div className="indicator-module-coin-unlock-row-symbol">
                    <strong>{item.symbol}</strong>
                    <span>{item.name ?? '未命名代币'}</span>
                  </div>
                  <div className={`indicator-module-coin-unlock-row-change ${changeClassName}`}>
                    {formatSignedPercent(item.priceChange24h ?? 0)}
                  </div>
                </div>
                <div className="indicator-module-coin-unlock-row-bar">
                  <div
                    className="indicator-module-coin-unlock-row-bar-fill"
                    style={{ width: `${Math.min(widthPercent, 100)}%` }}
                  />
                </div>
                <div className="indicator-module-coin-unlock-row-meta">
                  <span>解锁 {formatCompactUsd(item.nextUnlockValue ?? 0)}</span>
                  <span>时间 {formatDateTimeValue(item.nextUnlockTime)}</span>
                </div>
                <div className="indicator-module-coin-unlock-row-meta">
                  <span>已解锁 {formatPercentValue(item.unlockedPercent ?? 0)}</span>
                  <span>市值 {formatCompactUsd(item.marketCap ?? 0)}</span>
                </div>
              </button>
            );
          })}
        </div>

        <div className="indicator-module-coin-unlock-detail">
          <div className="indicator-module-coin-unlock-detail-header">
            <div>
              <div className="indicator-module-coin-unlock-detail-symbol">{detailRuntime?.symbol ?? activeItem?.symbol ?? '--'}</div>
              <div className="indicator-module-coin-unlock-detail-name">{detailRuntime?.name ?? activeItem?.name ?? '代币解锁详情'}</div>
            </div>
            <div className="indicator-module-coin-unlock-detail-next">
              <span>下一次解锁</span>
              <strong>{formatCompactUsd(detailRuntime?.nextUnlockValue ?? nextUnlockValue ?? 0)}</strong>
              <em>{formatDateTimeValue(detailRuntime?.nextUnlockTime ?? nextUnlockTime)}</em>
            </div>
          </div>

          <div className="indicator-module-coin-unlock-stat-grid">
            <div className="indicator-module-coin-unlock-stat-item">
              <span>价格</span>
              <strong>{formatUsdValue(detailRuntime?.price ?? activeItem?.price ?? 0)}</strong>
            </div>
            <div className="indicator-module-coin-unlock-stat-item">
              <span>24H</span>
              <strong className={getSignedStateClassName(detailRuntime?.priceChange24h ?? activeItem?.priceChange24h)}>
                {formatSignedPercent(detailRuntime?.priceChange24h ?? activeItem?.priceChange24h ?? 0)}
              </strong>
            </div>
            <div className="indicator-module-coin-unlock-stat-item">
              <span>市值</span>
              <strong>{formatCompactUsd(detailRuntime?.marketCap ?? activeItem?.marketCap ?? 0)}</strong>
            </div>
            <div className="indicator-module-coin-unlock-stat-item">
              <span>总量</span>
              <strong>{formatCoinAmountValue(detailRuntime?.totalSupply ?? activeItem?.unlockedSupply ?? 0)}</strong>
            </div>
            <div className="indicator-module-coin-unlock-stat-item">
              <span>已解锁</span>
              <strong>{formatPercentValue(detailRuntime?.unlockedPercent ?? activeItem?.unlockedPercent ?? 0)}</strong>
            </div>
            <div className="indicator-module-coin-unlock-stat-item">
              <span>未解锁</span>
              <strong>{formatPercentValue(detailRuntime?.lockedPercent ?? activeItem?.lockedPercent ?? 0)}</strong>
            </div>
          </div>

          <div className="indicator-module-coin-unlock-progress">
            <div className="indicator-module-coin-unlock-progress-bar">
              <div
                className="indicator-module-coin-unlock-progress-bar-fill"
                style={{ width: `${Math.max(6, Math.min(detailRuntime?.unlockedPercent ?? activeItem?.unlockedPercent ?? 0, 100))}%` }}
              />
            </div>
            <div className="indicator-module-coin-unlock-progress-meta">
              <span>流通量 {formatCoinAmountValue(detailRuntime?.circulatingSupply ?? detailRuntime?.unlockedSupply ?? activeItem?.unlockedSupply)}</span>
              <span>锁仓量 {formatCoinAmountValue(detailRuntime?.lockedSupply ?? activeItem?.lockedSupply)}</span>
              <span>下次释放 {formatCoinAmountValue(detailRuntime?.nextUnlockAmount ?? activeItem?.nextUnlockAmount)}</span>
            </div>
          </div>

          <div className="indicator-module-coin-unlock-detail-columns">
            <section className="indicator-module-coin-unlock-section">
              <div className="indicator-module-coin-unlock-section-title">锁仓分配</div>
              <div className="indicator-module-coin-unlock-section-list ui-scrollable">
                {allocationItems.map((item) => (
                  <div key={`${detailRuntime?.symbol ?? activeSymbol}-${item.label}`} className="indicator-module-coin-unlock-section-row">
                    <div className="indicator-module-coin-unlock-section-head">
                      <strong>{item.label}</strong>
                      <span>{formatPercentValue(item.unlockedPercent ?? 0)} / {formatPercentValue(item.lockedPercent ?? 0)}</span>
                    </div>
                    <div className="indicator-module-coin-unlock-section-meta">
                      <span>已解锁 {formatCoinAmountValue(item.unlockedAmount)}</span>
                      <span>未解锁 {formatCoinAmountValue(item.lockedAmount)}</span>
                    </div>
                    <div className="indicator-module-coin-unlock-section-meta">
                      <span>下次时间 {formatDateTimeValue(item.nextUnlockTime)}</span>
                      <span>下次数量 {formatCoinAmountValue(item.nextUnlockAmount)}</span>
                    </div>
                  </div>
                ))}
              </div>
            </section>

            <section className="indicator-module-coin-unlock-section">
              <div className="indicator-module-coin-unlock-section-title">未来解锁计划</div>
              <div className="indicator-module-coin-unlock-section-list ui-scrollable">
                {scheduleItems.map((item, index) => (
                  <div key={`${detailRuntime?.symbol ?? activeSymbol}-${item.label ?? index}`} className="indicator-module-coin-unlock-section-row">
                    <div className="indicator-module-coin-unlock-section-head">
                      <strong>{item.label ?? `计划 ${index + 1}`}</strong>
                      <span>{formatPercentValue(item.unlockPercent ?? 0)}</span>
                    </div>
                    <div className="indicator-module-coin-unlock-section-meta">
                      <span>时间 {formatDateTimeValue(item.unlockTime)}</span>
                      <span>数量 {formatCoinAmountValue(item.unlockAmount)}</span>
                    </div>
                    <div className="indicator-module-coin-unlock-section-meta">
                      <span>估值 {formatCompactUsd(item.unlockValue ?? 0)}</span>
                    </div>
                  </div>
                ))}
              </div>
            </section>
          </div>
        </div>
      </div>
    </div>
  );
};

const ExchangeAssetPanel: React.FC<{
  runtime?: ExchangeAssetPanelRuntime | null;
  palette: ChartPalette;
}> = ({ runtime, palette }) => {
  const [activeView, setActiveView] = useState<ExchangeAssetView>('assets');

  const assetsRuntime = runtime?.assets ?? null;
  const balanceListRuntime = runtime?.balanceList ?? null;
  const balanceChartRuntime = runtime?.balanceChart ?? null;
  const viewState = useMemo(() => {
    const summaryItems: Array<{ label: string; value: string }> = [];
    let option: EChartsOption;

    if (activeView === 'assets') {
      const items = (assetsRuntime?.items && assetsRuntime.items.length > 0
        ? assetsRuntime.items
        : EXCHANGE_ASSET_FALLBACK_ITEMS)
        .slice(0, 8);
      const totalBalanceUsd = assetsRuntime?.totalBalanceUsd
        ?? items.reduce((sum, item) => sum + item.balanceUsd, 0);
      const leader = items[0];
      summaryItems.push(
        { label: '总资产', value: formatCompactUsd(totalBalanceUsd) },
        { label: '资产数', value: String(assetsRuntime?.totalAssetCount ?? items.length) },
        { label: '第一大仓位', value: leader?.symbol ?? '--' },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.symbol}${row.assetsName ? ` · ${row.assetsName}` : ''}`,
              `市值：${formatCompactUsd(row.balanceUsd)}`,
              `数量：${formatHoldingAmount(row.balance)}`,
              `价格：${formatUsdValue(row.price ?? 0)}`,
            ].join('<br/>');
          },
        },
        grid: { left: 24, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '亿美元',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: {
            color: palette.textSecondary,
            formatter: (value: number) => `${value.toFixed(1)}`,
          },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => item.symbol),
          axisLabel: {
            color: palette.textSecondary,
            width: 92,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar' as const,
            barWidth: 12,
            data: items.map((item, index) => ({
              value: Number((item.balanceUsd / 100_000_000).toFixed(2)),
              itemStyle: {
                color: index === 0
                  ? palette.colorPrimary
                  : index === 1
                    ? palette.colorSecondary
                    : 'rgba(59, 130, 246, 0.35)',
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => `${Number(params?.value ?? 0).toFixed(2)}`,
            },
          },
        ],
      };
    } else if (activeView === 'balance-list') {
      const items = (balanceListRuntime?.items && balanceListRuntime.items.length > 0
        ? balanceListRuntime.items
        : EXCHANGE_BALANCE_LIST_FALLBACK_ITEMS)
        .slice(0, 8);
      const totalBalance = balanceListRuntime?.totalBalance
        ?? items.reduce((sum, item) => sum + item.balance, 0);
      const leader = items[0];
      summaryItems.push(
        { label: '总余额', value: formatHoldingAmount(totalBalance) },
        { label: '交易所数', value: String(balanceListRuntime?.totalExchangeCount ?? items.length) },
        { label: '第一名', value: leader?.exchangeName ?? '--' },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.exchangeName}`,
              `余额：${formatHoldingAmount(row.balance)} ${balanceListRuntime?.symbol ?? 'BTC'}`,
              `1D：${formatSignedPercent(row.changePercent1d ?? 0)}`,
              `7D：${formatSignedPercent(row.changePercent7d ?? 0)}`,
              `30D：${formatSignedPercent(row.changePercent30d ?? 0)}`,
            ].join('<br/>');
          },
        },
        grid: { left: 24, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: balanceListRuntime?.symbol ?? 'BTC',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => item.exchangeName),
          axisLabel: {
            color: palette.textSecondary,
            width: 96,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar' as const,
            barWidth: 12,
            data: items.map((item) => ({
              value: Number(item.balance.toFixed(2)),
              itemStyle: {
                color: (item.changePercent1d ?? 0) >= 0 ? palette.colorSuccess : palette.colorDanger,
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => formatHoldingAmount(Number(params?.value ?? 0)),
            },
          },
        ],
      };
    } else {
      const chartRuntime = balanceChartRuntime ?? {
        symbol: 'BTC',
        stale: false,
        sourceTs: EXCHANGE_BALANCE_CHART_FALLBACK_TIMES[EXCHANGE_BALANCE_CHART_FALLBACK_TIMES.length - 1],
        latestTotalBalance: EXCHANGE_BALANCE_CHART_FALLBACK_SERIES.reduce((sum, item) => sum + (item.latestBalance ?? 0), 0),
        totalSeriesCount: EXCHANGE_BALANCE_CHART_FALLBACK_SERIES.length,
        timeList: EXCHANGE_BALANCE_CHART_FALLBACK_TIMES,
        priceList: EXCHANGE_BALANCE_CHART_FALLBACK_PRICES,
        series: EXCHANGE_BALANCE_CHART_FALLBACK_SERIES,
      };
      const lastPrice = chartRuntime.priceList[chartRuntime.priceList.length - 1] ?? 0;
      summaryItems.push(
        { label: '最新总余额', value: formatHoldingAmount(chartRuntime.latestTotalBalance) },
        { label: '展示交易所', value: String(chartRuntime.series.length) },
        { label: '最新价格', value: formatUsdValue(lastPrice) },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const rows = Array.isArray(params) ? params : [params];
            const header = rows[0]?.axisValueLabel ? `时间：${rows[0].axisValueLabel}` : '';
            const lines = rows.map((row) => {
              const value = row?.value;
              if (row?.seriesName === `${chartRuntime.symbol} 价格`) {
                return `${row.seriesName}：${formatUsdValue(Number(value ?? 0))}`;
              }

              return `${row?.seriesName ?? '--'}：${formatHoldingAmount(Number(value ?? 0))}`;
            });
            return [header, ...lines].filter(Boolean).join('<br/>');
          },
        },
        legend: {
          top: 2,
          textStyle: { color: palette.textSecondary, fontSize: 11 },
        },
        grid: { left: 18, right: 18, top: 34, bottom: 18, containLabel: true },
        xAxis: {
          type: 'category',
          data: chartRuntime.timeList.map((item) => new Date(item).toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' })),
          axisLabel: { color: palette.textSecondary, fontSize: 10 },
          axisLine: { lineStyle: { color: palette.axisLine } },
          axisTick: { show: false },
        },
        yAxis: [
          {
            type: 'value',
            name: chartRuntime.symbol,
            nameTextStyle: { color: palette.textSecondary },
            axisLabel: { color: palette.textSecondary },
            splitLine: { lineStyle: { color: palette.splitLine } },
          },
          {
            type: 'value',
            name: 'USD',
            nameTextStyle: { color: palette.textSecondary },
            axisLabel: { color: palette.textSecondary },
            splitLine: { show: false },
          },
        ],
        series: [
          ...chartRuntime.series.map((item, index) => ({
            name: item.exchangeName,
            type: 'line' as const,
            smooth: true,
            showSymbol: false,
            lineStyle: {
              width: index === 0 ? 2.6 : 1.8,
              color: [palette.colorPrimary, palette.colorSecondary, palette.colorTertiary, palette.colorSuccess, palette.colorWarning][index % 5],
            },
            data: item.values,
          })),
          {
            name: `${chartRuntime.symbol} 价格`,
            type: 'line',
            yAxisIndex: 1,
            smooth: true,
            showSymbol: false,
            lineStyle: {
              width: 1.5,
              type: 'dashed',
              color: 'rgba(148, 163, 184, 0.85)',
            },
            data: chartRuntime.priceList,
          },
        ],
      };
    }

    return { summaryItems, option };
  }, [activeView, assetsRuntime, balanceListRuntime, balanceChartRuntime, palette]);

  return (
    <div className="indicator-module-exchange-panel">
      <div className="indicator-module-exchange-tabs" aria-label="交易所资产视图切换">
        {[
          { key: 'assets', label: '资产明细' },
          { key: 'balance-list', label: '余额排行' },
          { key: 'balance-chart', label: '余额趋势' },
        ].map((item) => {
          const isActive = item.key === activeView;
          return (
            <button
              key={item.key}
              type="button"
              className={`indicator-module-exchange-tab ${isActive ? 'indicator-module-exchange-tab--active' : ''}`}
              onClick={() => setActiveView(item.key as ExchangeAssetView)}
              aria-pressed={isActive}
            >
              {item.label}
            </button>
          );
        })}
      </div>
      <div className="indicator-module-exchange-summary">
        {viewState.summaryItems.map((item) => (
          <div key={item.label} className="indicator-module-exchange-summary-item">
            <span className="indicator-module-exchange-summary-label">{item.label}</span>
            <strong className="indicator-module-exchange-summary-value">{item.value}</strong>
          </div>
        ))}
      </div>
      <div className="indicator-module-exchange-chart">
        <IndicatorChart option={viewState.option} />
      </div>
    </div>
  );
};

const HyperliquidPanel: React.FC<{
  runtime?: HyperliquidPanelRuntime | null;
  palette: ChartPalette;
}> = ({ runtime, palette }) => {
  const [activeView, setActiveView] = useState<HyperliquidView>('whale-alert');

  const whaleAlertRuntime = runtime?.whaleAlert ?? null;
  const whalePositionRuntime = runtime?.whalePosition ?? null;
  const positionRuntime = runtime?.position ?? null;
  const userPositionRuntime = runtime?.userPosition ?? null;
  const walletPositionDistributionRuntime = runtime?.walletPositionDistribution ?? null;
  const walletPnlDistributionRuntime = runtime?.walletPnlDistribution ?? null;

  const viewState = useMemo(() => {
    const summaryItems: Array<{ label: string; value: string }> = [];
    let option: EChartsOption;

    if (activeView === 'whale-alert') {
      const items = (whaleAlertRuntime?.items && whaleAlertRuntime.items.length > 0
        ? whaleAlertRuntime.items
        : HYPERLIQUID_WHALE_ALERT_FALLBACK_ITEMS)
        .slice(0, 8);
      const totalPositionValueUsd = whaleAlertRuntime?.totalPositionValueUsd
        ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0);
      const longAlertCount = whaleAlertRuntime?.longAlertCount
        ?? items.filter((item) => item.positionSize > 0).length;
      const shortAlertCount = whaleAlertRuntime?.shortAlertCount
        ?? items.filter((item) => item.positionSize < 0).length;
      const leader = items[0];
      summaryItems.push(
        { label: '提醒总额', value: formatCompactUsd(totalPositionValueUsd) },
        { label: '多 / 空', value: `${formatCountValue(longAlertCount)} / ${formatCountValue(shortAlertCount)}` },
        { label: '最大提醒', value: leader ? `${leader.symbol} ${formatWalletAddress(leader.user)}` : '--' },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.symbol} · ${formatWalletAddress(row.user)}`,
              `方向：${formatDirectionLabel(row.positionSize)}`,
              `仓位市值：${formatCompactUsd(row.positionValueUsd)}`,
              `仓位数量：${formatHoldingAmount(row.positionSize)}`,
              `开仓价：${formatUsdValue(row.entryPrice ?? 0)}`,
              `强平价：${formatUsdValue(row.liqPrice ?? 0)}`,
              `提醒时间：${row.createTime ? new Date(row.createTime).toLocaleString('zh-CN', { hour12: false }) : '--'}`,
            ].join('<br/>');
          },
        },
        grid: { left: 28, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '百万 USD',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => `${item.symbol} · ${formatWalletAddress(item.user)}`),
          axisLabel: {
            color: palette.textSecondary,
            width: 124,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar',
            barWidth: 12,
            data: items.map((item) => ({
              value: Number((item.positionValueUsd / 1_000_000).toFixed(2)),
              itemStyle: {
                color: item.positionSize >= 0 ? palette.colorSuccess : palette.colorDanger,
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => `${Number(params?.value ?? 0).toFixed(2)}`,
            },
          },
        ],
      };
    } else if (activeView === 'whale-position') {
      const items = (whalePositionRuntime?.items && whalePositionRuntime.items.length > 0
        ? whalePositionRuntime.items
        : HYPERLIQUID_WHALE_POSITION_FALLBACK_ITEMS)
        .slice(0, 8);
      const totalPositionValueUsd = whalePositionRuntime?.totalPositionValueUsd
        ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0);
      const longCount = whalePositionRuntime?.longCount
        ?? items.filter((item) => item.positionSize > 0).length;
      const shortCount = whalePositionRuntime?.shortCount
        ?? items.filter((item) => item.positionSize < 0).length;
      const leader = items[0];
      summaryItems.push(
        { label: '总仓位', value: formatCompactUsd(totalPositionValueUsd) },
        { label: '多 / 空', value: `${formatCountValue(longCount)} / ${formatCountValue(shortCount)}` },
        { label: '最大鲸鱼', value: leader ? `${leader.symbol} ${formatWalletAddress(leader.user)}` : '--' },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.symbol} · ${formatWalletAddress(row.user)}`,
              `方向：${formatDirectionLabel(row.positionSize)}`,
              `仓位市值：${formatCompactUsd(row.positionValueUsd)}`,
              `未实现盈亏：${formatCompactUsd(row.unrealizedPnl ?? 0)}`,
              `杠杆：${formatHoldingAmount(row.leverage ?? 0)}x`,
              `保证金：${formatCompactUsd(row.marginBalance ?? 0)}`,
            ].join('<br/>');
          },
        },
        grid: { left: 28, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '百万 USD',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => `${item.symbol} · ${formatWalletAddress(item.user)}`),
          axisLabel: {
            color: palette.textSecondary,
            width: 124,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar',
            barWidth: 12,
            data: items.map((item) => ({
              value: Number((item.positionValueUsd / 1_000_000).toFixed(2)),
              itemStyle: {
                color: item.positionSize >= 0 ? palette.colorPrimary : palette.colorDanger,
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => `${Number(params?.value ?? 0).toFixed(2)}`,
            },
          },
        ],
      };
    } else if (activeView === 'position') {
      const items = (positionRuntime?.items && positionRuntime.items.length > 0
        ? positionRuntime.items
        : HYPERLIQUID_WHALE_POSITION_FALLBACK_ITEMS.filter((item) => item.symbol === 'BTC'))
        .slice(0, 8);
      const totalPositionValueUsd = positionRuntime?.totalPositionValueUsd
        ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0);
      summaryItems.push(
        { label: '币种', value: positionRuntime?.symbol ?? 'BTC' },
        { label: '页码', value: `${formatCountValue(positionRuntime?.currentPage ?? 1)} / ${formatCountValue(positionRuntime?.totalPages ?? 1)}` },
        { label: '总仓位', value: formatCompactUsd(totalPositionValueUsd) },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.symbol} · ${formatWalletAddress(row.user)}`,
              `方向：${formatDirectionLabel(row.positionSize)}`,
              `仓位市值：${formatCompactUsd(row.positionValueUsd)}`,
              `未实现盈亏：${formatCompactUsd(row.unrealizedPnl ?? 0)}`,
              `资金费：${formatCompactUsd(row.fundingFee ?? 0)}`,
              `模式：${row.marginMode ?? '--'}`,
            ].join('<br/>');
          },
        },
        grid: { left: 28, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '百万 USD',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => formatWalletAddress(item.user)),
          axisLabel: {
            color: palette.textSecondary,
            width: 96,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar',
            barWidth: 12,
            data: items.map((item) => ({
              value: Number((item.positionValueUsd / 1_000_000).toFixed(2)),
              itemStyle: {
                color: item.positionSize >= 0 ? palette.colorSecondary : palette.colorWarning,
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => `${Number(params?.value ?? 0).toFixed(2)}`,
            },
          },
        ],
      };
    } else if (activeView === 'user-position') {
      const userRuntime = userPositionRuntime ?? {
        userAddress: HYPERLIQUID_DEFAULT_USER_ADDRESS,
        stale: false,
        sourceTs: Date.now(),
        accountValue: 42_188_568.02,
        withdrawable: 22_804_869.2,
        totalNotionalPosition: 193_836_988.13,
        totalMarginUsed: 12_098_589.21,
        crossMaintenanceMarginUsed: 4_098_053.77,
        assetPositions: HYPERLIQUID_USER_POSITION_FALLBACK_ASSETS,
      };
      const items = (userRuntime.assetPositions.length > 0
        ? userRuntime.assetPositions
        : HYPERLIQUID_USER_POSITION_FALLBACK_ASSETS)
        .slice(0, 8);
      summaryItems.push(
        { label: '账户权益', value: formatCompactUsd(userRuntime.accountValue) },
        { label: '可提金额', value: formatCompactUsd(userRuntime.withdrawable ?? 0) },
        { label: '保证金占用', value: formatCompactUsd(userRuntime.totalMarginUsed ?? 0) },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const row = items[Number(params?.dataIndex ?? 0)];
            if (!row) {
              return '--';
            }

            return [
              `${row.coin} · ${formatDirectionLabel(row.size)}`,
              `仓位市值：${formatCompactUsd(row.positionValue)}`,
              `仓位数量：${formatHoldingAmount(row.size)}`,
              `未实现盈亏：${formatCompactUsd(row.unrealizedPnl ?? 0)}`,
              `杠杆：${formatHoldingAmount(row.leverageValue ?? 0)}x (${row.leverageType ?? '--'})`,
              `ROE：${formatPercentValue((row.returnOnEquity ?? 0) * 100)}`,
            ].join('<br/>');
          },
        },
        grid: { left: 24, right: 18, top: 12, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '百万 USD',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => item.coin),
          axisLabel: { color: palette.textSecondary },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            type: 'bar',
            barWidth: 12,
            data: items.map((item) => ({
              value: Number((Math.abs(item.positionValue) / 1_000_000).toFixed(2)),
              itemStyle: {
                color: (item.unrealizedPnl ?? 0) >= 0 ? palette.colorSuccess : palette.colorDanger,
                borderRadius: [0, 8, 8, 0],
              },
            })),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (params: any) => `${Number(params?.value ?? 0).toFixed(2)}`,
            },
          },
        ],
      };
    } else {
      const distributionRuntime = activeView === 'wallet-position-distribution'
        ? walletPositionDistributionRuntime
        : walletPnlDistributionRuntime;
      const fallbackItems = activeView === 'wallet-position-distribution'
        ? HYPERLIQUID_WALLET_POSITION_DISTRIBUTION_FALLBACK_ITEMS
        : HYPERLIQUID_WALLET_PNL_DISTRIBUTION_FALLBACK_ITEMS;
      const items = (distributionRuntime?.items && distributionRuntime.items.length > 0
        ? distributionRuntime.items
        : fallbackItems)
        .slice(0, 8);
      const totalPositionUsd = distributionRuntime?.totalPositionUsd
        ?? items.reduce((sum, item) => sum + item.positionUsd, 0);
      const totalPositionAddressCount = distributionRuntime?.totalPositionAddressCount
        ?? items.reduce((sum, item) => sum + (item.positionAddressCount ?? 0), 0);
      const strongestBiasItem = items.reduce<HyperliquidWalletDistributionItem | null>(
        (current, item) => (!current || Math.abs(item.biasScore ?? 0) > Math.abs(current.biasScore ?? 0) ? item : current),
        null,
      );
      summaryItems.push(
        { label: '总持仓', value: formatCompactUsd(totalPositionUsd) },
        { label: '活跃地址', value: formatCountValue(totalPositionAddressCount) },
        { label: '最强偏向', value: strongestBiasItem ? `${strongestBiasItem.groupName} ${strongestBiasItem.biasRemark ?? ''}`.trim() : '--' },
      );

      option = {
        tooltip: {
          ...buildTooltip(palette),
          formatter: (params: any) => {
            const dataIndex = Number(params?.dataIndex ?? 0);
            const row = items[dataIndex];
            if (!row) {
              return '--';
            }

            return [
              `${row.groupName}`,
              `多头仓位：${formatCompactUsd(row.longPositionUsd)}`,
              `空头仓位：${formatCompactUsd(row.shortPositionUsd)}`,
              `总持仓：${formatCompactUsd(row.positionUsd)}`,
              `偏向评分：${formatHoldingAmount(row.biasScore ?? 0)}`,
              `持仓地址数：${formatCountValue(row.positionAddressCount ?? 0)}`,
            ].join('<br/>');
          },
        },
        legend: {
          top: 2,
          textStyle: { color: palette.textSecondary, fontSize: 11 },
        },
        grid: { left: 28, right: 18, top: 32, bottom: 12, containLabel: true },
        xAxis: {
          type: 'value',
          name: '百万 USD',
          nameTextStyle: { color: palette.textSecondary },
          axisLabel: { color: palette.textSecondary },
          splitLine: { lineStyle: { color: palette.splitLine } },
        },
        yAxis: {
          type: 'category',
          inverse: true,
          data: items.map((item) => item.groupName),
          axisLabel: {
            color: palette.textSecondary,
            width: 110,
            overflow: 'truncate',
          },
          axisTick: { show: false },
          axisLine: { show: false },
        },
        series: [
          {
            name: '多头仓位',
            type: 'bar',
            stack: 'position',
            barWidth: 12,
            itemStyle: {
              color: 'rgba(34, 197, 94, 0.78)',
              borderRadius: [0, 0, 0, 0],
            },
            data: items.map((item) => Number((item.longPositionUsd / 1_000_000).toFixed(2))),
          },
          {
            name: '空头仓位',
            type: 'bar',
            stack: 'position',
            barWidth: 12,
            itemStyle: {
              color: 'rgba(239, 68, 68, 0.72)',
              borderRadius: [0, 8, 8, 0],
            },
            data: items.map((item) => Number((item.shortPositionUsd / 1_000_000).toFixed(2))),
            label: {
              show: true,
              position: 'right',
              color: palette.textSecondary,
              formatter: (_params: any) => '',
            },
          },
        ],
      };
    }

    return { summaryItems, option };
  }, [
    activeView,
    whaleAlertRuntime,
    whalePositionRuntime,
    positionRuntime,
    userPositionRuntime,
    walletPositionDistributionRuntime,
    walletPnlDistributionRuntime,
    palette,
  ]);

  return (
    <div className="indicator-module-exchange-panel">
      <div className="indicator-module-exchange-tabs" aria-label="Hyperliquid 指标视图切换">
        {[
          { key: 'whale-alert', label: '鲸鱼提醒' },
          { key: 'whale-position', label: '鲸鱼持仓' },
          { key: 'position', label: '持仓排行' },
          { key: 'user-position', label: '用户持仓' },
          { key: 'wallet-position-distribution', label: '钱包持仓分布' },
          { key: 'wallet-pnl-distribution', label: '钱包盈亏分布' },
        ].map((item) => {
          const isActive = item.key === activeView;
          return (
            <button
              key={item.key}
              type="button"
              className={`indicator-module-exchange-tab ${isActive ? 'indicator-module-exchange-tab--active' : ''}`}
              onClick={() => setActiveView(item.key as HyperliquidView)}
              aria-pressed={isActive}
            >
              {item.label}
            </button>
          );
        })}
      </div>
      <div className="indicator-module-exchange-summary">
        {viewState.summaryItems.map((item) => (
          <div key={item.label} className="indicator-module-exchange-summary-item">
            <span className="indicator-module-exchange-summary-label">{item.label}</span>
            <strong className="indicator-module-exchange-summary-value">{item.value}</strong>
          </div>
        ))}
      </div>
      <div className="indicator-module-exchange-chart">
        <IndicatorChart option={viewState.option} />
      </div>
    </div>
  );
};

const IndicatorCard: React.FC<{
  indicator: IndicatorCardData;
  layout: IndicatorCardLayout;
  gridMetrics: IndicatorGridMetrics;
  onLayoutChange?: (indicatorId: string, nextLayout: IndicatorCardLayout) => void;
  onSelectAsset?: (indicatorId: 'etf-flow' | 'futures-footprint' | 'long-short', asset: string) => void;
}> = ({ indicator, layout, gridMetrics, onLayoutChange, onSelectAsset }) => {
  const control = indicator.controls;
  const [isResizing, setIsResizing] = useState(false);

  const handleResizePointerDown = (event: React.PointerEvent<HTMLButtonElement>) => {
    if (!onLayoutChange || event.button !== 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();

    const maxCols = Math.max(1, gridMetrics.columnCount);
    const columnUnit = gridMetrics.columnWidth + gridMetrics.gap;
    const rowUnit = gridMetrics.rowHeight + gridMetrics.gap;
    const startWidth = layout.cols * gridMetrics.columnWidth + Math.max(layout.cols - 1, 0) * gridMetrics.gap;
    const startHeight = layout.rows * gridMetrics.rowHeight + Math.max(layout.rows - 1, 0) * gridMetrics.gap;
    const startX = event.clientX;
    const startY = event.clientY;
    let pendingLayout = layout;
    let frameId: number | null = null;
    const previousUserSelect = document.body.style.userSelect;
    const previousCursor = document.body.style.cursor;

    const flushLayout = () => {
      frameId = null;
      onLayoutChange(indicator.id, pendingLayout);
    };

    const handlePointerMove = (moveEvent: PointerEvent) => {
      const nextWidth = Math.max(gridMetrics.columnWidth, startWidth + (moveEvent.clientX - startX));
      const nextHeight = Math.max(gridMetrics.rowHeight, startHeight + (moveEvent.clientY - startY));
      const nextCols = clampNumber(
        Math.round((nextWidth + gridMetrics.gap) / columnUnit),
        1,
        maxCols,
      );
      const nextRows = clampNumber(
        Math.round((nextHeight + gridMetrics.gap) / rowUnit),
        1,
        INDICATOR_CARD_MAX_ROW_SPAN,
      );

      if (pendingLayout.cols === nextCols && pendingLayout.rows === nextRows) {
        return;
      }

      pendingLayout = {
        cols: nextCols,
        rows: nextRows,
      };

      if (frameId === null) {
        // 拖拽时按帧提交，避免 pointermove 高频触发导致整页重排抖动。
        frameId = window.requestAnimationFrame(flushLayout);
      }
    };

    const cleanup = () => {
      if (frameId !== null) {
        window.cancelAnimationFrame(frameId);
        frameId = null;
      }

      onLayoutChange(indicator.id, pendingLayout);
      document.body.style.userSelect = previousUserSelect;
      document.body.style.cursor = previousCursor;
      setIsResizing(false);
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', cleanup);
      window.removeEventListener('pointercancel', cleanup);
    };

    document.body.style.userSelect = 'none';
    document.body.style.cursor = 'nwse-resize';
    setIsResizing(true);
    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', cleanup);
    window.addEventListener('pointercancel', cleanup);
  };

  return (
    <article
      id={`indicator-card-${indicator.id}`}
      className={`indicator-module-card ${isResizing ? 'indicator-module-card--resizing' : ''}`}
      style={{
        gridColumn: `span ${layout.cols}`,
        gridRow: `span ${layout.rows}`,
      }}
    >
      <header className="indicator-module-card-header">
        <div className="indicator-module-card-headline">
          <h2 className="indicator-module-card-title">{indicator.name}</h2>
          <p className="indicator-module-card-sample">{indicator.sample}</p>
        </div>
        <span className="indicator-module-tag">{indicator.category}</span>
      </header>
      <p className="indicator-module-card-text">{indicator.description}</p>
      {control?.type === 'asset-switch' ? (
        <div className="indicator-module-asset-switcher" aria-label={control.ariaLabel}>
          {control.items.map((item) => {
            const isActive = item.asset === control.activeAsset;
            return (
              <button
                key={item.asset}
                type="button"
                className={`indicator-module-asset-button ${isActive ? 'indicator-module-asset-button--active' : ''}`}
                onClick={() => onSelectAsset?.(control.indicatorId, item.asset)}
                aria-pressed={isActive}
              >
                <span className="indicator-module-asset-button-label">{item.label}</span>
                <span className="indicator-module-asset-button-subtitle">{item.subtitle}</span>
              </button>
            );
          })}
        </div>
      ) : null}
      <p className="indicator-module-card-note">{indicator.note}</p>
      <div className={`indicator-module-chart-wrap ${indicator.chartWrapClassName ?? ''}`}>
        {indicator.customView?.type === 'grayscale-holdings' ? (
          <GrayscaleHoldingsPanel runtime={indicator.customView.runtime} />
        ) : indicator.customView?.type === 'coin-unlock-panel' ? (
          <CoinUnlockPanel
            runtime={indicator.customView.runtime}
            selectedSymbol={indicator.customView.selectedSymbol}
            onSelectSymbol={indicator.customView.onSelectSymbol}
          />
        ) : indicator.customView?.type === 'exchange-asset-panel' ? (
          <ExchangeAssetPanel runtime={indicator.customView.runtime} palette={indicator.customView.palette} />
        ) : indicator.customView?.type === 'hyperliquid-panel' ? (
          <HyperliquidPanel runtime={indicator.customView.runtime} palette={indicator.customView.palette} />
        ) : indicator.chartOption ? (
          <IndicatorChart option={indicator.chartOption} />
        ) : null}
      </div>
      <button
        type="button"
        className="indicator-module-card-resize-handle"
        onPointerDown={handleResizePointerDown}
        aria-label={`调整${indicator.name}卡片大小`}
        title="拖动调整卡片宽高"
      />
    </article>
  );
};

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

const parseEtfFlowRuntime = (latest: IndicatorLatestItem, fallbackAsset: EtfAsset): EtfFlowRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const asset = normalizeEtfAsset(toNonEmptyString(payload.asset)) ?? fallbackAsset;
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
    asset,
    latestNetFlowUsd,
    stale: latest.stale,
    sourceTs,
    series: normalizedSeries,
  };
};

const isGrayscaleHoldingItem = (item: GrayscaleHoldingItem | null): item is GrayscaleHoldingItem => item !== null;

const parseGrayscaleHoldingsRuntime = (latest: IndicatorLatestItem): GrayscaleHoldingsRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];

  const mappedItems: Array<GrayscaleHoldingItem | null> = itemsRaw.map((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const symbol = toNonEmptyString(row.symbol);
      if (!symbol) {
        return null;
      }

      const closeTimeRaw = toNumber(row.closeTime) ?? toNumber(row.close_time);
      const updateTimeRaw = toNumber(row.updateTime) ?? toNumber(row.update_time);
      const parsedItem: GrayscaleHoldingItem = {
        symbol: symbol.toUpperCase(),
        primaryMarketPrice: toNumber(row.primaryMarketPrice) ?? toNumber(row.primary_market_price) ?? 0,
        secondaryMarketPrice: toNumber(row.secondaryMarketPrice) ?? toNumber(row.secondary_market_price) ?? 0,
        premiumRate: toNumber(row.premiumRate) ?? toNumber(row.premium_rate) ?? 0,
        holdingsAmount: toNumber(row.holdingsAmount) ?? toNumber(row.holdings_amount) ?? 0,
        holdingsUsd: toNumber(row.holdingsUsd) ?? toNumber(row.holdings_usd) ?? 0,
        holdingsAmountChange1d: toNumber(row.holdingsAmountChange1d)
          ?? toNumber(row.holdings_amount_change1d)
          ?? toNumber(row.holdings_amount_change_1d)
          ?? 0,
        holdingsAmountChange7d: toNumber(row.holdingsAmountChange7d)
          ?? toNumber(row.holdings_amount_change_7d)
          ?? 0,
        holdingsAmountChange30d: toNumber(row.holdingsAmountChange30d)
          ?? toNumber(row.holdings_amount_change_30d)
          ?? 0,
        closeTime: closeTimeRaw !== null ? normalizeTimestampMs(closeTimeRaw) : undefined,
        updateTime: updateTimeRaw !== null ? normalizeTimestampMs(updateTimeRaw) : undefined,
      };
      return parsedItem;
    });
  const items = mappedItems
    .filter(isGrayscaleHoldingItem)
    .sort((left, right) => right.holdingsUsd - left.holdingsUsd);

  if (items.length === 0) {
    return null;
  }

  const payloadSourceTs = toNumber(payload.sourceTs);
  const totalHoldingsUsd = toNumber(payload.totalHoldingsUsd)
    ?? toNumber(payload.value)
    ?? items.reduce((sum, item) => sum + item.holdingsUsd, 0);
  const assetCount = toNumber(payload.assetCount) ?? items.length;
  const latestItemSourceTs = items
    .map((item) => item.updateTime ?? item.closeTime ?? 0)
    .reduce((max, item) => Math.max(max, item), 0);

  return {
    stale: latest.stale,
    sourceTs: payloadSourceTs ?? (latestItemSourceTs > 0 ? latestItemSourceTs : latest.sourceTs),
    totalHoldingsUsd,
    assetCount,
    maxHoldingSymbol: toNonEmptyString(payload.maxHoldingSymbol) ?? items[0]?.symbol,
    maxHoldingUsd: toNumber(payload.maxHoldingUsd) ?? items[0]?.holdingsUsd,
    items,
  };
};

const parseCoinUnlockListRuntime = (latest: IndicatorLatestItem): CoinUnlockListRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map<CoinUnlockItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const symbol = normalizeCoinUnlockSymbol(row.symbol);
      if (!symbol) {
        return null;
      }

      const nextUnlockTime = toNumber(row.nextUnlockTime) ?? toNumber(row.next_unlock_time);
      const updateTime = toNumber(row.updateTime) ?? toNumber(row.update_time);
      return {
        symbol,
        name: toNonEmptyString(row.name) ?? undefined,
        iconUrl: toNonEmptyString(row.iconUrl) ?? toNonEmptyString(row.icon_url) ?? undefined,
        price: toNumber(row.price) ?? undefined,
        priceChange24h: toNumber(row.priceChange24h) ?? toNumber(row.price_change_24h) ?? undefined,
        marketCap: toNumber(row.marketCap) ?? toNumber(row.market_cap) ?? undefined,
        unlockedSupply: toNumber(row.unlockedSupply) ?? toNumber(row.unlocked_supply) ?? undefined,
        lockedSupply: toNumber(row.lockedSupply) ?? toNumber(row.locked_supply) ?? undefined,
        unlockedPercent: toNumber(row.unlockedPercent) ?? toNumber(row.unlocked_percent) ?? undefined,
        lockedPercent: toNumber(row.lockedPercent) ?? toNumber(row.locked_percent) ?? undefined,
        nextUnlockTime: nextUnlockTime !== null ? normalizeTimestampMs(nextUnlockTime) : undefined,
        nextUnlockAmount: toNumber(row.nextUnlockAmount) ?? toNumber(row.next_unlock_amount) ?? undefined,
        nextUnlockPercent: toNumber(row.nextUnlockPercent) ?? toNumber(row.next_unlock_percent) ?? undefined,
        nextUnlockValue: toNumber(row.nextUnlockValue) ?? toNumber(row.next_unlock_value) ?? undefined,
        updateTime: updateTime !== null ? normalizeTimestampMs(updateTime) : undefined,
      };
    })
    .filter((item): item is CoinUnlockItem => item !== null)
    .sort((left, right) => (right.nextUnlockValue ?? 0) - (left.nextUnlockValue ?? 0));

  if (items.length === 0) {
    return null;
  }

  const itemSourceTs = items
    .map((item) => item.updateTime ?? item.nextUnlockTime ?? 0)
    .reduce((max, item) => Math.max(max, item), 0);
  const payloadNextUnlockTime = toNumber(payload.nextUnlockTime);

  return {
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? (itemSourceTs > 0 ? itemSourceTs : latest.sourceTs),
    totalCoinCount: toNumber(payload.totalCoinCount) ?? items.length,
    totalMarketCap: toNumber(payload.totalMarketCap)
      ?? items.reduce((sum, item) => sum + (item.marketCap ?? 0), 0),
    totalNextUnlockValue: toNumber(payload.totalNextUnlockValue)
      ?? toNumber(payload.value)
      ?? items.reduce((sum, item) => sum + (item.nextUnlockValue ?? 0), 0),
    nextUnlockSymbol: normalizeCoinUnlockSymbol(payload.nextUnlockSymbol) ?? items[0]?.symbol,
    nextUnlockTime: payloadNextUnlockTime !== null
      ? normalizeTimestampMs(payloadNextUnlockTime)
      : items[0]?.nextUnlockTime,
    nextUnlockValue: toNumber(payload.nextUnlockValue) ?? items[0]?.nextUnlockValue,
    items,
  };
};

const parseCoinVestingRuntime = (latest: IndicatorLatestItem, fallbackSymbol: string): CoinVestingRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const dataRoot = payload.data && typeof payload.data === 'object'
    ? payload.data as Record<string, unknown>
    : payload;
  const symbol = normalizeCoinUnlockSymbol(payload.symbol)
    ?? normalizeCoinUnlockSymbol(dataRoot.symbol)
    ?? normalizeCoinUnlockSymbol(fallbackSymbol);
  if (!symbol) {
    return null;
  }

  const allocationItemsRaw = Array.isArray(payload.allocationItems)
    ? payload.allocationItems
    : Array.isArray(dataRoot.allocationItems)
      ? dataRoot.allocationItems
      : [];
  const allocationItems = allocationItemsRaw
    .map<CoinVestingAllocationItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const label = toNonEmptyString(row.label) ?? toNonEmptyString(row.name);
      if (!label) {
        return null;
      }

      const nextUnlockTime = toNumber(row.nextUnlockTime) ?? toNumber(row.next_unlock_time);
      return {
        label,
        unlockedPercent: toNumber(row.unlockedPercent) ?? toNumber(row.unlocked_percent) ?? undefined,
        lockedPercent: toNumber(row.lockedPercent) ?? toNumber(row.locked_percent) ?? undefined,
        unlockedAmount: toNumber(row.unlockedAmount) ?? toNumber(row.unlocked_amount) ?? undefined,
        lockedAmount: toNumber(row.lockedAmount) ?? toNumber(row.locked_amount) ?? undefined,
        nextUnlockTime: nextUnlockTime !== null ? normalizeTimestampMs(nextUnlockTime) : undefined,
        nextUnlockAmount: toNumber(row.nextUnlockAmount) ?? toNumber(row.next_unlock_amount) ?? undefined,
      };
    })
    .filter((item): item is CoinVestingAllocationItem => item !== null);

  const scheduleItemsRaw = Array.isArray(payload.scheduleItems)
    ? payload.scheduleItems
    : Array.isArray(dataRoot.scheduleItems)
      ? dataRoot.scheduleItems
      : [];
  const scheduleItems = scheduleItemsRaw
    .map<CoinVestingScheduleItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const unlockTime = toNumber(row.unlockTime) ?? toNumber(row.unlock_time) ?? toNumber(row.time);
      return {
        label: toNonEmptyString(row.label) ?? toNonEmptyString(row.name) ?? undefined,
        unlockTime: unlockTime !== null ? normalizeTimestampMs(unlockTime) : undefined,
        unlockAmount: toNumber(row.unlockAmount) ?? toNumber(row.unlock_amount) ?? undefined,
        unlockPercent: toNumber(row.unlockPercent) ?? toNumber(row.unlock_percent) ?? undefined,
        unlockValue: toNumber(row.unlockValue) ?? toNumber(row.unlock_value) ?? undefined,
      };
    })
    .filter((item): item is CoinVestingScheduleItem => item !== null)
    .sort((left, right) => (left.unlockTime ?? Number.MAX_SAFE_INTEGER) - (right.unlockTime ?? Number.MAX_SAFE_INTEGER));

  const nextUnlockTime = toNumber(payload.nextUnlockTime) ?? toNumber(dataRoot.nextUnlockTime);

  return {
    symbol,
    name: toNonEmptyString(payload.name) ?? toNonEmptyString(dataRoot.name) ?? undefined,
    iconUrl: toNonEmptyString(payload.iconUrl) ?? toNonEmptyString(dataRoot.iconUrl) ?? undefined,
    price: toNumber(payload.price) ?? toNumber(dataRoot.price) ?? undefined,
    priceChange24h: toNumber(payload.priceChange24h)
      ?? toNumber(payload.price_change_24h)
      ?? toNumber(dataRoot.priceChange24h)
      ?? undefined,
    marketCap: toNumber(payload.marketCap)
      ?? toNumber(payload.market_cap)
      ?? toNumber(dataRoot.marketCap)
      ?? undefined,
    circulatingSupply: toNumber(payload.circulatingSupply)
      ?? toNumber(payload.circulating_supply)
      ?? toNumber(dataRoot.circulatingSupply)
      ?? undefined,
    totalSupply: toNumber(payload.totalSupply)
      ?? toNumber(payload.total_supply)
      ?? toNumber(dataRoot.totalSupply)
      ?? undefined,
    unlockedSupply: toNumber(payload.unlockedSupply)
      ?? toNumber(payload.unlocked_supply)
      ?? toNumber(dataRoot.unlockedSupply)
      ?? undefined,
    lockedSupply: toNumber(payload.lockedSupply)
      ?? toNumber(payload.locked_supply)
      ?? toNumber(dataRoot.lockedSupply)
      ?? undefined,
    unlockedPercent: toNumber(payload.unlockedPercent)
      ?? toNumber(payload.unlocked_percent)
      ?? toNumber(dataRoot.unlockedPercent)
      ?? undefined,
    lockedPercent: toNumber(payload.lockedPercent)
      ?? toNumber(payload.locked_percent)
      ?? toNumber(dataRoot.lockedPercent)
      ?? undefined,
    nextUnlockTime: nextUnlockTime !== null ? normalizeTimestampMs(nextUnlockTime) : scheduleItems[0]?.unlockTime,
    nextUnlockAmount: toNumber(payload.nextUnlockAmount)
      ?? toNumber(payload.next_unlock_amount)
      ?? toNumber(dataRoot.nextUnlockAmount)
      ?? undefined,
    nextUnlockPercent: toNumber(payload.nextUnlockPercent)
      ?? toNumber(payload.next_unlock_percent)
      ?? toNumber(dataRoot.nextUnlockPercent)
      ?? undefined,
    nextUnlockValue: toNumber(payload.nextUnlockValue)
      ?? toNumber(payload.next_unlock_value)
      ?? toNumber(dataRoot.nextUnlockValue)
      ?? undefined,
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs)
      ?? toNumber(dataRoot.sourceTs)
      ?? latest.sourceTs
      ?? scheduleItems.reduce((max, item) => Math.max(max, item.unlockTime ?? 0), 0),
    allocationItems,
    scheduleItems,
  };
};

const parseExchangeAssetsRuntime = (latest: IndicatorLatestItem): ExchangeAssetsRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map<ExchangeAssetItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const symbol = toNonEmptyString(row.symbol);
      const balanceUsd = toNumber(row.balanceUsd) ?? toNumber(row.balance_usd);
      if (!symbol || balanceUsd === null) {
        return null;
      }

      return {
        walletAddress: toNonEmptyString(row.walletAddress) ?? toNonEmptyString(row.wallet_address) ?? undefined,
        symbol: symbol.toUpperCase(),
        assetsName: toNonEmptyString(row.assetsName) ?? toNonEmptyString(row.assets_name) ?? undefined,
        balance: toNumber(row.balance) ?? 0,
        balanceUsd,
        price: toNumber(row.price) ?? undefined,
      };
    })
    .filter((item): item is ExchangeAssetItem => item !== null)
    .sort((left, right) => right.balanceUsd - left.balanceUsd);

  if (items.length === 0) {
    return null;
  }

  return {
    exchangeName: toNonEmptyString(payload.exchangeName) ?? 'Binance',
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? latest.sourceTs,
    totalBalanceUsd: toNumber(payload.totalBalanceUsd) ?? toNumber(payload.value) ?? items.reduce((sum, item) => sum + item.balanceUsd, 0),
    totalAssetCount: toNumber(payload.totalAssetCount) ?? items.length,
    items,
  };
};

const parseExchangeBalanceListRuntime = (latest: IndicatorLatestItem): ExchangeBalanceListRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map<ExchangeBalanceListItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const exchangeName = toNonEmptyString(row.exchangeName) ?? toNonEmptyString(row.exchange_name);
      const balance = toNumber(row.balance);
      if (!exchangeName || balance === null) {
        return null;
      }

      return {
        exchangeName,
        balance,
        change1d: toNumber(row.change1d) ?? toNumber(row.change_1d) ?? undefined,
        changePercent1d: toNumber(row.changePercent1d) ?? toNumber(row.change_percent_1d) ?? undefined,
        change7d: toNumber(row.change7d) ?? toNumber(row.change_7d) ?? undefined,
        changePercent7d: toNumber(row.changePercent7d) ?? toNumber(row.change_percent_7d) ?? undefined,
        change30d: toNumber(row.change30d) ?? toNumber(row.change_30d) ?? undefined,
        changePercent30d: toNumber(row.changePercent30d) ?? toNumber(row.change_percent_30d) ?? undefined,
      };
    })
    .filter((item): item is ExchangeBalanceListItem => item !== null)
    .sort((left, right) => right.balance - left.balance);

  if (items.length === 0) {
    return null;
  }

  return {
    symbol: (toNonEmptyString(payload.symbol) ?? 'BTC').toUpperCase(),
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? latest.sourceTs,
    totalBalance: toNumber(payload.totalBalance) ?? toNumber(payload.value) ?? items.reduce((sum, item) => sum + item.balance, 0),
    totalExchangeCount: toNumber(payload.totalExchangeCount) ?? items.length,
    items,
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

const parseLongShortRatioRuntime = (latest: IndicatorLatestItem, fallbackAsset: LongShortAsset): LongShortRatioRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const asset = normalizeLongShortAsset(
    toNonEmptyString(payload.asset)
      ?? toNonEmptyString(payload.symbol),
  ) ?? fallbackAsset;
  const exchange = toNonEmptyString(payload.exchange) ?? 'Binance';
  const symbol = (toNonEmptyString(payload.symbol) ?? `${asset}USDT`).toUpperCase();
  const interval = toNonEmptyString(payload.interval) ?? '15m';
  const payloadSourceTs = toNumber(payload.sourceTs);

  const seriesRaw = Array.isArray(payload.series)
    ? payload.series
    : [];
  const parsedSeries = seriesRaw
    .map((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const point = item as Record<string, unknown>;
      const ts = toNumber(point.ts) ?? toNumber(point.time) ?? toNumber(point.timestamp);
      const ratio = toNumber(point.latestRatio) ?? toNumber(point.topAccountLongShortRatio) ?? toNumber(point.top_account_long_short_ratio) ?? toNumber(point.value);
      const longPercent = toNumber(point.topAccountLongPercent) ?? toNumber(point.top_account_long_percent);
      const shortPercent = toNumber(point.topAccountShortPercent) ?? toNumber(point.top_account_short_percent);
      if (ts === null || ratio === null || longPercent === null || shortPercent === null) {
        return null;
      }

      return {
        ts: normalizeTimestampMs(ts),
        ratio,
        longPercent,
        shortPercent,
      };
    })
    .filter((item): item is { ts: number; ratio: number; longPercent: number; shortPercent: number } => item !== null)
    .sort((left, right) => left.ts - right.ts);

  const latestPoint = parsedSeries.length > 0 ? parsedSeries[parsedSeries.length - 1] : null;
  const latestRatio = toNumber(payload.latestRatio)
    ?? toNumber(payload.topAccountLongShortRatio)
    ?? toNumber(payload.top_account_long_short_ratio)
    ?? toNumber(payload.value)
    ?? latestPoint?.ratio;
  const latestLongPercent = toNumber(payload.topAccountLongPercent)
    ?? toNumber(payload.top_account_long_percent)
    ?? latestPoint?.longPercent;
  const latestShortPercent = toNumber(payload.topAccountShortPercent)
    ?? toNumber(payload.top_account_short_percent)
    ?? latestPoint?.shortPercent;
  if (latestRatio == null || latestLongPercent == null || latestShortPercent == null) {
    return null;
  }

  const sourceTs = payloadSourceTs
    ? normalizeTimestampMs(payloadSourceTs)
    : latestPoint?.ts
      ?? latest.sourceTs;

  return {
    asset,
    exchange,
    symbol,
    interval,
    latestRatio,
    latestLongPercent,
    latestShortPercent,
    stale: latest.stale,
    sourceTs,
    series: parsedSeries.length > 0
      ? parsedSeries
      : sourceTs
        ? [{
          ts: sourceTs,
          ratio: latestRatio,
          longPercent: latestLongPercent,
          shortPercent: latestShortPercent,
        }]
        : [],
  };
};

const parseFuturesFootprintRuntime = (latest: IndicatorLatestItem, fallbackAsset: FootprintAsset): FuturesFootprintRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const asset = normalizeFootprintAsset(
    toNonEmptyString(payload.asset)
      ?? toNonEmptyString(payload.symbol),
  ) ?? fallbackAsset;
  const exchange = toNonEmptyString(payload.exchange) ?? 'Binance';
  const symbol = (toNonEmptyString(payload.symbol) ?? `${asset}USDT`).toUpperCase();
  const interval = toNonEmptyString(payload.interval) ?? '15m';
  const payloadSourceTs = toNumber(payload.sourceTs);

  const seriesRaw = Array.isArray(payload.series)
    ? payload.series
    : [];
  const parsedSeries = seriesRaw
    .map((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const point = item as Record<string, unknown>;
      const ts = toNumber(point.ts) ?? toNumber(point.time) ?? toNumber(point.timestamp);
      const netDeltaUsd = toNumber(point.latestNetDeltaUsd) ?? toNumber(point.netDeltaUsd) ?? toNumber(point.net_delta_usd) ?? toNumber(point.value);
      const buyUsd = toNumber(point.latestBuyUsd) ?? toNumber(point.buyUsd) ?? toNumber(point.buy_usd);
      const sellUsd = toNumber(point.latestSellUsd) ?? toNumber(point.sellUsd) ?? toNumber(point.sell_usd);
      const buyVolume = toNumber(point.latestBuyVolume) ?? toNumber(point.buyVolume) ?? toNumber(point.buy_volume);
      const sellVolume = toNumber(point.latestSellVolume) ?? toNumber(point.sellVolume) ?? toNumber(point.sell_volume);
      const totalTradeCount = toNumber(point.latestTotalTradeCount) ?? toNumber(point.totalTradeCount) ?? toNumber(point.total_trade_count);
      const priceLow = toNumber(point.latestPriceLow) ?? toNumber(point.priceLow) ?? toNumber(point.price_low);
      const priceHigh = toNumber(point.latestPriceHigh) ?? toNumber(point.priceHigh) ?? toNumber(point.price_high);
      if (
        ts === null
        || netDeltaUsd === null
        || buyUsd === null
        || sellUsd === null
        || buyVolume === null
        || sellVolume === null
        || totalTradeCount === null
        || priceLow === null
        || priceHigh === null
      ) {
        return null;
      }

      return {
        ts: normalizeTimestampMs(ts),
        netDeltaUsd,
        buyUsd,
        sellUsd,
        buyVolume,
        sellVolume,
        totalTradeCount,
        priceLow,
        priceHigh,
      };
    })
    .filter((item): item is FuturesFootprintRuntime['series'][number] => item !== null)
    .sort((left, right) => left.ts - right.ts);

  const binsRaw = Array.isArray(payload.latestBins)
    ? payload.latestBins
    : Array.isArray(payload.latest_bins)
      ? payload.latest_bins
      : [];
  const latestBins = binsRaw
    .map((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const bin = item as Record<string, unknown>;
      const priceFrom = toNumber(bin.priceFrom) ?? toNumber(bin.price_from);
      const priceTo = toNumber(bin.priceTo) ?? toNumber(bin.price_to);
      const buyVolume = toNumber(bin.buyVolume) ?? toNumber(bin.buy_volume);
      const sellVolume = toNumber(bin.sellVolume) ?? toNumber(bin.sell_volume);
      const buyUsd = toNumber(bin.buyUsd) ?? toNumber(bin.buy_usd);
      const sellUsd = toNumber(bin.sellUsd) ?? toNumber(bin.sell_usd);
      const deltaUsd = toNumber(bin.deltaUsd) ?? toNumber(bin.delta_usd) ?? (buyUsd !== null && sellUsd !== null ? buyUsd - sellUsd : null);
      const buyTradeCount = toNumber(bin.buyTradeCount) ?? toNumber(bin.buy_trade_count);
      const sellTradeCount = toNumber(bin.sellTradeCount) ?? toNumber(bin.sell_trade_count);
      if (
        priceFrom === null
        || priceTo === null
        || buyVolume === null
        || sellVolume === null
        || buyUsd === null
        || sellUsd === null
        || deltaUsd === null
        || buyTradeCount === null
        || sellTradeCount === null
      ) {
        return null;
      }

      return {
        priceFrom,
        priceTo,
        buyVolume,
        sellVolume,
        buyUsd,
        sellUsd,
        deltaUsd,
        buyTradeCount,
        sellTradeCount,
      };
    })
    .filter((item): item is FuturesFootprintBin => item !== null)
    .sort((left, right) => left.priceFrom - right.priceFrom);

  const latestPoint = parsedSeries.length > 0 ? parsedSeries[parsedSeries.length - 1] : null;
  const latestNetDeltaUsd = toNumber(payload.latestNetDeltaUsd)
    ?? toNumber(payload.netDeltaUsd)
    ?? toNumber(payload.net_delta_usd)
    ?? toNumber(payload.value)
    ?? latestPoint?.netDeltaUsd;
  const latestBuyUsd = toNumber(payload.latestBuyUsd) ?? toNumber(payload.buyUsd) ?? toNumber(payload.buy_usd) ?? latestPoint?.buyUsd;
  const latestSellUsd = toNumber(payload.latestSellUsd) ?? toNumber(payload.sellUsd) ?? toNumber(payload.sell_usd) ?? latestPoint?.sellUsd;
  const latestBuyVolume = toNumber(payload.latestBuyVolume) ?? toNumber(payload.buyVolume) ?? toNumber(payload.buy_volume) ?? latestPoint?.buyVolume;
  const latestSellVolume = toNumber(payload.latestSellVolume) ?? toNumber(payload.sellVolume) ?? toNumber(payload.sell_volume) ?? latestPoint?.sellVolume;
  const latestTotalTradeCount = toNumber(payload.latestTotalTradeCount)
    ?? toNumber(payload.totalTradeCount)
    ?? toNumber(payload.total_trade_count)
    ?? latestPoint?.totalTradeCount;
  const latestPriceLow = toNumber(payload.latestPriceLow)
    ?? toNumber(payload.priceLow)
    ?? toNumber(payload.price_low)
    ?? latestPoint?.priceLow
    ?? (latestBins.length > 0 ? Math.min(...latestBins.map((item) => item.priceFrom)) : null);
  const latestPriceHigh = toNumber(payload.latestPriceHigh)
    ?? toNumber(payload.priceHigh)
    ?? toNumber(payload.price_high)
    ?? latestPoint?.priceHigh
    ?? (latestBins.length > 0 ? Math.max(...latestBins.map((item) => item.priceTo)) : null);
  if (
    latestNetDeltaUsd == null
    || latestBuyUsd == null
    || latestSellUsd == null
    || latestBuyVolume == null
    || latestSellVolume == null
    || latestTotalTradeCount == null
    || latestPriceLow == null
    || latestPriceHigh == null
  ) {
    return null;
  }

  const sourceTs = payloadSourceTs
    ? normalizeTimestampMs(payloadSourceTs)
    : latestPoint?.ts
      ?? latest.sourceTs;

  return {
    asset,
    exchange,
    symbol,
    interval,
    latestNetDeltaUsd,
    latestBuyUsd,
    latestSellUsd,
    latestBuyVolume,
    latestSellVolume,
    latestTotalTradeCount,
    latestPriceLow,
    latestPriceHigh,
    stale: latest.stale,
    sourceTs,
    series: parsedSeries,
    latestBins,
  };
};

const normalizeNullableNumberSeries = (values: Array<number | null>, targetLength: number): Array<number | null> => {
  if (targetLength <= 0) {
    return [];
  }

  if (values.length === targetLength) {
    return values;
  }

  if (values.length > targetLength) {
    return values.slice(values.length - targetLength);
  }

  return [
    ...Array.from({ length: targetLength - values.length }, () => null),
    ...values,
  ];
};

const findLastNumericValue = (values: Array<number | null>): number | undefined => {
  for (let index = values.length - 1; index >= 0; index -= 1) {
    const value = values[index];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
  }

  return undefined;
};

const parseExchangeBalanceChartRuntime = (latest: IndicatorLatestItem): ExchangeBalanceChartRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const dataRoot = payload.data && typeof payload.data === 'object'
    ? payload.data as Record<string, unknown>
    : payload;
  const timeList = (Array.isArray(dataRoot.timeList) ? dataRoot.timeList : [])
    .map((item) => toNumber(item))
    .filter((item): item is number => item !== null)
    .map((item) => normalizeTimestampMs(item));
  if (timeList.length === 0) {
    return null;
  }

  const normalizedPriceList = normalizeNullableNumberSeries(
    (Array.isArray(dataRoot.priceList) ? dataRoot.priceList : [])
      .map((item) => (item === null ? null : toNumber(item)))
      .map((item) => (item !== null && Number.isFinite(item) ? item : null)),
    timeList.length,
  );

  const seriesRaw = Array.isArray(dataRoot.series)
    ? dataRoot.series
    : dataRoot.dataMap && typeof dataRoot.dataMap === 'object'
      ? Object.entries(dataRoot.dataMap as Record<string, unknown>).map(([exchangeName, values]) => ({
        exchangeName,
        values,
      }))
      : [];
  const series = seriesRaw
    .map<ExchangeBalanceChartSeries | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const exchangeName = toNonEmptyString(row.exchangeName) ?? toNonEmptyString(row.exchange_name);
      if (!exchangeName) {
        return null;
      }

      const values = normalizeNullableNumberSeries(
        (Array.isArray(row.values) ? row.values : [])
          .map((entry) => (entry === null ? null : toNumber(entry)))
          .map((entry) => (entry !== null && Number.isFinite(entry) ? entry : null)),
        timeList.length,
      );
      const latestBalance = toNumber(row.latestBalance) ?? findLastNumericValue(values);
      if (values.every((value) => value === null) && latestBalance == null) {
        return null;
      }

      return {
        exchangeName,
        latestBalance: latestBalance ?? undefined,
        values,
      };
    })
    .filter((item): item is ExchangeBalanceChartSeries => item !== null)
    .sort((left, right) => (right.latestBalance ?? 0) - (left.latestBalance ?? 0));

  if (series.length === 0) {
    return null;
  }

  return {
    symbol: (toNonEmptyString(payload.symbol) ?? toNonEmptyString(dataRoot.symbol) ?? 'BTC').toUpperCase(),
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? toNumber(dataRoot.sourceTs) ?? latest.sourceTs ?? timeList[timeList.length - 1],
    latestTotalBalance: toNumber(payload.latestTotalBalance)
      ?? toNumber(dataRoot.latestTotalBalance)
      ?? toNumber(payload.value)
      ?? toNumber(dataRoot.value)
      ?? series.reduce((sum, item) => sum + (item.latestBalance ?? 0), 0),
    totalSeriesCount: toNumber(payload.totalSeriesCount) ?? toNumber(dataRoot.totalSeriesCount) ?? series.length,
    timeList,
    priceList: normalizedPriceList,
    series,
  };
};

const parseHyperliquidWhaleAlertRuntime = (latest: IndicatorLatestItem): HyperliquidWhaleAlertRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map<HyperliquidWhaleAlertItem | null>((item) => {
      if (!item || typeof item !== 'object') {
        return null;
      }

      const row = item as Record<string, unknown>;
      const user = toNonEmptyString(row.user);
      const symbol = toNonEmptyString(row.symbol);
      const positionValueUsd = toNumber(row.positionValueUsd) ?? toNumber(row.position_value_usd);
      if (!user || !symbol || positionValueUsd === null) {
        return null;
      }

      const createTime = toNumber(row.createTime) ?? toNumber(row.create_time);
      return {
        user,
        symbol: symbol.toUpperCase(),
        positionSize: toNumber(row.positionSize) ?? toNumber(row.position_size) ?? 0,
        entryPrice: toNumber(row.entryPrice) ?? toNumber(row.entry_price) ?? undefined,
        liqPrice: toNumber(row.liqPrice) ?? toNumber(row.liq_price) ?? undefined,
        positionValueUsd,
        positionAction: toNumber(row.positionAction) ?? toNumber(row.position_action) ?? undefined,
        createTime: createTime !== null ? normalizeTimestampMs(createTime) : undefined,
      };
    })
    .filter((item): item is HyperliquidWhaleAlertItem => item !== null)
    .sort((left, right) => right.positionValueUsd - left.positionValueUsd);

  if (items.length === 0) {
    return null;
  }

  return {
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? latest.sourceTs ?? items[0].createTime,
    totalPositionValueUsd: toNumber(payload.totalPositionValueUsd)
      ?? toNumber(payload.value)
      ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0),
    totalAlertCount: toNumber(payload.totalAlertCount) ?? items.length,
    longAlertCount: toNumber(payload.longAlertCount) ?? items.filter((item) => item.positionSize > 0).length,
    shortAlertCount: toNumber(payload.shortAlertCount) ?? items.filter((item) => item.positionSize < 0).length,
    items,
  };
};

const parseHyperliquidPositionItem = (input: unknown): HyperliquidPositionItem | null => {
  if (!input || typeof input !== 'object') {
    return null;
  }

  const row = input as Record<string, unknown>;
  const user = toNonEmptyString(row.user);
  const symbol = toNonEmptyString(row.symbol);
  const positionValueUsd = toNumber(row.positionValueUsd) ?? toNumber(row.position_value_usd);
  if (!user || !symbol || positionValueUsd === null) {
    return null;
  }

  const createTime = toNumber(row.createTime) ?? toNumber(row.create_time);
  const updateTime = toNumber(row.updateTime) ?? toNumber(row.update_time);
  return {
    user,
    symbol: symbol.toUpperCase(),
    positionSize: toNumber(row.positionSize) ?? toNumber(row.position_size) ?? 0,
    entryPrice: toNumber(row.entryPrice) ?? toNumber(row.entry_price) ?? undefined,
    markPrice: toNumber(row.markPrice) ?? toNumber(row.mark_price) ?? undefined,
    liqPrice: toNumber(row.liqPrice) ?? toNumber(row.liq_price) ?? undefined,
    leverage: toNumber(row.leverage) ?? undefined,
    marginBalance: toNumber(row.marginBalance) ?? toNumber(row.margin_balance) ?? undefined,
    positionValueUsd,
    unrealizedPnl: toNumber(row.unrealizedPnl) ?? toNumber(row.unrealized_pnl) ?? undefined,
    fundingFee: toNumber(row.fundingFee) ?? toNumber(row.funding_fee) ?? undefined,
    marginMode: toNonEmptyString(row.marginMode) ?? toNonEmptyString(row.margin_mode) ?? undefined,
    createTime: createTime !== null ? normalizeTimestampMs(createTime) : undefined,
    updateTime: updateTime !== null ? normalizeTimestampMs(updateTime) : undefined,
  };
};

const parseHyperliquidWhalePositionRuntime = (latest: IndicatorLatestItem): HyperliquidWhalePositionRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map((item) => parseHyperliquidPositionItem(item))
    .filter((item): item is HyperliquidPositionItem => item !== null)
    .sort((left, right) => right.positionValueUsd - left.positionValueUsd);

  if (items.length === 0) {
    return null;
  }

  return {
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs)
      ?? latest.sourceTs
      ?? items.reduce((max, item) => Math.max(max, item.updateTime ?? item.createTime ?? 0), 0),
    totalPositionValueUsd: toNumber(payload.totalPositionValueUsd)
      ?? toNumber(payload.value)
      ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0),
    totalMarginBalance: toNumber(payload.totalMarginBalance)
      ?? items.reduce((sum, item) => sum + (item.marginBalance ?? 0), 0),
    totalPositionCount: toNumber(payload.totalPositionCount) ?? items.length,
    longCount: toNumber(payload.longCount) ?? items.filter((item) => item.positionSize > 0).length,
    shortCount: toNumber(payload.shortCount) ?? items.filter((item) => item.positionSize < 0).length,
    items,
  };
};

const parseHyperliquidPositionRuntime = (latest: IndicatorLatestItem): HyperliquidPositionRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : payload.data && typeof payload.data === 'object' && Array.isArray((payload.data as Record<string, unknown>).list)
      ? (payload.data as Record<string, unknown>).list as unknown[]
      : [];
  const items = itemsRaw
    .map((item) => parseHyperliquidPositionItem(item))
    .filter((item): item is HyperliquidPositionItem => item !== null)
    .sort((left, right) => right.positionValueUsd - left.positionValueUsd);

  if (items.length === 0) {
    return null;
  }

  const dataRoot = payload.data && typeof payload.data === 'object'
    ? payload.data as Record<string, unknown>
    : payload;
  return {
    symbol: (toNonEmptyString(payload.symbol) ?? toNonEmptyString(dataRoot.symbol) ?? items[0].symbol).toUpperCase(),
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs)
      ?? latest.sourceTs
      ?? items.reduce((max, item) => Math.max(max, item.updateTime ?? item.createTime ?? 0), 0),
    totalPages: toNumber(payload.totalPages) ?? toNumber(dataRoot.total_pages) ?? toNumber(dataRoot.totalPages) ?? 1,
    currentPage: toNumber(payload.currentPage) ?? toNumber(dataRoot.current_page) ?? toNumber(dataRoot.currentPage) ?? 1,
    totalPositionValueUsd: toNumber(payload.totalPositionValueUsd)
      ?? toNumber(payload.value)
      ?? items.reduce((sum, item) => sum + item.positionValueUsd, 0),
    totalPositionCount: toNumber(payload.totalPositionCount) ?? items.length,
    longCount: toNumber(payload.longCount) ?? items.filter((item) => item.positionSize > 0).length,
    shortCount: toNumber(payload.shortCount) ?? items.filter((item) => item.positionSize < 0).length,
    items,
  };
};

const parseHyperliquidUserMarginSummary = (input: unknown): HyperliquidUserMarginSummary | undefined => {
  if (!input || typeof input !== 'object') {
    return undefined;
  }

  const row = input as Record<string, unknown>;
  const accountValue = toNumber(row.accountValue) ?? toNumber(row.account_value);
  if (accountValue === null) {
    return undefined;
  }

  return {
    accountValue,
    totalNtlPos: toNumber(row.totalNtlPos) ?? toNumber(row.total_ntl_pos) ?? undefined,
    totalRawUsd: toNumber(row.totalRawUsd) ?? toNumber(row.total_raw_usd) ?? undefined,
    totalMarginUsed: toNumber(row.totalMarginUsed) ?? toNumber(row.total_margin_used) ?? undefined,
  };
};

const parseHyperliquidUserAssetPosition = (input: unknown): HyperliquidUserAssetPosition | null => {
  if (!input || typeof input !== 'object') {
    return null;
  }

  const row = input as Record<string, unknown>;
  const positionRoot = row.position && typeof row.position === 'object'
    ? row.position as Record<string, unknown>
    : row;
  const coin = toNonEmptyString(positionRoot.coin);
  const size = toNumber(positionRoot.size) ?? toNumber(positionRoot.szi);
  const positionValue = toNumber(positionRoot.positionValue) ?? toNumber(positionRoot.position_value);
  if (!coin || size === null || positionValue === null) {
    return null;
  }

  const leverageRoot = positionRoot.leverage && typeof positionRoot.leverage === 'object'
    ? positionRoot.leverage as Record<string, unknown>
    : null;
  const fundingRoot = positionRoot.cumFunding && typeof positionRoot.cumFunding === 'object'
    ? positionRoot.cumFunding as Record<string, unknown>
    : positionRoot.cum_funding && typeof positionRoot.cum_funding === 'object'
      ? positionRoot.cum_funding as Record<string, unknown>
      : null;
  return {
    type: toNonEmptyString(row.type) ?? undefined,
    coin: coin.toUpperCase(),
    size,
    leverageType: leverageRoot ? toNonEmptyString(leverageRoot.type) ?? undefined : undefined,
    leverageValue: leverageRoot ? toNumber(leverageRoot.value) ?? undefined : undefined,
    entryPrice: toNumber(positionRoot.entryPrice) ?? toNumber(positionRoot.entry_px) ?? undefined,
    positionValue,
    unrealizedPnl: toNumber(positionRoot.unrealizedPnl) ?? toNumber(positionRoot.unrealized_pnl) ?? undefined,
    returnOnEquity: toNumber(positionRoot.returnOnEquity) ?? toNumber(positionRoot.return_on_equity) ?? undefined,
    liquidationPrice: toNumber(positionRoot.liquidationPrice) ?? toNumber(positionRoot.liquidation_px) ?? undefined,
    maxLeverage: toNumber(positionRoot.maxLeverage) ?? toNumber(positionRoot.max_leverage) ?? undefined,
    cumFundingAllTime: fundingRoot ? toNumber(fundingRoot.allTime) ?? toNumber(fundingRoot.all_time) ?? undefined : undefined,
    cumFundingSinceOpen: fundingRoot ? toNumber(fundingRoot.sinceOpen) ?? toNumber(fundingRoot.since_open) ?? undefined : undefined,
    cumFundingSinceChange: fundingRoot ? toNumber(fundingRoot.sinceChange) ?? toNumber(fundingRoot.since_change) ?? undefined : undefined,
  };
};

const parseHyperliquidUserPositionRuntime = (latest: IndicatorLatestItem): HyperliquidUserPositionRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const dataRoot = payload.data && typeof payload.data === 'object'
    ? payload.data as Record<string, unknown>
    : payload;
  const marginSummary = parseHyperliquidUserMarginSummary(payload.marginSummary ?? dataRoot.margin_summary ?? dataRoot.marginSummary);
  const crossMarginSummary = parseHyperliquidUserMarginSummary(payload.crossMarginSummary ?? dataRoot.cross_margin_summary ?? dataRoot.crossMarginSummary);
  const accountValue = toNumber(payload.accountValue)
    ?? marginSummary?.accountValue
    ?? crossMarginSummary?.accountValue;
  if (accountValue === null || accountValue === undefined) {
    return null;
  }

  const assetPositionsRaw = Array.isArray(payload.assetPositions)
    ? payload.assetPositions
    : Array.isArray(dataRoot.asset_positions)
      ? dataRoot.asset_positions
      : Array.isArray(dataRoot.assetPositions)
        ? dataRoot.assetPositions
        : [];
  const assetPositions = assetPositionsRaw
    .map((item) => parseHyperliquidUserAssetPosition(item))
    .filter((item): item is HyperliquidUserAssetPosition => item !== null)
    .sort((left, right) => Math.abs(right.positionValue) - Math.abs(left.positionValue));

  return {
    userAddress: toNonEmptyString(payload.userAddress)
      ?? toNonEmptyString(payload.user_address)
      ?? HYPERLIQUID_DEFAULT_USER_ADDRESS,
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? toNumber(dataRoot.update_time) ?? toNumber(dataRoot.updateTime) ?? latest.sourceTs,
    accountValue,
    withdrawable: toNumber(payload.withdrawable) ?? toNumber(dataRoot.withdrawable) ?? undefined,
    totalNotionalPosition: toNumber(payload.totalNotionalPosition)
      ?? marginSummary?.totalNtlPos
      ?? crossMarginSummary?.totalNtlPos,
    totalMarginUsed: toNumber(payload.totalMarginUsed)
      ?? marginSummary?.totalMarginUsed
      ?? crossMarginSummary?.totalMarginUsed,
    crossMaintenanceMarginUsed: toNumber(payload.crossMaintenanceMarginUsed)
      ?? toNumber(dataRoot.cross_maintenance_margin_used)
      ?? undefined,
    marginSummary,
    crossMarginSummary,
    assetPositions,
  };
};

const parseHyperliquidWalletDistributionItem = (input: unknown): HyperliquidWalletDistributionItem | null => {
  if (!input || typeof input !== 'object') {
    return null;
  }

  const row = input as Record<string, unknown>;
  const groupName = toNonEmptyString(row.groupName) ?? toNonEmptyString(row.group_name);
  const positionUsd = toNumber(row.positionUsd) ?? toNumber(row.position_usd);
  if (!groupName || positionUsd === null) {
    return null;
  }

  return {
    groupName,
    allAddressCount: toNumber(row.allAddressCount) ?? toNumber(row.all_address_count) ?? undefined,
    positionAddressCount: toNumber(row.positionAddressCount) ?? toNumber(row.position_address_count) ?? undefined,
    positionAddressPercent: toNumber(row.positionAddressPercent) ?? toNumber(row.position_address_percent) ?? undefined,
    biasScore: toNumber(row.biasScore) ?? toNumber(row.bias_score) ?? undefined,
    biasRemark: toNonEmptyString(row.biasRemark) ?? toNonEmptyString(row.bias_remark) ?? undefined,
    minimumAmount: toNumber(row.minimumAmount) ?? toNumber(row.minimum_amount) ?? undefined,
    maximumAmount: toNumber(row.maximumAmount) ?? toNumber(row.maximum_amount) ?? undefined,
    longPositionUsd: toNumber(row.longPositionUsd) ?? toNumber(row.long_position_usd) ?? 0,
    shortPositionUsd: toNumber(row.shortPositionUsd) ?? toNumber(row.short_position_usd) ?? 0,
    longPositionUsdPercent: toNumber(row.longPositionUsdPercent) ?? toNumber(row.long_position_usd_percent) ?? undefined,
    shortPositionUsdPercent: toNumber(row.shortPositionUsdPercent) ?? toNumber(row.short_position_usd_percent) ?? undefined,
    positionUsd,
    profitAddressCount: toNumber(row.profitAddressCount) ?? toNumber(row.profit_address_count) ?? undefined,
    lossAddressCount: toNumber(row.lossAddressCount) ?? toNumber(row.loss_address_count) ?? undefined,
    profitAddressPercent: toNumber(row.profitAddressPercent) ?? toNumber(row.profit_address_percent) ?? undefined,
    lossAddressPercent: toNumber(row.lossAddressPercent) ?? toNumber(row.loss_address_percent) ?? undefined,
  };
};

const parseHyperliquidWalletDistributionRuntime = (latest: IndicatorLatestItem): HyperliquidWalletDistributionRuntime | null => {
  if (!latest.payload || typeof latest.payload !== 'object') {
    return null;
  }

  const payload = latest.payload as Record<string, unknown>;
  const itemsRaw = Array.isArray(payload.items)
    ? payload.items
    : Array.isArray(payload.data)
      ? payload.data
      : [];
  const items = itemsRaw
    .map((item) => parseHyperliquidWalletDistributionItem(item))
    .filter((item): item is HyperliquidWalletDistributionItem => item !== null)
    .sort((left, right) => right.positionUsd - left.positionUsd);

  if (items.length === 0) {
    return null;
  }

  return {
    stale: latest.stale,
    sourceTs: toNumber(payload.sourceTs) ?? latest.sourceTs,
    totalPositionUsd: toNumber(payload.totalPositionUsd)
      ?? toNumber(payload.value)
      ?? items.reduce((sum, item) => sum + item.positionUsd, 0),
    totalGroupCount: toNumber(payload.totalGroupCount) ?? items.length,
    totalPositionAddressCount: toNumber(payload.totalPositionAddressCount)
      ?? items.reduce((sum, item) => sum + (item.positionAddressCount ?? 0), 0),
    items,
  };
};

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
  const gridRef = useRef<HTMLDivElement | null>(null);
  const [isDarkMode, setIsDarkMode] = useState(() =>
    document.documentElement.classList.contains('dark-theme'),
  );
  const [fearGreedRuntime, setFearGreedRuntime] = useState<FearGreedRuntime | null>(null);
  const [selectedEtfAsset, setSelectedEtfAsset] = useState<EtfAsset>('BTC');
  const [etfFlowRuntimeMap, setEtfFlowRuntimeMap] = useState<Partial<Record<EtfAsset, EtfFlowRuntime>>>({});
  const [selectedFootprintAsset, setSelectedFootprintAsset] = useState<FootprintAsset>('BTC');
  const [footprintRuntimeMap, setFootprintRuntimeMap] = useState<Partial<Record<FootprintAsset, FuturesFootprintRuntime>>>({});
  const [selectedLongShortAsset, setSelectedLongShortAsset] = useState<LongShortAsset>('BTC');
  const [longShortRatioRuntimeMap, setLongShortRatioRuntimeMap] = useState<Partial<Record<LongShortAsset, LongShortRatioRuntime>>>({});
  const [grayscaleHoldingsRuntime, setGrayscaleHoldingsRuntime] = useState<GrayscaleHoldingsRuntime | null>(null);
  const [coinUnlockListRuntime, setCoinUnlockListRuntime] = useState<CoinUnlockListRuntime | null>(null);
  const [selectedCoinUnlockSymbol, setSelectedCoinUnlockSymbol] = useState<string>(DEFAULT_COIN_UNLOCK_SYMBOL);
  const [coinVestingRuntimeMap, setCoinVestingRuntimeMap] = useState<Record<string, CoinVestingRuntime>>({});
  const [exchangeAssetsRuntime, setExchangeAssetsRuntime] = useState<ExchangeAssetsRuntime | null>(null);
  const [exchangeBalanceListRuntime, setExchangeBalanceListRuntime] = useState<ExchangeBalanceListRuntime | null>(null);
  const [exchangeBalanceChartRuntime, setExchangeBalanceChartRuntime] = useState<ExchangeBalanceChartRuntime | null>(null);
  const [hyperliquidWhaleAlertRuntime, setHyperliquidWhaleAlertRuntime] = useState<HyperliquidWhaleAlertRuntime | null>(null);
  const [hyperliquidWhalePositionRuntime, setHyperliquidWhalePositionRuntime] = useState<HyperliquidWhalePositionRuntime | null>(null);
  const [hyperliquidPositionRuntime, setHyperliquidPositionRuntime] = useState<HyperliquidPositionRuntime | null>(null);
  const [hyperliquidUserPositionRuntime, setHyperliquidUserPositionRuntime] = useState<HyperliquidUserPositionRuntime | null>(null);
  const [hyperliquidWalletPositionDistributionRuntime, setHyperliquidWalletPositionDistributionRuntime] = useState<HyperliquidWalletDistributionRuntime | null>(null);
  const [hyperliquidWalletPnlDistributionRuntime, setHyperliquidWalletPnlDistributionRuntime] = useState<HyperliquidWalletDistributionRuntime | null>(null);
  const [liquidationHeatmapRuntime, setLiquidationHeatmapRuntime] = useState<LiquidationHeatmapRuntime | null>(null);
  const [gridMetrics, setGridMetrics] = useState<IndicatorGridMetrics>(INDICATOR_GRID_FALLBACK_METRICS);
  const [indicatorLayouts, setIndicatorLayouts] = useState<IndicatorCardLayoutMap>(() => readStoredIndicatorCardLayouts());

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

  useEffect(() => {
    const gridElement = gridRef.current;
    if (!gridElement) {
      return;
    }

    const updateGridMetrics = () => {
      // 栅格列数随容器宽度自动变化，拖拽尺寸按当前列宽与行高吸附。
      setGridMetrics(buildIndicatorGridMetrics(gridElement.clientWidth));
    };

    updateGridMetrics();
    const observer = new ResizeObserver(updateGridMetrics);
    observer.observe(gridElement);

    return () => {
      observer.disconnect();
    };
  }, []);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    const persistTimer = window.setTimeout(() => {
      try {
        window.localStorage.setItem(INDICATOR_LAYOUT_STORAGE_KEY, JSON.stringify(indicatorLayouts));
      } catch {
        // 本地存储失败时不影响页面交互，直接忽略即可。
      }
    }, 120);

    return () => {
      window.clearTimeout(persistTimer);
    };
  }, [indicatorLayouts]);

  const palette = useMemo(() => createPalette(isDarkMode), [isDarkMode]);
  const activeEtfFlowRuntime = etfFlowRuntimeMap[selectedEtfAsset] ?? null;
  const activeFootprintRuntime = footprintRuntimeMap[selectedFootprintAsset] ?? null;
  const activeLongShortRatioRuntime = longShortRatioRuntimeMap[selectedLongShortAsset] ?? null;
  const activeCoinUnlockSymbol = normalizeCoinUnlockSymbol(selectedCoinUnlockSymbol) ?? DEFAULT_COIN_UNLOCK_SYMBOL;
  const activeCoinVestingRuntime = coinVestingRuntimeMap[activeCoinUnlockSymbol] ?? null;
  const handleIndicatorAssetChange = (indicatorId: 'etf-flow' | 'futures-footprint' | 'long-short', asset: string) => {
    if (indicatorId === 'etf-flow') {
      const normalizedAsset = normalizeEtfAsset(asset);
      if (normalizedAsset) {
        setSelectedEtfAsset(normalizedAsset);
      }
      return;
    }

    if (indicatorId === 'futures-footprint') {
      const normalizedAsset = normalizeFootprintAsset(asset);
      if (normalizedAsset) {
        setSelectedFootprintAsset(normalizedAsset);
      }
      return;
    }

    const normalizedAsset = normalizeLongShortAsset(asset);
    if (normalizedAsset) {
      setSelectedLongShortAsset(normalizedAsset);
    }
  };
  const handleCoinUnlockSymbolChange = (symbol: string) => {
    const normalized = normalizeCoinUnlockSymbol(symbol);
    if (normalized) {
      setSelectedCoinUnlockSymbol(normalized);
    }
  };
  const handleIndicatorLayoutChange = (indicatorId: string, nextLayout: IndicatorCardLayout) => {
    const normalized = normalizeIndicatorCardLayout(nextLayout);
    if (!normalized) {
      return;
    }

    setIndicatorLayouts((previous) => {
      const current = previous[indicatorId];
      if (current?.cols === normalized.cols && current?.rows === normalized.rows) {
        return previous;
      }

      return {
        ...previous,
        [indicatorId]: normalized,
      };
    });
  };
  const indicators = useMemo(
    () => buildIndicators(
      palette,
      fearGreedRuntime,
      activeEtfFlowRuntime,
      selectedEtfAsset,
      activeFootprintRuntime,
      selectedFootprintAsset,
      activeLongShortRatioRuntime,
      selectedLongShortAsset,
      grayscaleHoldingsRuntime,
      {
        list: coinUnlockListRuntime,
        vesting: activeCoinVestingRuntime,
      },
      activeCoinUnlockSymbol,
      handleCoinUnlockSymbolChange,
      {
        assets: exchangeAssetsRuntime,
        balanceList: exchangeBalanceListRuntime,
        balanceChart: exchangeBalanceChartRuntime,
      },
      {
        whaleAlert: hyperliquidWhaleAlertRuntime,
        whalePosition: hyperliquidWhalePositionRuntime,
        position: hyperliquidPositionRuntime,
        userPosition: hyperliquidUserPositionRuntime,
        walletPositionDistribution: hyperliquidWalletPositionDistributionRuntime,
        walletPnlDistribution: hyperliquidWalletPnlDistributionRuntime,
      },
      liquidationHeatmapRuntime,
    ),
    [
      palette,
      fearGreedRuntime,
      activeEtfFlowRuntime,
      selectedEtfAsset,
      activeFootprintRuntime,
      selectedFootprintAsset,
      activeLongShortRatioRuntime,
      selectedLongShortAsset,
      grayscaleHoldingsRuntime,
      coinUnlockListRuntime,
      activeCoinUnlockSymbol,
      activeCoinVestingRuntime,
      handleCoinUnlockSymbolChange,
      exchangeAssetsRuntime,
      exchangeBalanceListRuntime,
      exchangeBalanceChartRuntime,
      hyperliquidWhaleAlertRuntime,
      hyperliquidWhalePositionRuntime,
      hyperliquidPositionRuntime,
      hyperliquidUserPositionRuntime,
      hyperliquidWalletPositionDistributionRuntime,
      hyperliquidWalletPnlDistributionRuntime,
      liquidationHeatmapRuntime,
    ],
  );

  useEffect(() => {
    const nextSymbol = coinUnlockListRuntime?.items[0]?.symbol;
    if (!nextSymbol) {
      return;
    }

    const exists = coinUnlockListRuntime.items.some((item) => item.symbol === activeCoinUnlockSymbol);
    if (!exists) {
      setSelectedCoinUnlockSymbol(nextSymbol);
    }
  }, [coinUnlockListRuntime, activeCoinUnlockSymbol]);

  useEffect(() => {
    let disposed = false;

    const loadIndicatorRuntime = async () => {
      const vestingScopeSymbol = activeCoinUnlockSymbol || DEFAULT_COIN_UNLOCK_SYMBOL;
      const [
        baseResults,
        etfFlowResults,
        footprintResults,
        longShortRatioResults,
      ] = await Promise.all([
        Promise.allSettled([
          getIndicatorLatest('coinglass.fear_greed', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.grayscale_holdings', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.coin_unlock_list', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.coin_vesting', { symbol: vestingScopeSymbol }, { allowStale: true }),
          getIndicatorLatest('coinglass.exchange_assets', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.exchange_balance_list', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.exchange_balance_chart', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_whale_alert', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_whale_position', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_position', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_user_position', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_wallet_position_distribution', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.hyperliquid_wallet_pnl_distribution', undefined, { allowStale: true }),
          getIndicatorLatest('coinglass.liquidation_heatmap_model1', undefined, { allowStale: true }),
        ]),
        Promise.allSettled(
          ETF_ASSET_OPTIONS.map((item) => getIndicatorLatest('coinglass.etf_flow', { asset: item.asset }, { allowStale: true })),
        ),
        Promise.allSettled(
          FOOTPRINT_ASSET_OPTIONS.map((item) => getIndicatorLatest('coinglass.futures_footprint', { asset: item.asset }, { allowStale: true })),
        ),
        Promise.allSettled(
          LONG_SHORT_ASSET_OPTIONS.map((item) => getIndicatorLatest('coinglass.top_long_short_account_ratio', { asset: item.asset }, { allowStale: true })),
        ),
      ]);
      const [
        fearGreedResult,
        grayscaleHoldingsResult,
        coinUnlockListResult,
        coinVestingResult,
        exchangeAssetsResult,
        exchangeBalanceListResult,
        exchangeBalanceChartResult,
        hyperliquidWhaleAlertResult,
        hyperliquidWhalePositionResult,
        hyperliquidPositionResult,
        hyperliquidUserPositionResult,
        hyperliquidWalletPositionDistributionResult,
        hyperliquidWalletPnlDistributionResult,
        liquidationHeatmapResult,
      ] = baseResults;
      if (disposed) {
        return;
      }

      if (fearGreedResult.status === 'fulfilled') {
        const runtime = parseFearGreedRuntime(fearGreedResult.value);
        if (runtime) {
          setFearGreedRuntime(runtime);
        }
      }

      if (grayscaleHoldingsResult.status === 'fulfilled') {
        const runtime = parseGrayscaleHoldingsRuntime(grayscaleHoldingsResult.value);
        if (runtime) {
          setGrayscaleHoldingsRuntime(runtime);
        }
      }

      if (coinUnlockListResult.status === 'fulfilled') {
        const runtime = parseCoinUnlockListRuntime(coinUnlockListResult.value);
        if (runtime) {
          setCoinUnlockListRuntime(runtime);
        }
      }

      if (coinVestingResult.status === 'fulfilled') {
        const runtime = parseCoinVestingRuntime(coinVestingResult.value, vestingScopeSymbol);
        if (runtime) {
          setCoinVestingRuntimeMap((previous) => ({
            ...previous,
            [runtime.symbol]: runtime,
          }));
        }
      }

      if (exchangeAssetsResult.status === 'fulfilled') {
        const runtime = parseExchangeAssetsRuntime(exchangeAssetsResult.value);
        if (runtime) {
          setExchangeAssetsRuntime(runtime);
        }
      }

      if (exchangeBalanceListResult.status === 'fulfilled') {
        const runtime = parseExchangeBalanceListRuntime(exchangeBalanceListResult.value);
        if (runtime) {
          setExchangeBalanceListRuntime(runtime);
        }
      }

      if (exchangeBalanceChartResult.status === 'fulfilled') {
        const runtime = parseExchangeBalanceChartRuntime(exchangeBalanceChartResult.value);
        if (runtime) {
          setExchangeBalanceChartRuntime(runtime);
        }
      }

      if (hyperliquidWhaleAlertResult.status === 'fulfilled') {
        const runtime = parseHyperliquidWhaleAlertRuntime(hyperliquidWhaleAlertResult.value);
        if (runtime) {
          setHyperliquidWhaleAlertRuntime(runtime);
        }
      }

      if (hyperliquidWhalePositionResult.status === 'fulfilled') {
        const runtime = parseHyperliquidWhalePositionRuntime(hyperliquidWhalePositionResult.value);
        if (runtime) {
          setHyperliquidWhalePositionRuntime(runtime);
        }
      }

      if (hyperliquidPositionResult.status === 'fulfilled') {
        const runtime = parseHyperliquidPositionRuntime(hyperliquidPositionResult.value);
        if (runtime) {
          setHyperliquidPositionRuntime(runtime);
        }
      }

      if (hyperliquidUserPositionResult.status === 'fulfilled') {
        const runtime = parseHyperliquidUserPositionRuntime(hyperliquidUserPositionResult.value);
        if (runtime) {
          setHyperliquidUserPositionRuntime(runtime);
        }
      }

      if (hyperliquidWalletPositionDistributionResult.status === 'fulfilled') {
        const runtime = parseHyperliquidWalletDistributionRuntime(hyperliquidWalletPositionDistributionResult.value);
        if (runtime) {
          setHyperliquidWalletPositionDistributionRuntime(runtime);
        }
      }

      if (hyperliquidWalletPnlDistributionResult.status === 'fulfilled') {
        const runtime = parseHyperliquidWalletDistributionRuntime(hyperliquidWalletPnlDistributionResult.value);
        if (runtime) {
          setHyperliquidWalletPnlDistributionRuntime(runtime);
        }
      }

      if (liquidationHeatmapResult.status === 'fulfilled') {
        const runtime = parseLiquidationHeatmapRuntime(liquidationHeatmapResult.value);
        if (runtime) {
          setLiquidationHeatmapRuntime(runtime);
        }
      }

      setEtfFlowRuntimeMap((previous) => {
        const next = { ...previous };
        ETF_ASSET_OPTIONS.forEach((item, index) => {
          const result = etfFlowResults[index];
          if (result?.status !== 'fulfilled') {
            return;
          }

          const runtime = parseEtfFlowRuntime(result.value, item.asset);
          if (runtime) {
            next[item.asset] = runtime;
          }
        });
        return next;
      });

      setFootprintRuntimeMap((previous) => {
        const next = { ...previous };
        FOOTPRINT_ASSET_OPTIONS.forEach((item, index) => {
          const result = footprintResults[index];
          if (result?.status !== 'fulfilled') {
            return;
          }

          const runtime = parseFuturesFootprintRuntime(result.value, item.asset);
          if (runtime) {
            next[item.asset] = runtime;
          }
        });
        return next;
      });

      setLongShortRatioRuntimeMap((previous) => {
        const next = { ...previous };
        LONG_SHORT_ASSET_OPTIONS.forEach((item, index) => {
          const result = longShortRatioResults[index];
          if (result?.status !== 'fulfilled') {
            return;
          }

          const runtime = parseLongShortRatioRuntime(result.value, item.asset);
          if (runtime) {
            next[item.asset] = runtime;
          }
        });
        return next;
      });
    };

    void loadIndicatorRuntime();
    const timer = window.setInterval(() => {
      void loadIndicatorRuntime();
    }, 60_000);

    return () => {
      disposed = true;
      window.clearInterval(timer);
    };
  }, [activeCoinUnlockSymbol]);

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
      <div
        ref={gridRef}
        className="indicator-module-grid"
        style={{
          ['--indicator-grid-columns' as string]: String(gridMetrics.columnCount),
          ['--indicator-grid-row-height' as string]: `${gridMetrics.rowHeight}px`,
          ['--indicator-grid-gap' as string]: `${gridMetrics.gap}px`,
        }}
      >
        {indicators.map((indicator) => {
          const layout = resolveIndicatorCardLayout(
            indicator,
            indicatorLayouts[indicator.id],
            gridMetrics.columnCount,
          );

          return (
            <IndicatorCard
              key={indicator.id}
              indicator={indicator}
              layout={layout}
              gridMetrics={gridMetrics}
              onLayoutChange={handleIndicatorLayoutChange}
              onSelectAsset={handleIndicatorAssetChange}
            />
          );
        })}
      </div>
    </div>
  );
};

export default IndicatorModule;

