namespace ServerTest.Modules.TradingExecution.Domain
{
    /// <summary>
    /// 开仓尝试事件类型。
    /// </summary>
    public static class OrderOpenAttemptTypes
    {
        /// <summary>
        /// 开仓执行结果（成功或下单失败）。
        /// </summary>
        public const string OrderResult = "order_result";

        /// <summary>
        /// 因最大持仓限制被阻断。
        /// </summary>
        public const string BlockedByMaxPosition = "blocked_max_position";
    }
}
