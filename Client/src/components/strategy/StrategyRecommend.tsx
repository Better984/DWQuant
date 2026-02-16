import React, { useCallback, useEffect, useMemo, useState } from 'react';
import OfficialStrategyItem from './OfficialStrategyItem';
import StrategyMarketItem from './StrategyMarketItem';
import type { StrategyConfig, StrategyTradeConfig } from './StrategyModule.types';
import { HttpClient, getToken } from '../../network/index.ts';
import { useNotification } from '../ui/index.ts';
import './StrategyRecommend.css';

type OfficialRecord = {
  defId: number;
  name: string;
  description: string;
  versionNo: number;
  configJson?: StrategyConfig;
};

type MarketRecord = {
  marketId: number;
  title: string;
  description: string;
  configJson?: StrategyConfig;
  authorName?: string | null;
  updatedAt?: string;
};

const resolveSymbol = (symbol?: string) => {
  if (!symbol) {
    return { currency: '-', pair: '-' };
  }
  const parts = symbol.split('/');
  return { currency: parts[0] || symbol, pair: symbol };
};

const formatPercent = (value?: number) => {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }
  const normalized = value > 1 ? value : value * 100;
  const text = normalized.toFixed(2).replace(/\.00$/, '');
  return `${text}%`;
};

const formatQuantity = (value: number | undefined, currency: string) => {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }
  return `${value} ${currency}`;
};

const formatUpdatedAt = (value?: string) => {
  if (!value) {
    return '';
  }
  return value.replace('T', ' ').replace('Z', '');
};

const StrategyRecommend: React.FC = () => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const { error: showError } = useNotification();
  const [official, setOfficial] = useState<OfficialRecord[]>([]);
  const [market, setMarket] = useState<MarketRecord[]>([]);
  const [loadingOfficial, setLoadingOfficial] = useState(false);
  const [loadingMarket, setLoadingMarket] = useState(false);

  const fetchOfficial = useCallback(async () => {
    setLoadingOfficial(true);
    try {
      const data = await client.postProtocol<OfficialRecord[]>('/api/strategy/official/list', 'strategy.official.list');
      setOfficial(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取官方策略失败';
      showError(message);
    } finally {
      setLoadingOfficial(false);
    }
  }, [client, showError]);

  const fetchMarket = useCallback(async () => {
    setLoadingMarket(true);
    try {
      const data = await client.postProtocol<MarketRecord[]>('/api/strategy/market/list', 'strategy.market.list');
      setMarket(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取策略广场失败';
      showError(message);
    } finally {
      setLoadingMarket(false);
    }
  }, [client, showError]);

  useEffect(() => {
    fetchOfficial();
    fetchMarket();
  }, [fetchOfficial, fetchMarket]);

  const officialItems = useMemo(() => {
    return official.slice(0, 4).map((record) => {
      const trade = record.configJson?.trade as StrategyTradeConfig | undefined;
      const risk = trade?.risk;
      const { currency, pair } = resolveSymbol(trade?.symbol);
      const takeProfit = formatPercent(risk?.takeProfitPct);
      const stopLoss = formatPercent(risk?.stopLossPct);
      const profitLossRatio =
        takeProfit === '-' && stopLoss === '-' ? '-' : `止盈 ${takeProfit} / 止损 ${stopLoss}`;

      return (
        <OfficialStrategyItem
          key={record.defId}
          name={record.name}
          description={record.description}
          exchange={trade?.exchange ?? '-'}
          tradingPair={pair}
          leverage={trade?.sizing?.leverage ?? 0}
          singlePosition={formatQuantity(trade?.sizing?.orderQty, currency)}
          totalPosition={formatQuantity(trade?.sizing?.maxPositionQty, currency)}
          profitLossRatio={profitLossRatio}
          version={record.versionNo}
        />
      );
    });
  }, [official]);

  const marketItems = useMemo(() => {
    return market.slice(0, 4).map((record) => {
      const trade = record.configJson?.trade as StrategyTradeConfig | undefined;
      return (
        <StrategyMarketItem
          key={record.marketId}
          title={record.title}
          description={record.description}
          author={record.authorName}
          exchange={trade?.exchange ?? '-'}
          symbol={trade?.symbol ?? '-'}
          leverage={trade?.sizing?.leverage ?? 0}
          updatedAt={formatUpdatedAt(record.updatedAt)}
        />
      );
    });
  }, [market]);

  return (
    <div className="strategy-recommend">
      <section className="strategy-recommend-section">
        <div className="strategy-recommend-header">
          <h2 className="strategy-recommend-title">官方策略</h2>
          <span className="strategy-recommend-subtitle">官方推荐策略精选</span>
        </div>
        <div className="strategy-recommend-grid">
          {loadingOfficial ? (
            <div className="strategy-recommend-empty">加载中...</div>
          ) : officialItems.length === 0 ? (
            <div className="strategy-recommend-empty">暂无官方策略</div>
          ) : (
            officialItems
          )}
        </div>
      </section>

      <section className="strategy-recommend-section">
        <div className="strategy-recommend-header">
          <h2 className="strategy-recommend-title">策略广场</h2>
          <span className="strategy-recommend-subtitle">用户公开策略精选</span>
        </div>
        <div className="strategy-recommend-grid">
          {loadingMarket ? (
            <div className="strategy-recommend-empty">加载中...</div>
          ) : marketItems.length === 0 ? (
            <div className="strategy-recommend-empty">暂无公开策略</div>
          ) : (
            marketItems
          )}
        </div>
      </section>
    </div>
  );
};

export default StrategyRecommend;


