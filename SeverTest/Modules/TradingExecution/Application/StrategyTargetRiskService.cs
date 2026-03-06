using ServerTest.Models.Strategy;
using ServerTest.Modules.MarketData.Domain;

namespace ServerTest.Modules.TradingExecution.Application
{
    public sealed class StrategyTargetRiskContext
    {
        public StrategyTradeTarget Target { get; init; } = new();
        public ContractDetails? Contract { get; init; }
        public decimal CurrentOpenQty { get; init; }
        public decimal CurrentEvaluatedQty { get; set; }
    }

    public sealed class StrategyTargetRiskRuleResult
    {
        public string RuleName { get; init; } = string.Empty;
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public decimal? NormalizedQty { get; init; }
    }

    public sealed class StrategyTargetRiskResult
    {
        public bool Success { get; init; }
        public decimal NormalizedQty { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<StrategyRiskCheckSnapshot> Snapshots { get; init; } = Array.Empty<StrategyRiskCheckSnapshot>();
    }

    public interface IStrategyTargetRiskRule
    {
        ValueTask<StrategyTargetRiskRuleResult> EvaluateAsync(StrategyTargetRiskContext context, CancellationToken ct);
    }

    public sealed class StrategyTargetRiskService
    {
        private readonly IReadOnlyList<IStrategyTargetRiskRule> _rules;

        public StrategyTargetRiskService(IEnumerable<IStrategyTargetRiskRule> rules)
        {
            _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
        }

        public async Task<StrategyTargetRiskResult> EvaluateAsync(StrategyTargetRiskContext context, CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var normalizedQty = context.Target?.NormalizedQty > 0 ? context.Target.NormalizedQty : context.Target?.RequestedQty ?? 0m;
            context.CurrentEvaluatedQty = normalizedQty;
            var snapshots = new List<StrategyRiskCheckSnapshot>(_rules.Count);
            var finalMessage = "风控校验通过";

            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var ruleResult = await rule.EvaluateAsync(context, ct).ConfigureAwait(false);
                if (ruleResult.NormalizedQty.HasValue)
                {
                    normalizedQty = ruleResult.NormalizedQty.Value;
                    context.CurrentEvaluatedQty = normalizedQty;
                }

                snapshots.Add(new StrategyRiskCheckSnapshot
                {
                    Rule = ruleResult.RuleName,
                    Success = ruleResult.Success,
                    Message = ruleResult.Message
                });
                finalMessage = ruleResult.Message;

                if (!ruleResult.Success)
                {
                    return new StrategyTargetRiskResult
                    {
                        Success = false,
                        NormalizedQty = normalizedQty,
                        Message = ruleResult.Message,
                        Snapshots = snapshots
                    };
                }
            }

            return new StrategyTargetRiskResult
            {
                Success = true,
                NormalizedQty = normalizedQty,
                Message = finalMessage,
                Snapshots = snapshots
            };
        }
    }

