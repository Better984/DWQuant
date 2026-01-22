namespace ServerTest.Models
{
    /// <summary>
    /// 市场数据公共配置类，与 CCXT 保持一致
    /// </summary>
    public static class MarketDataConfig
    {
        /// <summary>
        /// 允许的交易所枚举
        /// </summary>
        public enum ExchangeEnum
        {
            Binance,
            OKX,
            Bitget,
            //Huobi,
            //Gateio
        }

        /// <summary>
        /// 允许的交易对枚举
        /// </summary>
        public enum SymbolEnum
        {
            BTC_USDT,
            ETH_USDT,
            XRP_USDT,
            SOL_USDT,
            DOGE_USDT,
            BNB_USDT,
        }

        /// <summary>
        /// 允许的交易周期枚举
        /// </summary>
        public enum TimeframeEnum
        {
            m1,   // 1m
            m3,   // 3m
            m5,   // 5m
            m15,  // 15m
            m30,  // 30m
            h1,   // 1h
            h2,   // 2h
            h4,   // 4h
            h6,   // 6h
            h8,   // 8h
            h12,  // 12h
            d1,   // 1d
            d3,   // 3d
            w1,   // 1w
            mo1   // 1mo
        }

        /// <summary>
        /// 缓存的历史数据长度
        /// </summary>
        public const int CacheHistoryLength = 2000;

        /// <summary>
        /// 交易所枚举转字符串（CCXT 格式）
        /// </summary>
        public static string ExchangeToString(ExchangeEnum exchange)
        {
            return exchange switch
            {
                ExchangeEnum.Binance => "binance",
                ExchangeEnum.OKX => "okx",
                ExchangeEnum.Bitget => "bitget",
                //ExchangeEnum.Huobi => "huobi",
                //ExchangeEnum.Gateio => "gate",
                _ => throw new ArgumentException($"不支持的交易所: {exchange}")
            };
        }

        /// <summary>
        /// 交易对枚举转字符串（CCXT 格式）
        /// </summary>
        public static string SymbolToString(SymbolEnum symbol)
        {
            return symbol switch
            {
                SymbolEnum.BTC_USDT => "BTC/USDT",
                SymbolEnum.ETH_USDT => "ETH/USDT",
                SymbolEnum.XRP_USDT => "XRP/USDT",
                SymbolEnum.SOL_USDT => "SOL/USDT",
                SymbolEnum.DOGE_USDT => "DOGE/USDT",
                SymbolEnum.BNB_USDT => "BNB/USDT",
                _ => throw new ArgumentException($"不支持的交易对: {symbol}")
            };
        }

        /// <summary>
        /// 周期枚举转字符串（CCXT 格式）
        /// </summary>
        public static string TimeframeToString(TimeframeEnum timeframe)
        {
            return timeframe switch
            {
                TimeframeEnum.m1 => "1m",
                TimeframeEnum.m3 => "3m",
                TimeframeEnum.m5 => "5m",
                TimeframeEnum.m15 => "15m",
                TimeframeEnum.m30 => "30m",
                TimeframeEnum.h1 => "1h",
                TimeframeEnum.h2 => "2h",
                TimeframeEnum.h4 => "4h",
                TimeframeEnum.h6 => "6h",
                TimeframeEnum.h8 => "8h",
                TimeframeEnum.h12 => "12h",
                TimeframeEnum.d1 => "1d",
                TimeframeEnum.d3 => "3d",
                TimeframeEnum.w1 => "1w",
                TimeframeEnum.mo1 => "1mo",
                _ => throw new ArgumentException($"??????????????????: {timeframe}")
            };
        }

        /// <summary>
        /// 周期字符串转毫秒
        /// </summary>
        public static long TimeframeToMs(string timeframe)
        {
            return timeframe switch
            {
                "1m" => 60L * 1000,
                "3m" => 3L * 60 * 1000,
                "5m" => 5L * 60 * 1000,
                "15m" => 15L * 60 * 1000,
                "30m" => 30L * 60 * 1000,
                "1h" => 60L * 60 * 1000,
                "2h" => 2L * 60 * 60 * 1000,
                "4h" => 4L * 60 * 60 * 1000,
                "6h" => 6L * 60 * 60 * 1000,
                "8h" => 8L * 60 * 60 * 1000,
                "12h" => 12L * 60 * 60 * 1000,
                "1d" => 24L * 60 * 60 * 1000,
                "3d" => 3L * 24 * 60 * 60 * 1000,
                "1w" => 7L * 24 * 60 * 60 * 1000,
                "1mo" => 30L * 24 * 60 * 60 * 1000,
                _ => throw new ArgumentException($"??????????????????: {timeframe}")
            };
        }

        /// <summary>
        /// 获取交易所的合约类型配置
        /// </summary>
        public static Dictionary<string, object> GetExchangeOptions(ExchangeEnum exchange)
        {
            return exchange switch
            {
                ExchangeEnum.Binance => new Dictionary<string, object>
                {
                    { "defaultType", "future" }
                },
                ExchangeEnum.OKX => new Dictionary<string, object>
                {
                    { "options", new Dictionary<string, object> { { "defaultType", "swap" } } }
                },
                ExchangeEnum.Bitget => new Dictionary<string, object>
                {
                    { "options", new Dictionary<string, object> { { "defaultType", "swap" } } }
                },
                //ExchangeEnum.Huobi => new Dictionary<string, object>
                //{
                //    { "options", new Dictionary<string, object> { { "defaultType", "swap" } } }
                //},
                //ExchangeEnum.Gateio => new Dictionary<string, object>
                //{
                //    { "options", new Dictionary<string, object> { { "defaultType", "swap" } } }
                //},
                _ => new Dictionary<string, object>()
            };
        }
    }
}
