using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Config;

namespace ServerTest.Infrastructure.Config
{
    /// <summary>
    /// 服务器配置缓存
    /// </summary>
    public sealed class ServerConfigStore
    {
        private readonly ServerConfigRepository _repository;
        private readonly ILogger<ServerConfigStore> _logger;
        private IReadOnlyDictionary<string, ServerConfigItem> _items =
            new Dictionary<string, ServerConfigItem>(StringComparer.OrdinalIgnoreCase);

        public ServerConfigStore(ServerConfigRepository repository, ILogger<ServerConfigStore> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryGet(string key, out ServerConfigItem item)
        {
            item = null!;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return _items.TryGetValue(key.Trim(), out item!);
        }

        public string GetString(string key, string fallback)
        {
            if (TryGet(key, out var item) && !string.IsNullOrWhiteSpace(item.Value))
            {
                return item.Value;
            }

            return fallback;
        }

        public int GetInt(string key, int fallback)
        {
            if (TryGet(key, out var item))
            {
                if (int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            if (TryGet(key, out var item))
            {
                if (bool.TryParse(item.Value, out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        public decimal GetDecimal(string key, decimal fallback)
        {
            if (TryGet(key, out var item))
            {
                if (decimal.TryParse(item.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            try
            {
                var list = await _repository.GetAllAsync(ct).ConfigureAwait(false);
                ApplySnapshot(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新服务器配置失败");
            }
        }

        private void ApplySnapshot(IReadOnlyList<ServerConfigItem> items)
        {
            var map = (items ?? Array.Empty<ServerConfigItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            _items = map;
        }
    }
}
