using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    /// <summary>
    /// 运行时间模板数据访问
    /// </summary>
    public sealed class StrategyRuntimeTemplateRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly IDbManager _db;
        private readonly ILogger<StrategyRuntimeTemplateRepository> _logger;

        private sealed class TemplateRow
        {
            public string TemplateId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Timezone { get; set; } = string.Empty;
            public string DaysJson { get; set; } = string.Empty;
            public string TimeRangesJson { get; set; } = string.Empty;
            public string? CalendarJson { get; set; }
        }

        public StrategyRuntimeTemplateRepository(IDbManager db, ILogger<StrategyRuntimeTemplateRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<StrategyRuntimeTemplateDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  template_id AS TemplateId,
  name AS Name,
  timezone AS Timezone,
  days_json AS DaysJson,
  time_ranges_json AS TimeRangesJson,
  calendar_json AS CalendarJson
FROM strategy_runtime_template
ORDER BY template_id
";

            var rows = await _db.QueryAsync<TemplateRow>(sql, null, null, ct).ConfigureAwait(false);
            var result = new List<StrategyRuntimeTemplateDefinition>();
            foreach (var row in rows)
            {
                var template = MapRow(row);
                if (template != null)
                {
                    result.Add(template);
                }
            }

            return result;
        }

        public async Task<bool> ExistsAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            const string sql = @"
SELECT 1
FROM strategy_runtime_template
WHERE template_id = @templateId
LIMIT 1
";

            var value = await _db.ExecuteScalarAsync<int?>(sql, new { templateId }, null, ct).ConfigureAwait(false);
            return value.HasValue;
        }

        public Task<int> InsertAsync(StrategyRuntimeTemplateDefinition template, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO strategy_runtime_template
  (template_id, name, timezone, days_json, time_ranges_json, calendar_json, created_at, updated_at)
VALUES
  (@templateId, @name, @timezone, @daysJson, @timeRangesJson, @calendarJson, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
";

            var param = BuildSqlParams(template);
            return _db.ExecuteAsync(sql, param, null, ct);
        }

        public Task<int> UpdateAsync(StrategyRuntimeTemplateDefinition template, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE strategy_runtime_template
SET
  name = @name,
  timezone = @timezone,
  days_json = @daysJson,
  time_ranges_json = @timeRangesJson,
  calendar_json = @calendarJson,
  updated_at = CURRENT_TIMESTAMP
WHERE template_id = @templateId
";

            var param = BuildSqlParams(template);
            return _db.ExecuteAsync(sql, param, null, ct);
        }

        public Task<int> DeleteAsync(string templateId, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM strategy_runtime_template
WHERE template_id = @templateId
";

            return _db.ExecuteAsync(sql, new { templateId }, null, ct);
        }

        private object BuildSqlParams(StrategyRuntimeTemplateDefinition template)
        {
            return new
            {
                templateId = template.Id?.Trim() ?? string.Empty,
                name = template.Name?.Trim() ?? string.Empty,
                timezone = template.Timezone?.Trim() ?? "UTC",
                daysJson = Serialize(template.Days ?? new List<string>()),
                timeRangesJson = Serialize(template.TimeRanges ?? new List<StrategyRuntimeTimeRange>()),
                calendarJson = Serialize(template.Calendar ?? new List<StrategyRuntimeCalendarException>())
            };
        }

        private StrategyRuntimeTemplateDefinition? MapRow(TemplateRow row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.TemplateId))
            {
                return null;
            }

            return new StrategyRuntimeTemplateDefinition
            {
                Id = row.TemplateId.Trim(),
                Name = row.Name?.Trim() ?? string.Empty,
                Timezone = string.IsNullOrWhiteSpace(row.Timezone) ? "UTC" : row.Timezone.Trim(),
                Days = Deserialize<List<string>>(row.DaysJson) ?? new List<string>(),
                TimeRanges = Deserialize<List<StrategyRuntimeTimeRange>>(row.TimeRangesJson) ?? new List<StrategyRuntimeTimeRange>(),
                Calendar = Deserialize<List<StrategyRuntimeCalendarException>>(row.CalendarJson) ?? new List<StrategyRuntimeCalendarException>()
            };
        }

        private T? Deserialize<T>(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(raw, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "运行时间模板 JSON 解析失败");
                return default;
            }
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
    }
}
