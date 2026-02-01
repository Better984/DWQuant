namespace ServerTest.Options
{
    /// <summary>
    /// 请求限制相关配置
    /// </summary>
    public sealed class RequestLimitsOptions
    {
        /// <summary>
        /// 默认最大请求体字节数
        /// </summary>
        public int DefaultMaxBodyBytes { get; set; } = 1024 * 1024;
    }
}
