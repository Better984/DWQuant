using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Options;
using System.Collections.Concurrent;

namespace ServerTest.Modules.Indicators.Application
{
    /// <summary>
    /// 指标定义注册中心：管理定义缓存与采集器路由。
    /// </summary>
    public sealed class IndicatorRegistry
    {
        private readonly IndicatorRepository _repository;
        private readonly IReadOnlyList<IIndicatorCollector> _collectors;
        private readonly IndicatorFrameworkOptions _options;
        private readonly ILogger<IndicatorRegistry> _logger;

        private readonly SemaphoreSlim _reloadLock = new(1, 1);
        private readonly ConcurrentDictionary<string, IndicatorDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
        private long _lastReloadMs;

        public IndicatorRegistry(
            IndicatorRepository repository,
            IEnumerable<IIndicatorCollector> collectors,
            IOptions<IndicatorFrameworkOptions> options,
            ILogger<IndicatorRegistry> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _collectors = collectors?.ToList() ?? throw new ArgumentNullException(nameof(collectors));
            _options = options?.Value ?? new IndicatorFrameworkOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IndicatorDefinition?> GetDefinitionAsync(string code, CancellationToken ct)
        {
            await EnsureLoadedAsync(forceReload: false, ct).ConfigureAwait(false);

            if (_definitions.TryGetValue(code, out var definition))
            {
                return definition;
            }

            return null;
        }

        public async Task<IReadOnlyList<IndicatorDefinition>> GetDefinitionsAsync(bool includeDisabled, CancellationToken ct)
        {
            await EnsureLoadedAsync(forceReload: false, ct).ConfigureAwait(false);

            var list = _definitions.Values
                .Where(definition => includeDisabled || definition.Enabled)
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }

        public IIndicatorCollector ResolveCollector(IndicatorDefinition definition)
        {
            var collector = _collectors.FirstOrDefault(item => item.CanHandle(definition));
            if (collector == null)
            {
                throw new InvalidOperationException($"未找到可处理指标 {definition.Code} 的采集器");
            }

            return collector;
        }

        public async Task ForceReloadAsync(CancellationToken ct)
        {
            await EnsureLoadedAsync(forceReload: true, ct).ConfigureAwait(false);
        }

        private async Task EnsureLoadedAsync(bool forceReload, CancellationToken ct)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!forceReload &&
                _definitions.Count > 0 &&
                nowMs - Interlocked.Read(ref _lastReloadMs) < _options.DefinitionReloadSeconds * 1000L)
            {
                return;
            }

            await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (!forceReload &&
                    _definitions.Count > 0 &&
                    nowMs - Interlocked.Read(ref _lastReloadMs) < _options.DefinitionReloadSeconds * 1000L)
                {
                    return;
                }

                var definitions = await _repository.GetAllDefinitionsAsync(ct).ConfigureAwait(false);
                var next = new ConcurrentDictionary<string, IndicatorDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var definition in definitions)
                {
                    if (!string.IsNullOrWhiteSpace(definition.Code))
                    {
                        next[definition.Code] = definition;
                    }
                }

                _definitions.Clear();
                foreach (var pair in next)
                {
                    _definitions[pair.Key] = pair.Value;
                }

                Interlocked.Exchange(ref _lastReloadMs, nowMs);
                _logger.LogInformation("指标定义已刷新: count={Count}", _definitions.Count);
            }
            finally
            {
                _reloadLock.Release();
            }
        }
    }
}
