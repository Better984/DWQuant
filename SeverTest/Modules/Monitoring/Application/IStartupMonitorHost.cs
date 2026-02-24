using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Services;

namespace ServerTest.Modules.Monitoring.Application
{
    /// <summary>
    /// 启动监控宿主抽象，用于隔离主服务与具体 UI 实现。
    /// </summary>
    public interface IStartupMonitorHost
    {
        void Start(SystemStartupManager startupManager);

        void AppendLog(StartupMonitorLogEntry entry);
    }
}
