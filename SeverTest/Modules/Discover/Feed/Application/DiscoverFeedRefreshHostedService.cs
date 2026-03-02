using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.Discover.Application
{
    /// <summary>
    /// Discover 资讯后台刷新任务。
    /// 按配置周期拉取上游新闻和快讯，并写入内存缓存与数据库。
    /// </summary>
    public sealed class DiscoverFeedRefreshHostedService : BackgroundService
    {
        private readonly DiscoverFeedService _discoverFeedService;
        private readonly IOptionsMonitor<DiscoverFeedOptions> _discoverOptionsMonitor;
        private readonly IOptionsMonitor<CoinGlassModuleSwitchOptions> _moduleSwitchMonitor;
        private readonly ILogger<DiscoverFeedRefreshHostedService> _logger;

        public DiscoverFeedRefreshHostedService(
            DiscoverFeedService discoverFeedService,
            IOptionsMonitor<DiscoverFeedOptions> discoverOptionsMonitor,
            IOptionsMonitor<CoinGlassModuleSwitchOptions> moduleSwitchMonitor,
            ILogger<DiscoverFeedRefreshHostedService> logger)
        {
            _discoverFeedService = discoverFeedService ?? throw new ArgumentNullException(nameof(discoverFeedService));
            _discoverOptionsMonitor = discoverOptionsMonitor ?? throw new ArgumentNullException(nameof(discoverOptionsMonitor));
            _moduleSwitchMonitor = moduleSwitchMonitor ?? throw new ArgumentNullException(nameof(moduleSwitchMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var lastEnabled = (bool?)null;
            var lastIntervalSeconds = -1;

            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _discoverOptionsMonitor.CurrentValue;
                var moduleSwitch = _moduleSwitchMonitor.CurrentValue;
                var enabled = options.Enabled && moduleSwitch.FeedEnabled;
                var intervalSeconds = Math.Max(1, options.PollIntervalSeconds);

                if (lastEnabled != enabled)
                {
                    _logger.LogInformation(
                        "Discover 资讯后台刷新状态变更：enabled={Enabled}",
                        enabled);
                    lastEnabled = enabled;
                }

                if (lastIntervalSeconds != intervalSeconds)
                {
                    _logger.LogInformation(
                        "Discover 资讯后台刷新间隔变更：interval={IntervalSeconds}s",
                        intervalSeconds);
                    lastIntervalSeconds = intervalSeconds;
                }

                if (enabled)
                {
                    try
                    {
                        await _discoverFeedService.RefreshOnceAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Discover 资讯后台刷新失败，本轮结束后重试");
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
