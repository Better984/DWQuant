using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerTest.Services
{
    /// <summary>
    /// 系统启动状态枚举
    /// </summary>
    public enum SystemStatus
    {
        /// <summary>未启动</summary>
        NotStarted = 0,
        /// <summary>启动中</summary>
        Starting = 1,
        /// <summary>已就绪</summary>
        Ready = 2,
        /// <summary>启动失败</summary>
        Failed = 3
    }

    /// <summary>
    /// 系统模块枚举
    /// </summary>
    public enum SystemModule
    {
        /// <summary>基础设施（Redis、数据库等）</summary>
        Infrastructure = 0,
        /// <summary>网络层（HTTP API + WebSocket）</summary>
        Network = 1,
        /// <summary>行情数据引擎</summary>
        MarketDataEngine = 2,
        /// <summary>指标引擎</summary>
        IndicatorEngine = 3,
        /// <summary>策略引擎</summary>
        StrategyEngine = 4,
        /// <summary>实盘交易系统（整体）</summary>
        TradingSystem = 5
    }

    /// <summary>
    /// 系统启动管理器：管理各个系统模块的启动状态，确保启动顺序正确
    /// </summary>
    public sealed class SystemStartupManager
    {
        private readonly ILogger<SystemStartupManager> _logger;
        private readonly ConcurrentDictionary<SystemModule, SystemStatus> _statuses = new();
        private readonly ConcurrentDictionary<SystemModule, string> _errorMessages = new();
        private readonly ConcurrentDictionary<SystemModule, string> _descriptions = new();

        public event EventHandler<SystemStatusChangedEventArgs>? StatusChanged;

        public SystemStartupManager(ILogger<SystemStartupManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // 初始化所有模块状态为未启动
            foreach (SystemModule module in Enum.GetValues<SystemModule>())
            {
                _statuses[module] = SystemStatus.NotStarted;
            }
        }

        /// <summary>
        /// 标记模块开始启动
        /// </summary>
        public void MarkStarting(SystemModule module, string? description = null)
        {
            _statuses[module] = SystemStatus.Starting;
            var desc = description ?? module.ToString();
            _descriptions[module] = desc;
            StatusChanged?.Invoke(this, new SystemStatusChangedEventArgs(module, SystemStatus.Ready, desc, null));
            _descriptions[module] = desc;
            StatusChanged?.Invoke(this, new SystemStatusChangedEventArgs(module, SystemStatus.Starting, desc, null));
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("🚀 [{Module}] 开始启动: {Description}", module, desc);
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// 标记模块启动成功
        /// </summary>
        public void MarkReady(SystemModule module, string? description = null)
        {
            _statuses[module] = SystemStatus.Ready;
            _errorMessages.TryRemove(module, out _);
            var desc = description ?? module.ToString();
            _logger.LogInformation("✅ [{Module}] 启动成功: {Description}", module, desc);
        }

        /// <summary>
        /// 标记模块启动失败
        /// </summary>
        public void MarkFailed(SystemModule module, string errorMessage)
        {
            _statuses[module] = SystemStatus.Failed;
            _errorMessages[module] = errorMessage;
            StatusChanged?.Invoke(this, new SystemStatusChangedEventArgs(module, SystemStatus.Failed, _descriptions.TryGetValue(module, out var desc) ? desc : null, errorMessage));
            _logger.LogError("❌ [{Module}] 启动失败: {Error}", module, errorMessage);
        }

        /// <summary>
        /// 获取模块状态
        /// </summary>
        public SystemStatus GetStatus(SystemModule module)
        {
            return _statuses.TryGetValue(module, out var status) ? status : SystemStatus.NotStarted;
        }

        /// <summary>
        /// 获取模块错误信息
        /// </summary>
        public string? GetErrorMessage(SystemModule module)
        {
            return _errorMessages.TryGetValue(module, out var error) ? error : null;
        }

        /// <summary>
        /// 检查模块是否就绪
        /// </summary>
        public bool IsReady(SystemModule module)
        {
            return GetStatus(module) == SystemStatus.Ready;
        }

        /// <summary>
        /// 检查模块是否启动中
        /// </summary>
        public bool IsStarting(SystemModule module)
        {
            return GetStatus(module) == SystemStatus.Starting;
        }

        /// <summary>
        /// 检查模块是否失败
        /// </summary>
        public bool IsFailed(SystemModule module)
        {
            return GetStatus(module) == SystemStatus.Failed;
        }

        /// <summary>
        /// 等待模块就绪（带超时）
        /// </summary>
        public async Task<bool> WaitForReadyAsync(SystemModule module, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                var status = GetStatus(module);
                if (status == SystemStatus.Ready)
                {
                    return true;
                }

                if (status == SystemStatus.Failed)
                {
                    _logger.LogWarning("[{Module}] 模块启动失败，无法继续等待", module);
                    return false;
                }

                await Task.Delay(100, cancellationToken);
            }

            _logger.LogWarning("[{Module}] 等待模块就绪超时 ({Timeout}秒)", module, timeout.TotalSeconds);
            return false;
        }

        /// <summary>
        /// 检查关键系统是否就绪（用于阻断请求）
        /// </summary>
        public bool AreCriticalSystemsReady()
        {
            // 检查基础设施和实盘交易系统是否就绪
            var infrastructureReady = IsReady(SystemModule.Infrastructure);
            var tradingSystemReady = IsReady(SystemModule.TradingSystem);

            if (!infrastructureReady)
            {
                _logger.LogWarning("⚠️ 基础设施未就绪，无法处理请求");
                return false;
            }

            if (!tradingSystemReady)
            {
                _logger.LogWarning("⚠️ 实盘交易系统未就绪，无法处理交易相关请求");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 获取所有模块状态摘要
        /// </summary>
        public Dictionary<SystemModule, (SystemStatus Status, string? Error)> GetAllStatuses()
        {
            var result = new Dictionary<SystemModule, (SystemStatus, string?)>();
            foreach (SystemModule module in Enum.GetValues<SystemModule>())
            {
                result[module] = (GetStatus(module), GetErrorMessage(module));
            }
            return result;
        }

        /// <summary>
        /// 打印启动状态摘要
        /// </summary>
        public void PrintStatusSummary()
        {
            _logger.LogInformation("");
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("📊 系统启动状态摘要");
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            
            foreach (SystemModule module in Enum.GetValues<SystemModule>())
            {
                var status = GetStatus(module);
                var statusIcon = status switch
                {
                    SystemStatus.Ready => "✅",
                    SystemStatus.Starting => "⏳",
                    SystemStatus.Failed => "❌",
                    _ => "⚪"
                };
                
                var statusText = status switch
                {
                    SystemStatus.Ready => "就绪",
                    SystemStatus.Starting => "启动中",
                    SystemStatus.Failed => "失败",
                    _ => "未启动"
                };

                var error = GetErrorMessage(module);
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogInformation("{Icon} [{Module}] {Status} - {Error}", statusIcon, module, statusText, error);
                }
                else
                {
                    _logger.LogInformation("{Icon} [{Module}] {Status}", statusIcon, module, statusText);
                }
            }
            
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("");
        }
    }

    public sealed class SystemStatusChangedEventArgs : EventArgs
    {
        public SystemStatusChangedEventArgs(SystemModule module, SystemStatus status, string? description, string? errorMessage)
        {
            Module = module;
            Status = status;
            Description = description;
            ErrorMessage = errorMessage;
        }

        public SystemModule Module { get; }
        public SystemStatus Status { get; }
        public string? Description { get; }
        public string? ErrorMessage { get; }
    }
}
