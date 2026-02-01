using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Text;

namespace ServerTest.Modules.TradingExecution.Application
{
    public sealed class CcxtStrategyActionExecutor : IStrategyActionExecutor
    {
        private readonly ILogger<CcxtStrategyActionExecutor> _logger;

        public CcxtStrategyActionExecutor(ILogger<CcxtStrategyActionExecutor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public (bool Success, StringBuilder Message) Execute(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (context == null || method == null)
            {
                return BuildResult(method?.Method ?? "MakeTrade", false, "Invalid execution context");
            }

            var trade = context.StrategyConfig.Trade;
            if (trade == null)
            {
                return BuildResult(method.Method, false, "Trade config missing");
            }

            var action = method.Param != null && method.Param.Length > 0 ? method.Param[0] : string.Empty;
            if (!TryMapAction(action, out var orderSide, out var reduceOnly, out var actionLabel))
            {
                return BuildResult(method.Method, false, $"Unsupported trade action: {action}");
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(trade.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(trade.Symbol);
            var qty = trade.Sizing?.OrderQty ?? 0m;
            if (qty <= 0)
            {
                return BuildResult(method.Method, false, "OrderQty <= 0");
            }

            _logger.LogInformation(
                "策略下单(DryRun): {Uid} 动作={Action} 方向={Side} 交易所={Exchange} 币对={Symbol} 数量={Qty} 杠杆={Leverage} 模式={Mode} time={Time}",
                context.Strategy.UidCode,
                actionLabel,
                orderSide,
                exchange,
                symbol,
                qty,
                trade.Sizing?.Leverage ?? 0,
                trade.PositionMode,
                context.CurrentTime.ToString("yyyy-MM-dd HH:mm:ss"));

            // 真实下单示例（先注释，防止误触发）
            // var options = new Dictionary<string, object>
            // {
            //     ["defaultType"] = "swap",
            //     ["enableRateLimit"] = true
            // };
            // if (reduceOnly)
            // {
            //     options["reduceOnly"] = true;
            // }
            //
            // ccxt.Exchange exchangeClient = trade.Exchange.ToLowerInvariant() switch
            // {
            //     "binance" => new ccxt.binanceusdm(options),
            //     "okx" => new ccxt.okx(options),
            //     "bitget" => new ccxt.bitget(options),
            //     _ => throw new NotSupportedException($"Exchange not supported: {trade.Exchange}")
            // };
            //
            // var order = await exchangeClient.create_order(symbol, "market", orderSide, (double)qty, null, new Dictionary<string, object>
            // {
            //     ["reduceOnly"] = reduceOnly
            // });

            return BuildResult(method.Method, true, "DryRun: order request logged");
        }

        private static bool TryMapAction(
            string action,
            out string orderSide,
            out bool reduceOnly,
            out string actionLabel)
        {
            orderSide = string.Empty;
            reduceOnly = false;
            actionLabel = action;

            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            switch (action.Trim().ToUpperInvariant())
            {
                case "LONG":
                    orderSide = "buy";
                    reduceOnly = false;
                    actionLabel = "OpenLong";
                    return true;
                case "SHORT":
                    orderSide = "sell";
                    reduceOnly = false;
                    actionLabel = "OpenShort";
                    return true;
                case "CLOSELONG":
                    orderSide = "sell";
                    reduceOnly = true;
                    actionLabel = "CloseLong";
                    return true;
                case "CLOSESHORT":
                    orderSide = "buy";
                    reduceOnly = true;
                    actionLabel = "CloseShort";
                    return true;
                default:
                    return false;
            }
        }

        private static (bool Success, StringBuilder Message) BuildResult(string method, bool success, string message)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(method))
            {
                builder.Append(method).Append(": ");
            }

            builder.Append(message);
            return (success, builder);
        }
    }
}
