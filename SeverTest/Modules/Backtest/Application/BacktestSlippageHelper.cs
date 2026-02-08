using System;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测滑点计算（统一复用，避免重复实现）
    /// </summary>
    internal static class BacktestSlippageHelper
    {
        /// <summary>
        /// 按固定 BPS 计算滑点后的执行价格。
        /// buy 方向加价，sell 方向减价。
        /// </summary>
        public static decimal ApplySlippage(decimal price, string orderSide, int slippageBps)
        {
            if (slippageBps == 0)
            {
                return price;
            }

            var ratio = slippageBps / 10000m;
            return orderSide.Equals("buy", StringComparison.OrdinalIgnoreCase)
                ? price * (1 + ratio)
                : price * (1 - ratio);
        }
    }
}
