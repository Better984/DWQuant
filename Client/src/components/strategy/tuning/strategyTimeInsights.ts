import type { LocalBacktestTrade } from '../localBacktestEngine';
import type {
  StrategyTimeAnalysis,
  StrategyTimeBucketInsight,
  StrategyTimeInsightGroup,
} from './strategyTuningTypes';

type BucketAccumulator = {
  key: string;
  label: string;
  count: number;
  winCount: number;
  lossCount: number;
  pnlSum: number;
  returnPctSum: number;
  holdingHoursSum: number;
};

const formatPercent = (value: number) => `${value.toFixed(1)}%`;

const toPercent = (value: number, base: number) => {
  if (!Number.isFinite(value) || !Number.isFinite(base) || Math.abs(base) <= 1e-9) {
    return 0;
  }
  return (value / base) * 100;
};

const finalizeBucket = (bucket: BucketAccumulator): StrategyTimeBucketInsight => {
  const count = bucket.count;
  return {
    key: bucket.key,
    label: bucket.label,
    count,
    winCount: bucket.winCount,
    lossCount: bucket.lossCount,
    winRate: count > 0 ? bucket.winCount / count : 0,
    lossRate: count > 0 ? bucket.lossCount / count : 0,
    avgPnl: count > 0 ? bucket.pnlSum / count : 0,
    avgReturnPct: count > 0 ? bucket.returnPctSum / count : 0,
    avgHoldingHours: count > 0 ? bucket.holdingHoursSum / count : 0,
  };
};

const buildInsightGroup = (
  id: string,
  title: string,
  description: string,
  buckets: StrategyTimeBucketInsight[],
) => {
  if (buckets.length <= 0) {
    return {
      id,
      title,
      description,
      bestBucket: null,
      riskBucket: null,
      buckets,
      summary: '暂无足够样本。',
    } satisfies StrategyTimeInsightGroup;
  }

  const minSamples = Math.max(3, Math.floor(buckets.reduce((sum, item) => sum + item.count, 0) / 10));
  const scoped = buckets.filter((item) => item.count >= minSamples);
  const candidates = scoped.length > 0 ? scoped : buckets;
  const totalSamples = buckets.reduce((sum, item) => sum + item.count, 0);
  const bestBucket = [...candidates].sort((left, right) => {
    if (right.winRate !== left.winRate) {
      return right.winRate - left.winRate;
    }
    return right.avgPnl - left.avgPnl;
  })[0] || null;
  const riskBucket = [...candidates].sort((left, right) => {
    if (right.lossRate !== left.lossRate) {
      return right.lossRate - left.lossRate;
    }
    return left.avgPnl - right.avgPnl;
  })[0] || null;
  const describeBucket = (label: string, bucket: StrategyTimeBucketInsight) => (
    `${label}${bucket.label}：样本 ${bucket.count} 笔（占总样本 ${formatPercent((bucket.count / Math.max(1, totalSamples)) * 100)}），` +
    `胜率 ${formatPercent(bucket.winRate * 100)}，亏损率 ${formatPercent(bucket.lossRate * 100)}`
  );

  return {
    id,
    title,
    description,
    bestBucket,
    riskBucket,
    buckets,
    summary:
      bestBucket && riskBucket
        ? bestBucket.key === riskBucket.key
          ? `${describeBucket('', bestBucket)}。`
          : `${describeBucket('高胜率区间 ', bestBucket)}。${describeBucket('高风险区间 ', riskBucket)}。`
        : '暂无足够样本。',
  } satisfies StrategyTimeInsightGroup;
};

const buildGroup = (
  id: string,
  title: string,
  description: string,
  trades: LocalBacktestTrade[],
  mapping: Array<{ key: string; label: string; match: (trade: LocalBacktestTrade) => boolean }>,
) => {
  const buckets = mapping.map((item) => ({
    key: item.key,
    label: item.label,
    count: 0,
    winCount: 0,
    lossCount: 0,
    pnlSum: 0,
    returnPctSum: 0,
    holdingHoursSum: 0,
  } satisfies BucketAccumulator));

  trades.forEach((trade) => {
    const bucket = buckets.find((_, index) => (
      mapping[index].match(trade)
    ));
    if (!bucket) {
      return;
    }
    const holdingHours = Math.max(0, (trade.exitTime - trade.entryTime) / (60 * 60 * 1000));
    bucket.count += 1;
    if (trade.pnl >= 0) {
      bucket.winCount += 1;
    } else {
      bucket.lossCount += 1;
    }
    bucket.pnlSum += trade.pnl;
    bucket.returnPctSum += toPercent(trade.pnl, trade.entryPrice * trade.qty);
    bucket.holdingHoursSum += holdingHours;
  });

  return buildInsightGroup(
    id,
    title,
    description,
    buckets.filter((item) => item.count > 0).map((item) => finalizeBucket(item)),
  );
};

const weekDayFormatter = new Intl.DateTimeFormat('zh-CN', {
  weekday: 'short',
});

const hourFormatter = new Intl.DateTimeFormat('zh-CN', {
  hour: '2-digit',
  hourCycle: 'h23',
});

const nyTimeFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: 'America/New_York',
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

const getLocalWeekday = (timestamp: number) => weekDayFormatter.format(new Date(timestamp));

const getLocalHour = (timestamp: number) => Number(hourFormatter.format(new Date(timestamp)));

