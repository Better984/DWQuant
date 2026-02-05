using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Config;
using ServerTest.Models.Config;

namespace ServerTest.Services
{
    /// <summary>
    /// 服务器配置初始化（建表 + 默认值入库）
    /// </summary>
    public sealed class ServerConfigBootstrapHostedService : IHostedService
    {
        private readonly ServerConfigRepository _repository;
        private readonly ServerConfigStore _store;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServerConfigBootstrapHostedService> _logger;

        public ServerConfigBootstrapHostedService(
            ServerConfigRepository repository,
            ServerConfigStore store,
            IConfiguration configuration,
            ILogger<ServerConfigBootstrapHostedService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _repository.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

                var existing = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
                var existingMap = existing.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

                foreach (var definition in ServerConfigDefinitions.All)
                {
                    if (existingMap.TryGetValue(definition.Key, out var item))
                    {
                        // 同步注释/分类/实时属性，保留当前值
                        if (!IsMetaEqual(item, definition))
                        {
                            var updated = new ServerConfigItem
                            {
                                Key = definition.Key,
                                Category = definition.Category,
                                ValueType = definition.ValueType,
                                Value = item.Value,
                                Description = definition.Description,
                                IsRealtime = definition.IsRealtime
                            };
                            await _repository.UpsertAsync(updated, null, cancellationToken).ConfigureAwait(false);
                        }
                        continue;
                    }

                    var defaultValue = ResolveDefaultValue(definition);
                    var newItem = new ServerConfigItem
                    {
                        Key = definition.Key,
                        Category = definition.Category,
                        ValueType = definition.ValueType,
                        Value = defaultValue,
                        Description = definition.Description,
                        IsRealtime = definition.IsRealtime
                    };
                    await _repository.UpsertAsync(newItem, null, cancellationToken).ConfigureAwait(false);
                }

                await _store.ReloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化服务器配置失败");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private string ResolveDefaultValue(ServerConfigDefinition definition)
        {
            var raw = _configuration[definition.Key];
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }

            return definition.ValueType switch
            {
                "bool" => bool.FalseString.ToLowerInvariant(),
                "int" => "0",
                "decimal" => 0m.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }

        private static bool IsMetaEqual(ServerConfigItem item, ServerConfigDefinition definition)
        {
            return string.Equals(item.Category, definition.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ValueType, definition.ValueType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Description, definition.Description, StringComparison.OrdinalIgnoreCase)
                && item.IsRealtime == definition.IsRealtime;
        }
    }
}
