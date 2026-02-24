using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Services;

namespace ServerTest.Modules.Monitoring.Application
{
    /// <summary>
    /// 空实现：用于无桌面环境（Linux/K8s/Windows Service）下安全运行。
    /// </summary>
    public sealed class NoopStartupMonitorHost : IStartupMonitorHost
    {
        public void Start(SystemStartupManager startupManager)
        {
            // 无 UI 模式下不执行任何操作。
        }

        public void AppendLog(StartupMonitorLogEntry entry)
        {
            // 无 UI 模式下不缓存日志，避免引入额外内存占用。
        }
    }
}
