using System;

namespace ServerTest.Models.Config
{
    /// <summary>
    /// 服务器配置项（管理端展示/更新）
    /// </summary>
    public sealed class ServerConfigItem
    {
        /// <summary>
        /// 配置键（使用 : 分隔层级）
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 分类
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// 值类型（string/int/decimal/bool/json）
        /// </summary>
        public string ValueType { get; set; } = "string";

        /// <summary>
        /// 值（字符串存储）
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 说明（简短注释）
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 是否实时生效
        /// </summary>
        public bool IsRealtime { get; set; }

        /// <summary>
        /// 最近更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
