using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.Discover.Application
{
    /// <summary>
    /// Discover 日历后台刷新任务。
    /// 按配置周期拉取上游三类日历，并写入内存缓存与数据库。
    /// </summary>
    public sealed class DiscoverCalendarRefreshHostedService : BackgroundService
    {
        private readonly DiscoverCalendarService _discoverCalendarService;
        private readonly IOptionsMonitor<DiscoverCalendarOptions> _calendarOptionsMonitor;
        private readonly IOptionsMonitor<CoinGlassModuleSwitchOptions> _moduleSwitchMonitor;
        private readonly ILogger<DiscoverCalendarRefreshHostedService> _logger;

        public DiscoverCalendarRefreshHostedService(
            DiscoverCalendarService discoverCalendarService,
            IOptionsMonitor<DiscoverCalendarOptions> calendarOptionsMonitor,
            IOptionsMonitor<CoinGlassModuleSwitchOptions> moduleSwitchMonitor,
            ILogger<DiscoverCalendarRefreshHostedService> logger)
        {
            _discoverCalendarService = discoverCalendarService ?? throw new ArgumentNullException(nameof(discoverCalendarService));
            _calendarOptionsMonitor = calendarOptionsMonitor ?? throw new ArgumentNullException(nameof(calendarOptionsMonitor));
            _moduleSwitchMonitor = moduleSwitchMonitor ?? throw new ArgumentNullException(nameof(moduleSwitchMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var lastEnabled = (bool?)null;
            var lastIntervalSeconds = -1;

            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _calendarOptionsMonitor.CurrentValue;
                var moduleSwitch = _moduleSwitchMonitor.CurrentValue;
                var enabled = options.Enabled && moduleSwitch.CalendarEnabled;
                var intervalSeconds = Math.Max(1, options.PollIntervalSeconds);

                if (lastEnabled != enabled)
                {
                    _logger.LogInformation(
                        "Discover 日历后台刷新状态变更：enabled={Enabled}",
                        enabled);
                    lastEnabled = enabled;
                }

                if (lastIntervalSeconds != intervalSeconds)
                {
                    _logger.LogInformation(
                        "Discover 日历后台刷新间隔变更：interval={IntervalSeconds}s",
                        intervalSeconds);
                    lastIntervalSeconds = intervalSeconds;
                }

                if (enabled)
                {
                    try
                    {
                        await _discoverCalendarService.RefreshOnceAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Discover 日历后台刷新失败，本轮结束后重试");
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
