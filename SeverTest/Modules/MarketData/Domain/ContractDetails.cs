namespace ServerTest.Modules.MarketData.Domain
{
    /// <summary>
    /// 合约详情信息（从 CCXT markets 对象提取）
    /// </summary>
    public sealed class ContractDetails
    {
        /// <summary>
        /// 交易所名称（MarketDataConfig.ExchangeEnum 的字符串值）
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

        /// <summary>
        /// 交易对符号（CCXT 格式，如 BTC/USDT:USDT）
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 基础资产（如 BTC）
        /// </summary>
        public string Base { get; set; } = string.Empty;

        /// <summary>
        /// 计价资产（如 USDT）
        /// </summary>
        public string Quote { get; set; } = string.Empty;

        /// <summary>
        /// 结算资产（合约结算币种）
        /// </summary>
        public string? Settle { get; set; }

        /// <summary>
        /// 是否为合约市场
        /// </summary>
        public bool IsContract { get; set; }

        /// <summary>
        /// 是否为永续合约（swap）
        /// </summary>
        public bool IsSwap { get; set; }

        /// <summary>
        /// 是否为交割合约（future）
        /// </summary>
        public bool IsFuture { get; set; }

        /// <summary>
        /// 是否为线性合约（linear）
        /// </summary>
        public bool? IsLinear { get; set; }

        /// <summary>
        /// 是否为反向合约（inverse）
        /// </summary>
        public bool? IsInverse { get; set; }

        /// <summary>
        /// 合约乘数（contractSize）
        /// </summary>
        public decimal? ContractSize { get; set; }

        /// <summary>
        /// 最小订单数量
        /// </summary>
        public decimal? MinOrderAmount { get; set; }

        /// <summary>
        /// 最大订单数量
        /// </summary>
        public decimal? MaxOrderAmount { get; set; }

        /// <summary>
        /// 最小订单价格
        /// </summary>
        public decimal? MinOrderPrice { get; set; }

        /// <summary>
        /// 最大订单价格
        /// </summary>
        public decimal? MaxOrderPrice { get; set; }

        /// <summary>
        /// 数量精度（小数点后位数）
        /// </summary>
        public int? AmountPrecision { get; set; }

        /// <summary>
        /// 价格精度（小数点后位数）
        /// </summary>
        public int? PricePrecision { get; set; }

        /// <summary>
        /// 最小价格变动（tickSize）
        /// </summary>
        public decimal? TickSize { get; set; }

        /// <summary>
        /// 是否活跃（可交易）
        /// </summary>
        public bool? Active { get; set; }

        /// <summary>
        /// 市场信息最后更新时间（Unix 毫秒时间戳）
        /// </summary>
        public long? LastUpdated { get; set; }
    }
}
