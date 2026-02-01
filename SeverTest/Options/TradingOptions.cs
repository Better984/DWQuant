namespace ServerTest.Options
{
    /// <summary>
    /// 交易相关配置选项
    /// </summary>
    public class TradingOptions
    {
        /// <summary>
        /// 是否启用模拟盘（sandbox）模式
        /// 启用后，所有订单将发送到交易所的测试环境，不会使用真实资金
        /// </summary>
        public bool EnableSandboxMode { get; set; } = false;
    }
}