    public sealed class TargetBasicValidationRule : IStrategyTargetRiskRule
    {
        public ValueTask<StrategyTargetRiskRuleResult> EvaluateAsync(StrategyTargetRiskContext context, CancellationToken ct)
        {
            _ = ct;
            var target = context.Target ?? new StrategyTradeTarget();
            if (string.IsNullOrWhiteSpace(target.Exchange) || string.IsNullOrWhiteSpace(target.Symbol))
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "基础校验",
                    Success = false,
                    Message = "缺少交易所或交易对"
                });
            }

            if (target.IsOpen && target.RequestedQty <= 0)
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "基础校验",
                    Success = false,
                    Message = "开仓数量必须大于0"
                });
            }

            return ValueTask.FromResult(new StrategyTargetRiskRuleResult
            {
                RuleName = "基础校验",
                Success = true,
                Message = "基础参数校验通过"
            });
        }
    }

    public sealed class TargetMarketOrderRule : IStrategyTargetRiskRule
    {
        public ValueTask<StrategyTargetRiskRuleResult> EvaluateAsync(StrategyTargetRiskContext context, CancellationToken ct)
        {
            _ = ct;
            var target = context.Target ?? new StrategyTradeTarget();
            if (!target.IsOpen)
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "市价规则",
                    Success = true,
                    Message = "平仓目标跳过数量归一化",
                    NormalizedQty = context.CurrentEvaluatedQty
                });
            }

            var normalization = MarketOrderRuleHelper.Normalize(target.RequestedQty, context.Contract);
            return ValueTask.FromResult(new StrategyTargetRiskRuleResult
            {
                RuleName = "市价规则",
                Success = normalization.Success,
                Message = normalization.Message,
                NormalizedQty = normalization.NormalizedQty
            });
        }
    }

    public sealed class TargetMaxPositionRule : IStrategyTargetRiskRule
    {
        public ValueTask<StrategyTargetRiskRuleResult> EvaluateAsync(StrategyTargetRiskContext context, CancellationToken ct)
        {
            _ = ct;
            var target = context.Target ?? new StrategyTradeTarget();
            if (!target.IsOpen)
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "最大持仓",
                    Success = true,
                    Message = "平仓目标跳过最大持仓校验",
                    NormalizedQty = context.CurrentEvaluatedQty
                });
            }

            var effectiveMaxPositionQty = StrategyTradeTargetHelper.ResolveEffectiveMaxPositionQty(target);
            if (effectiveMaxPositionQty <= 0)
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "最大持仓",
                    Success = false,
                    Message = "最大持仓配置无效",
                    NormalizedQty = context.CurrentEvaluatedQty
                });
            }

            var currentOpenQty = Math.Max(0m, context.CurrentOpenQty);
            var requestQty = Math.Max(0m, context.CurrentEvaluatedQty);
            if (currentOpenQty + requestQty > effectiveMaxPositionQty)
            {
                return ValueTask.FromResult(new StrategyTargetRiskRuleResult
                {
                    RuleName = "最大持仓",
                    Success = false,
                    Message = $"开仓被最大持仓限制阻断：当前同向持仓{currentOpenQty} + 本次开仓{requestQty} > 上限{effectiveMaxPositionQty}",
                    NormalizedQty = requestQty
                });
            }

            return ValueTask.FromResult(new StrategyTargetRiskRuleResult
            {
                RuleName = "最大持仓",
                Success = true,
                Message = "最大持仓校验通过",
                NormalizedQty = requestQty
            });
        }
    }

    public sealed class MarketOrderNormalizationResult
    {
        public bool Success { get; init; }
        public decimal NormalizedQty { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static class MarketOrderRuleHelper
    {
        public static MarketOrderNormalizationResult Normalize(decimal qty, ContractDetails? contract)
        {
            if (qty <= 0)
            {
                return new MarketOrderNormalizationResult
                {
                    Success = false,
                    NormalizedQty = 0m,
                    Message = "市价单数量必须大于0"
                };
            }

            var normalizedQty = qty;
            if (contract?.AmountPrecision != null)
            {
                var digits = Math.Max(0, contract.AmountPrecision.Value);
                var factor = (decimal)Math.Pow(10, digits);
                normalizedQty = Math.Floor(normalizedQty * factor) / factor;
            }

            if (contract?.MinOrderAmount != null && normalizedQty < contract.MinOrderAmount.Value)
            {
                return new MarketOrderNormalizationResult
                {
                    Success = false,
                    NormalizedQty = normalizedQty,
                    Message = $"市价单数量{normalizedQty}低于交易所最小下单量{contract.MinOrderAmount.Value}"
                };
            }

            if (contract?.MaxOrderAmount != null && normalizedQty > contract.MaxOrderAmount.Value)
            {
                normalizedQty = contract.MaxOrderAmount.Value;
            }

            if (normalizedQty <= 0)
            {
                return new MarketOrderNormalizationResult
                {
                    Success = false,
                    NormalizedQty = normalizedQty,
                    Message = "市价单数量归一化后无效"
                };
            }

            if (normalizedQty != qty)
            {
                return new MarketOrderNormalizationResult
                {
                    Success = true,
                    NormalizedQty = normalizedQty,
                    Message = $"市价单数量已按交易所规则归一化：{qty} -> {normalizedQty}"
                };
            }

            return new MarketOrderNormalizationResult
            {
                Success = true,
                NormalizedQty = normalizedQty,
                Message = "市价单数量校验通过"
            };
        }
    }
}
