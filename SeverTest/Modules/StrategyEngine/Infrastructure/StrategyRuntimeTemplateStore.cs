using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    /// <summary>
    /// 运行时间模板缓存（由数据库加载）
    /// </summary>
    public sealed class StrategyRuntimeTemplateStore : IStrategyRuntimeTemplateProvider
    {
        private readonly StrategyRuntimeTemplateRepository _repository;
        private readonly ILogger<StrategyRuntimeTemplateStore> _logger;
        private IReadOnlyList<StrategyRuntimeTemplateDefinition> _templates = Array.Empty<StrategyRuntimeTemplateDefinition>();
        private IReadOnlyDictionary<string, StrategyRuntimeTemplateDefinition> _templateMap =
            new Dictionary<string, StrategyRuntimeTemplateDefinition>(StringComparer.OrdinalIgnoreCase);

        public StrategyRuntimeTemplateStore(
            StrategyRuntimeTemplateRepository repository,
            ILogger<StrategyRuntimeTemplateStore> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Timezones = BuildTimezones();
            ReloadSync();
        }

        public IReadOnlyList<StrategyRuntimeTemplateDefinition> Templates => _templates;

        public IReadOnlyList<StrategyRuntimeTimezoneOption> Timezones { get; }

        public bool TryGet(string id, out StrategyRuntimeTemplateDefinition template)
        {
            template = null!;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return _templateMap.TryGetValue(id.Trim(), out template!);
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            try
            {
                var templates = await _repository.GetAllAsync(ct).ConfigureAwait(false);
                ApplySnapshot(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新运行时间模板失败");
            }
        }

        private void ReloadSync()
        {
            try
            {
                var templates = _repository.GetAllAsync().GetAwaiter().GetResult();
                ApplySnapshot(templates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载运行时间模板失败");
            }
        }

        private void ApplySnapshot(IReadOnlyList<StrategyRuntimeTemplateDefinition> templates)
        {
            var list = templates?.Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToList()
                ?? new List<StrategyRuntimeTemplateDefinition>();
            var map = list.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            _templates = list;
            _templateMap = map;
        }

        private static IReadOnlyList<StrategyRuntimeTimezoneOption> BuildTimezones()
        {
            return new List<StrategyRuntimeTimezoneOption>
            {
                new StrategyRuntimeTimezoneOption { Value = "Asia/Shanghai", Label = "中国/上海 (UTC+8)" },
                new StrategyRuntimeTimezoneOption { Value = "America/New_York", Label = "美国/纽约 (UTC-5/-4)" },
                new StrategyRuntimeTimezoneOption { Value = "UTC", Label = "UTC" }
            };
        }
    }
}
