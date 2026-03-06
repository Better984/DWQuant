using ServerTest.Models;

namespace ServerTest.Models.Strategy
{
    public static class StrategyTradeTargetTypes
    {
        public const string OpenLong = "open_long";
        public const string OpenShort = "open_short";
        public const string CloseLong = "close_long";
        public const string CloseShort = "close_short";
    }

    public readonly record struct StrategyTradeActionDescriptor(
        string TargetType,
        string PositionSide,
        string OrderSide,
        bool ReduceOnly);

    public sealed class StrategyMarketTimeSlice
    {
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Timeframe { get; init; } = string.Empty;
        public int TimeframeSec { get; init; }
        public long CandleTimestamp { get; init; }
        public bool IsBarClose { get; init; }

        public static StrategyMarketTimeSlice FromMarketTask(MarketDataTask task)
        {
            return new StrategyMarketTimeSlice
            {
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Timeframe = task.Timeframe,
                TimeframeSec = task.TimeframeSec,
                CandleTimestamp = task.CandleTimestamp,
                IsBarClose = task.IsBarClose
            };
        }

        public DateTimeOffset ResolveSignalTime(DateTimeOffset fallback)
        {
            if (CandleTimestamp > 0)
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(CandleTimestamp);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            return fallback == default ? DateTimeOffset.UtcNow : fallback;
        }
    }

