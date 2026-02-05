using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Config;
using ServerTest.Models.Config;

namespace ServerTest.Services
{
    /// <summary>
    /// 服务器配置管理服务（后台管理）
    /// </summary>
    public sealed class ServerConfigService
    {
        private readonly ServerConfigRepository _repository;
        private readonly ServerConfigStore _store;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServerConfigService> _logger;

        public ServerConfigService(
            ServerConfigRepository repository,
            ServerConfigStore store,
            IConfiguration configuration,
            ILogger<ServerConfigService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<ServerConfigItem>> ListAsync(CancellationToken ct = default)
        {
            return _repository.GetAllAsync(ct);
        }

        public async Task<(bool Success, string Error)> UpdateValueAsync(string key, string rawValue, long uid, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, "配置键不能为空");
            }

            var definition = ServerConfigDefinitions.All.FirstOrDefault(d =>
                d.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                return (false, "不支持的配置项");
            }

            if (!TryNormalizeValue(definition.ValueType, rawValue, out var normalized, out var error))
            {
                return (false, error);
            }

            var affected = await _repository.UpdateValueAsync(definition.Key, normalized, uid, ct).ConfigureAwait(false);
            if (affected <= 0)
            {
                return (false, "更新失败，未找到配置项");
            }

            await _store.ReloadAsync(ct).ConfigureAwait(false);
            ReloadConfiguration();

            return (true, string.Empty);
        }

        private void ReloadConfiguration()
        {
            if (_configuration is IConfigurationRoot root)
            {
                try
                {
                    root.Reload();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "刷新配置提供器失败");
                }
            }
        }

        private static bool TryNormalizeValue(string valueType, string rawValue, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;
            var trimmed = rawValue?.Trim() ?? string.Empty;

            switch (valueType?.ToLowerInvariant())
            {
                case "bool":
                    if (bool.TryParse(trimmed, out var boolValue))
                    {
                        normalized = boolValue.ToString().ToLowerInvariant();
                        return true;
                    }
                    if (trimmed == "1" || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = "true";
                        return true;
                    }
                    if (trimmed == "0" || trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = "false";
                        return true;
                    }
                    error = "布尔值仅支持 true/false/1/0";
                    return false;
                case "int":
                    if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        normalized = intValue.ToString(CultureInfo.InvariantCulture);
                        return true;
                    }
                    error = "整数格式错误";
                    return false;
                case "decimal":
                    if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                    {
                        normalized = decimalValue.ToString(CultureInfo.InvariantCulture);
                        return true;
                    }
                    error = "小数格式错误";
                    return false;
                case "json":
                    try
                    {
                        JsonDocument.Parse(string.IsNullOrWhiteSpace(trimmed) ? "{}" : trimmed);
                        normalized = trimmed;
                        return true;
                    }
                    catch
                    {
                        error = "JSON 格式错误";
                        return false;
                    }
                default:
                    normalized = trimmed;
                    return true;
            }
        }
    }
}
