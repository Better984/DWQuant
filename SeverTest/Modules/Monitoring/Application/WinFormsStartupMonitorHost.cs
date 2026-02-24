using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Services;
using System.Threading;

namespace ServerTest.Modules.Monitoring.Application
{
    /// <summary>
    /// WinForms 监控宿主占位实现。
    /// 主服务当前已移除 WinForms 编译依赖，此实现仅用于配置兼容与明确降级日志。
    /// </summary>
    public sealed class WinFormsStartupMonitorHost : IStartupMonitorHost
    {
        private int _warned;

        public void Start(SystemStartupManager startupManager)
        {
            if (Interlocked.Exchange(ref _warned, 1) != 0)
            {
                return;
            }

            Console.WriteLine(
                "[Monitoring] Monitoring.EnableDesktopHost 已开启，但当前主服务构建未包含 WinForms 监控窗体，已自动降级为无 UI 运行。");
        }

        public void AppendLog(StartupMonitorLogEntry entry)
        {
            // 占位实现：当前构建不处理桌面监控日志落地。
        }
    }
}
