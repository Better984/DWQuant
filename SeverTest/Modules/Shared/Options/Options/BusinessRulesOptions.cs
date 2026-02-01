namespace ServerTest.Options
{
    /// <summary>
    /// 业务规则相关配置
    /// </summary>
    public sealed class BusinessRulesOptions
    {
        /// <summary>
        /// 超级管理员角色 ID
        /// </summary>
        public int SuperAdminRole { get; set; } = 255;

        /// <summary>
        /// 每个交易所最多绑定的 API Key 数量
        /// </summary>
        public int MaxKeysPerExchange { get; set; } = 5;

        /// <summary>
        /// 分享码长度
        /// </summary>
        public int ShareCodeLength { get; set; } = 8;

        /// <summary>
        /// 分享码字符集
        /// </summary>
        public string ShareCodeAlphabet { get; set; } = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    }
}
