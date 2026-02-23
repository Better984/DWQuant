using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Options;

namespace ServerTest.Modules.Indicators.Application
{
    /// <summary>
    /// 指标后台刷新任务。
    /// </summary>
    public sealed class IndicatorRefreshHostedService : BackgroundService
    {
        private readonly IndicatorQueryService _queryService;
        private readonly IndicatorFrameworkOptions _options;
        private readonly ILogger<IndicatorRefreshHostedService> _logger;

        public IndicatorRefreshHostedService(
            IndicatorQueryService queryService,
            IOptions<IndicatorFrameworkOptions> options,
            ILogger<IndicatorRefreshHostedService> logger)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _options = options?.Value ?? new IndicatorFrameworkOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("指标框架已禁用，后台刷新任务不启动");
                return;
            }

            _logger.LogInformation(
                "指标后台刷新任务启动: scanInterval={Interval}s",
                _options.RefreshScanIntervalSeconds);

            try
            {
                await _queryService.EnsureInitializedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "指标框架初始化失败，后台任务退出");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var definitions = await _queryService
                        .GetDefinitionsAsync(includeDisabled: false, stoppingToken)
                        .ConfigureAwait(false);

                    foreach (var definition in definitions)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        await RefreshDefinitionSafeAsync(definition, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "指标后台刷新循环异常，下个周期重试");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.RefreshScanIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("指标后台刷新任务已停止");
        }

        private async Task RefreshDefinitionSafeAsync(IndicatorDefinition definition, CancellationToken ct)
        {
            try
            {
                var scope = ParseScope(definition.DefaultScopeKey);
                var result = await _queryService
                    .GetLatestAsync(definition.Code, scope, allowStale: true, forceRefresh: false, ct)
                    .ConfigureAwait(false);

                if (result.Stale)
                {
                    _logger.LogDebug("指标返回过期快照（已触发异步刷新）: code={Code}, scope={Scope}", definition.Code, result.Snapshot.ScopeKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台刷新指标失败: code={Code}", definition.Code);
            }
        }

        private static IDictionary<string, string>? ParseScope(string? defaultScopeKey)
        {
            if (string.IsNullOrWhiteSpace(defaultScopeKey) ||
                string.Equals(defaultScopeKey, "global", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pairs = defaultScopeKey.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var segments = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (segments.Length == 2 && !string.IsNullOrWhiteSpace(segments[0]) && !string.IsNullOrWhiteSpace(segments[1]))
                {
                    map[segments[0]] = segments[1];
                }
            }

            return map.Count == 0 ? null : map;
        }
    }
}
