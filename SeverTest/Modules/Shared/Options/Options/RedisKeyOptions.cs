namespace ServerTest.Options
{
    /// <summary>
    /// Redis Key 命名规范配置
    /// </summary>
    public sealed class RedisKeyOptions
    {
        /// <summary>
        /// 行情订阅用户集合 Key
        /// </summary>
        public string MarketSubUserSetKey { get; set; } = "market_sub:users";

        /// <summary>
        /// 行情订阅用户 Key 前缀
        /// </summary>
        public string MarketSubUserPrefix { get; set; } = "market_sub:user:";
    }
}
