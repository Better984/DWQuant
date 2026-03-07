using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.Monitoring.Application
{
    /// <summary>
    /// 协议性能监控入库开关。
    /// 当前为启动时读取，修改 appsettings 后需重启服务生效。
    /// </summary>
    public sealed class ProtocolPerformanceStorageFeature
    {
        public const string DisabledMessage = "协议性能数据入库未启用";

        public ProtocolPerformanceStorageFeature(IOptions<MonitoringOptions> monitoringOptions)
        {
            if (monitoringOptions == null)
            {
                throw new ArgumentNullException(nameof(monitoringOptions));
            }

            IsEnabled = monitoringOptions.Value.EnableProtocolPerformanceStorage;
        }

        public bool IsEnabled { get; }
    }
}
