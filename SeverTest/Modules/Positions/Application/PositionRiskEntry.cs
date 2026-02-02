using ServerTest.Domain.Entities;
using ServerTest.Modules.Positions.Domain;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class PositionRiskEntry
    {
        public PositionRiskEntry(StrategyPosition position, PositionRiskConfig? config)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            PositionId = position.PositionId;
            Uid = position.Uid;
            UsId = position.UsId;
            ExchangeApiKeyId = position.ExchangeApiKeyId;
            Exchange = position.Exchange;
            Symbol = position.Symbol;
            Side = position.Side;
            EntryPrice = position.EntryPrice;
            Qty = position.Qty;
            StopLossPrice = position.StopLossPrice;
            TakeProfitPrice = position.TakeProfitPrice;
            TrailingEnabled = position.TrailingEnabled;
            TrailingStopPrice = position.TrailingStopPrice;
            TrailingTriggered = position.TrailingTriggered;
            Status = position.Status;

            ApplyTrailingConfig(config);
        }

        public long PositionId { get; }
        public long Uid { get; }
        public long UsId { get; }
        public long? ExchangeApiKeyId { get; }
        public string Exchange { get; }
        public string Symbol { get; }
        public string Side { get; }
        public decimal EntryPrice { get; }
        public decimal Qty { get; }
        public string Status { get; private set; }
        public decimal? StopLossPrice { get; }
        public decimal? TakeProfitPrice { get; }
        public bool TrailingEnabled { get; }
        public decimal? TrailingStopPrice { get; private set; }
        public bool TrailingTriggered { get; private set; }
        public decimal? TrailingActivationPct { get; private set; }
        public decimal? TrailingDrawdownPct { get; private set; }
        public decimal? TrailingActivationPrice { get; private set; }
        public decimal? TrailingUpdateThresholdPrice { get; private set; }

        public bool HasTrailingConfig =>
            TrailingActivationPct.HasValue &&
            TrailingDrawdownPct.HasValue &&
            TrailingActivationPct.Value > 0m &&
            TrailingDrawdownPct.Value > 0m &&
            TrailingDrawdownPct.Value < 1m;

        public void SetTrailingStopPrice(decimal newStopPrice)
        {
            TrailingStopPrice = newStopPrice;
            RebuildTrailingThreshold();
        }

        public void MarkClosed()
        {
            Status = "Closed";
        }

        public void MarkTrailingTriggered()
        {
            TrailingTriggered = true;
        }

        private void ApplyTrailingConfig(PositionRiskConfig? config)
        {
            if (config == null)
            {
                return;
            }

            TrailingActivationPct = config.ActivationPct;
            TrailingDrawdownPct = config.DrawdownPct;
            if (!HasTrailingConfig)
            {
                return;
            }

            TrailingActivationPrice = BuildActivationPrice(EntryPrice, TrailingActivationPct!.Value, Side);
            RebuildTrailingThreshold();
        }

        private void RebuildTrailingThreshold()
        {
            if (!HasTrailingConfig || !TrailingStopPrice.HasValue)
            {
                TrailingUpdateThresholdPrice = null;
                return;
            }

            var drawdownPct = TrailingDrawdownPct!.Value;
            if (drawdownPct <= 0m || drawdownPct >= 1m)
            {
                TrailingUpdateThresholdPrice = null;
                return;
            }
            TrailingUpdateThresholdPrice = Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? TrailingStopPrice.Value / (1 - drawdownPct)
                : TrailingStopPrice.Value / (1 + drawdownPct);
        }

        private static decimal BuildActivationPrice(decimal entryPrice, decimal activationPct, string side)
        {
            if (entryPrice <= 0 || activationPct <= 0)
            {
                return 0m;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + activationPct)
                : entryPrice * (1 - activationPct);
        }
    }
}