    public sealed class StrategyRiskCheckSnapshot
    {
        public string Rule { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class StrategyTradeTarget
    {
        public string TargetId { get; init; } = Guid.NewGuid().ToString("N");
        public string StrategyUid { get; init; } = string.Empty;
        public long? Uid { get; init; }
        public long? UsId { get; init; }
        public long? StrategyVersionId { get; init; }
        public int? StrategyVersionNo { get; init; }
        public string StrategyState { get; init; } = string.Empty;
        public long? ExchangeApiKeyId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public int TimeframeSec { get; init; }
        public string Stage { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public string[] Param { get; init; } = Array.Empty<string>();
        public string TargetType { get; init; } = string.Empty;
        public string PositionSide { get; init; } = string.Empty;
        public string OrderSide { get; init; } = string.Empty;
        public bool ReduceOnly { get; init; }
        public decimal RequestedQty { get; init; }
        public decimal NormalizedQty { get; set; }
        public decimal MaxPositionQty { get; init; }
        public int Leverage { get; init; }
        public decimal? TakeProfitPct { get; init; }
        public decimal? StopLossPct { get; init; }
        public bool TrailingEnabled { get; init; }
        public decimal? TrailingActivationPct { get; init; }
        public decimal? TrailingDrawdownPct { get; init; }
        public StrategyMarketTimeSlice TimeSlice { get; init; } = new();
        public IReadOnlyList<ConditionEvaluationSnapshot> TriggerResults { get; init; } = Array.Empty<ConditionEvaluationSnapshot>();
        public IReadOnlyList<StrategyRiskCheckSnapshot> RiskChecks { get; set; } = Array.Empty<StrategyRiskCheckSnapshot>();
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

        public bool IsOpen => !ReduceOnly;
        public bool IsClose => ReduceOnly;
    }

    public static class StrategyTradeTargetHelper
    {
        public static bool TryCreate(
            string strategyUid,
            long? uid,
            long? usId,
            long? strategyVersionId,
            int? strategyVersionNo,
            string strategyState,
            long? exchangeApiKeyId,
            TradeConfig? trade,
            StrategyMethod method,
            MarketDataTask task,
            IReadOnlyList<ConditionEvaluationSnapshot> triggerResults,
            DateTimeOffset createdAt,
            out StrategyTradeTarget target,
            out string error)
        {
            target = new StrategyTradeTarget();
            error = string.Empty;

            var action = method?.Param != null && method.Param.Length > 0 ? method.Param[0] : string.Empty;
            if (!TryParseAction(action, out var descriptor))
            {
                error = $"不支持的交易动作: {action}";
                return false;
            }

            trade ??= new TradeConfig();
            var requestedQty = trade.Sizing?.OrderQty ?? 0m;
            target = new StrategyTradeTarget
            {
                StrategyUid = strategyUid ?? string.Empty,
                Uid = uid,
                UsId = usId,
                StrategyVersionId = strategyVersionId,
                StrategyVersionNo = strategyVersionNo,
                StrategyState = strategyState ?? string.Empty,
                ExchangeApiKeyId = exchangeApiKeyId,
                Exchange = trade.Exchange ?? string.Empty,
                Symbol = trade.Symbol ?? string.Empty,
                TimeframeSec = trade.TimeframeSec,
                Stage = method?.Method ?? string.Empty,
                Method = method?.Method ?? string.Empty,
                Param = method?.Param ?? Array.Empty<string>(),
                TargetType = descriptor.TargetType,
                PositionSide = descriptor.PositionSide,
                OrderSide = descriptor.OrderSide,
                ReduceOnly = descriptor.ReduceOnly,
                RequestedQty = requestedQty,
                NormalizedQty = requestedQty,
                MaxPositionQty = trade.Sizing?.MaxPositionQty ?? 0m,
                Leverage = trade.Sizing?.Leverage ?? 1,
                TakeProfitPct = trade.Risk?.TakeProfitPct,
                StopLossPct = trade.Risk?.StopLossPct,
                TrailingEnabled = trade.Risk?.Trailing?.Enabled ?? false,
                TrailingActivationPct = trade.Risk?.Trailing?.ActivationProfitPct,
                TrailingDrawdownPct = trade.Risk?.Trailing?.CloseOnDrawdownPct,
                TimeSlice = StrategyMarketTimeSlice.FromMarketTask(task),
                TriggerResults = triggerResults ?? Array.Empty<ConditionEvaluationSnapshot>(),
                CreatedAt = createdAt == default ? DateTimeOffset.UtcNow : createdAt
            };
            return true;
        }

        public static bool TryResolveFromTask(
            StrategyActionTask task,
            out StrategyTradeTarget target,
            out string error)
        {
            if (task.Target != null)
            {
                target = task.Target;
                error = string.Empty;
                return true;
            }

            var action = task.Param != null && task.Param.Length > 0 ? task.Param[0] : string.Empty;
            if (!TryParseAction(action, out var descriptor))
            {
                target = new StrategyTradeTarget();
                error = $"不支持的交易动作: {action}";
                return false;
            }

            target = new StrategyTradeTarget
            {
                StrategyUid = task.StrategyUid,
                Uid = task.Uid,
                UsId = task.UsId,
                StrategyVersionId = task.StrategyVersionId,
                StrategyVersionNo = task.StrategyVersionNo,
                StrategyState = task.StrategyState,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                TimeframeSec = task.TimeframeSec,
                Stage = task.Stage,
                Method = task.Method,
                Param = task.Param ?? Array.Empty<string>(),
                TargetType = descriptor.TargetType,
                PositionSide = descriptor.PositionSide,
                OrderSide = descriptor.OrderSide,
                ReduceOnly = descriptor.ReduceOnly,
                RequestedQty = task.OrderQty,
                NormalizedQty = task.OrderQty,
                MaxPositionQty = task.MaxPositionQty,
                Leverage = task.Leverage,
                TakeProfitPct = task.TakeProfitPct,
                StopLossPct = task.StopLossPct,
                TrailingEnabled = task.TrailingEnabled,
                TrailingActivationPct = task.TrailingActivationPct,
                TrailingDrawdownPct = task.TrailingDrawdownPct,
                TimeSlice = StrategyMarketTimeSlice.FromMarketTask(task.MarketTask),
                TriggerResults = task.TriggerResults ?? Array.Empty<ConditionEvaluationSnapshot>(),
                CreatedAt = task.CreatedAt == default ? DateTimeOffset.UtcNow : task.CreatedAt
            };
            error = string.Empty;
            return true;
        }

        public static bool TryParseAction(string? action, out StrategyTradeActionDescriptor descriptor)
        {
            descriptor = default;
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            switch (action.Trim().ToUpperInvariant())
            {
                case "LONG":
                    descriptor = new StrategyTradeActionDescriptor(StrategyTradeTargetTypes.OpenLong, "Long", "buy", false);
                    return true;
                case "SHORT":
                    descriptor = new StrategyTradeActionDescriptor(StrategyTradeTargetTypes.OpenShort, "Short", "sell", false);
                    return true;
                case "CLOSELONG":
                    descriptor = new StrategyTradeActionDescriptor(StrategyTradeTargetTypes.CloseLong, "Long", "sell", true);
                    return true;
                case "CLOSESHORT":
                    descriptor = new StrategyTradeActionDescriptor(StrategyTradeTargetTypes.CloseShort, "Short", "buy", true);
                    return true;
                default:
                    return false;
            }
        }

        public static decimal ResolveEffectiveMaxPositionQty(StrategyTradeTarget target)
        {
            if (target == null)
            {
                return 0m;
            }

            if (target.MaxPositionQty > 0)
            {
                return target.MaxPositionQty;
            }

            return target.RequestedQty > 0 ? target.RequestedQty : 0m;
        }

        public static DateTime ResolveSignalTimeUtc(StrategyTradeTarget target)
        {
            if (target == null)
            {
                return DateTime.UtcNow;
            }

            return target.TimeSlice.ResolveSignalTime(target.CreatedAt).UtcDateTime;
        }
    }
}
