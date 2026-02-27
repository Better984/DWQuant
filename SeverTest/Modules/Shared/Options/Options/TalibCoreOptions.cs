namespace ServerTest.Options
{
    /// <summary>
    /// TA 指标核心配置：
    /// TalibNet = 使用 TALib.NETCore；
    /// TalibWasmNode = 使用 Node + talib-web + talib.wasm（与前端同核心）。
    /// </summary>
    public sealed class TalibCoreOptions
    {
        /// <summary>
        /// 指标核心模式：TalibNet / TalibWasmNode
        /// </summary>
        public string Mode { get; set; } = "TalibWasmNode";

        /// <summary>
        /// Node 可执行文件路径，默认使用 PATH 中的 node。
        /// </summary>
        public string NodeExecutable { get; set; } = "node";

        /// <summary>
        /// Node 桥接脚本路径（可留空，程序会自动探测 Client/scripts/talib-node-bridge.mjs）。
        /// </summary>
        public string BridgeScriptPath { get; set; } = string.Empty;

        /// <summary>
        /// talib_web_api_meta.json 路径（可留空自动探测）。
        /// </summary>
        public string MetaPath { get; set; } = string.Empty;

        /// <summary>
        /// talib.wasm 路径（可留空自动探测）。
        /// </summary>
        public string WasmPath { get; set; } = string.Empty;

        /// <summary>
        /// 子进程启动与握手超时（毫秒）。
        /// </summary>
        public int StartupTimeoutMs { get; set; } = 15000;

        /// <summary>
        /// 单次计算请求超时（毫秒）。
        /// </summary>
        public int RequestTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// WASM Node 进程池大小。1 = 单进程（默认），大于 1 = 多进程并行计算指标。
        /// 建议值：CPU 核数 / 4，或 2~4。
        /// </summary>
        public int PoolSize { get; set; } = 1;

        /// <summary>
        /// 严格模式：同核心调用失败时不回退 TALib.NETCore，直接判失败。
        /// </summary>
        public bool StrictWasmCore { get; set; } = false;
    }
}