const getNySessionBucket = (timestamp: number) => {
  const parts = nyTimeFormatter.formatToParts(new Date(timestamp));
  const hour = Number(parts.find((item) => item.type === 'hour')?.value || 0);
  const minute = Number(parts.find((item) => item.type === 'minute')?.value || 0);
  const weekday = parts.find((item) => item.type === 'weekday')?.value || '';
  const isWeekend = weekday === 'Sat' || weekday === 'Sun';
  const minutes = hour * 60 + minute;
  if (isWeekend) {
    return 'weekend';
  }
  if (minutes >= 9 * 60 + 30 && minutes < 16 * 60) {
    return 'us_main';
  }
  if ((minutes >= 4 * 60 && minutes < 9 * 60 + 30) || (minutes >= 16 * 60 && minutes < 20 * 60)) {
    return 'us_extended';
  }
  return 'off_hours';
};

export const buildStrategyTimeAnalysis = (trades: LocalBacktestTrade[]): StrategyTimeAnalysis => {
  const closedTrades = trades.filter((item) => !item.isOpen && item.exitTime > item.entryTime);
  if (closedTrades.length <= 0) {
    return {
      totalClosedTrades: 0,
      groups: [],
    };
  }

  const durationGroup = buildGroup(
    'holding_duration',
    '持仓周期分布',
    '观察持仓时长与胜率、平均盈亏之间的关系。',
    closedTrades,
    [
      { key: 'lt15m', label: '15分钟内', match: (trade) => trade.exitTime - trade.entryTime < 15 * 60 * 1000 },
      {
        key: '15m_1h',
        label: '15分钟 ~ 1小时',
        match: (trade) => trade.exitTime - trade.entryTime >= 15 * 60 * 1000
          && trade.exitTime - trade.entryTime < 60 * 60 * 1000,
      },
      {
        key: '1h_4h',
        label: '1小时 ~ 4小时',
        match: (trade) => trade.exitTime - trade.entryTime >= 60 * 60 * 1000
          && trade.exitTime - trade.entryTime < 4 * 60 * 60 * 1000,
      },
      {
        key: '4h_12h',
        label: '4小时 ~ 12小时',
        match: (trade) => trade.exitTime - trade.entryTime >= 4 * 60 * 60 * 1000
          && trade.exitTime - trade.entryTime < 12 * 60 * 60 * 1000,
      },
      {
        key: '12h_24h',
        label: '12小时 ~ 24小时',
        match: (trade) => trade.exitTime - trade.entryTime >= 12 * 60 * 60 * 1000
          && trade.exitTime - trade.entryTime < 24 * 60 * 60 * 1000,
      },
      {
        key: 'gt24h',
        label: '24小时以上',
        match: (trade) => trade.exitTime - trade.entryTime >= 24 * 60 * 60 * 1000,
      },
    ],
  );

  const weekdayOrder = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];
  const weekdayGroup = buildGroup(
    'entry_weekday',
    '开仓星期分布',
    '识别哪些星期开仓更稳定、哪些星期更容易产生亏损。',
    closedTrades,
    weekdayOrder.map((label) => ({
      key: label,
      label,
      match: (trade) => getLocalWeekday(trade.entryTime) === label,
    })),
  );

  const hourGroup = buildGroup(
    'entry_hour',
    '开仓时段分布',
    '根据本地时间把开仓样本按 4 小时桶分组，查看峰值时段。',
    closedTrades,
    [
      { key: '00_04', label: '00:00 ~ 04:00', match: (trade) => getLocalHour(trade.entryTime) < 4 },
      { key: '04_08', label: '04:00 ~ 08:00', match: (trade) => getLocalHour(trade.entryTime) >= 4 && getLocalHour(trade.entryTime) < 8 },
      { key: '08_12', label: '08:00 ~ 12:00', match: (trade) => getLocalHour(trade.entryTime) >= 8 && getLocalHour(trade.entryTime) < 12 },
      { key: '12_16', label: '12:00 ~ 16:00', match: (trade) => getLocalHour(trade.entryTime) >= 12 && getLocalHour(trade.entryTime) < 16 },
      { key: '16_20', label: '16:00 ~ 20:00', match: (trade) => getLocalHour(trade.entryTime) >= 16 && getLocalHour(trade.entryTime) < 20 },
      { key: '20_24', label: '20:00 ~ 24:00', match: (trade) => getLocalHour(trade.entryTime) >= 20 },
    ],
  );

  const marketSessionGroup = buildGroup(
    'market_session',
    '市场活跃时段',
    '按纽约时间区分美股主时段、延伸时段和非美股时段，自动处理夏令时。',
    closedTrades,
    [
      { key: 'us_main', label: '美股主时段', match: (trade) => getNySessionBucket(trade.entryTime) === 'us_main' },
      { key: 'us_extended', label: '美股延伸时段', match: (trade) => getNySessionBucket(trade.entryTime) === 'us_extended' },
      { key: 'off_hours', label: '非美股时段', match: (trade) => getNySessionBucket(trade.entryTime) === 'off_hours' },
      { key: 'weekend', label: '纽约周末', match: (trade) => getNySessionBucket(trade.entryTime) === 'weekend' },
    ],
  );

  return {
    totalClosedTrades: closedTrades.length,
    groups: [
      durationGroup,
      weekdayGroup,
      hourGroup,
      marketSessionGroup,
    ],
  };
};
