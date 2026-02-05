using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Infrastructure;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// 运行时间模板管理服务
    /// </summary>
    public sealed class StrategyRuntimeTemplateService
    {
        private readonly StrategyRuntimeTemplateRepository _repository;
        private readonly StrategyRuntimeTemplateStore _store;
        private readonly ILogger<StrategyRuntimeTemplateService> _logger;

        public StrategyRuntimeTemplateService(
            StrategyRuntimeTemplateRepository repository,
            StrategyRuntimeTemplateStore store,
            ILogger<StrategyRuntimeTemplateService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<StrategyRuntimeTemplateDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            return _repository.GetAllAsync(ct);
        }

        public IReadOnlyList<StrategyRuntimeTimezoneOption> GetTimezones()
        {
            return _store.Timezones;
        }

        public async Task<(bool Success, string Error)> CreateAsync(StrategyRuntimeTemplateDefinition template, CancellationToken ct = default)
        {
            if (!TryValidateTemplate(template, out var error))
            {
                return (false, error);
            }

            if (await _repository.ExistsAsync(template.Id, ct).ConfigureAwait(false))
            {
                return (false, "模板 ID 已存在");
            }

            var affected = await _repository.InsertAsync(template, ct).ConfigureAwait(false);
            if (affected <= 0)
            {
                _logger.LogWarning("新增运行时间模板失败: {TemplateId}", template.Id);
                return (false, "新增模板失败");
            }

            await _store.ReloadAsync(ct).ConfigureAwait(false);
            return (true, string.Empty);
        }

        public async Task<(bool Success, string Error)> UpdateAsync(StrategyRuntimeTemplateDefinition template, CancellationToken ct = default)
        {
            if (!TryValidateTemplate(template, out var error))
            {
                return (false, error);
            }

            if (!await _repository.ExistsAsync(template.Id, ct).ConfigureAwait(false))
            {
                return (false, "模板不存在");
            }

            var affected = await _repository.UpdateAsync(template, ct).ConfigureAwait(false);
            if (affected <= 0)
            {
                _logger.LogWarning("更新运行时间模板失败: {TemplateId}", template.Id);
                return (false, "更新模板失败");
            }

            await _store.ReloadAsync(ct).ConfigureAwait(false);
            return (true, string.Empty);
        }

        public async Task<(bool Success, string Error)> DeleteAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return (false, "模板 ID 不能为空");
            }

            var affected = await _repository.DeleteAsync(templateId.Trim(), ct).ConfigureAwait(false);
            if (affected <= 0)
            {
                _logger.LogWarning("删除运行时间模板失败: {TemplateId}", templateId);
                return (false, "未找到可删除的模板");
            }

            await _store.ReloadAsync(ct).ConfigureAwait(false);
            return (true, string.Empty);
        }

        private bool TryValidateTemplate(StrategyRuntimeTemplateDefinition template, out string error)
        {
            error = string.Empty;
            if (template == null)
            {
                error = "模板为空";
                return false;
            }

            template.Id = template.Id?.Trim() ?? string.Empty;
            template.Name = template.Name?.Trim() ?? string.Empty;
            template.Timezone = string.IsNullOrWhiteSpace(template.Timezone) ? "UTC" : template.Timezone.Trim();

            if (string.IsNullOrWhiteSpace(template.Id))
            {
                error = "模板 ID 不能为空";
                return false;
            }

            if (template.Id.Length > 64)
            {
                error = "模板 ID 长度不能超过 64";
                return false;
            }

            if (string.IsNullOrWhiteSpace(template.Name))
            {
                error = "模板名称不能为空";
                return false;
            }

            if (template.Name.Length > 128)
            {
                error = "模板名称长度不能超过 128";
                return false;
            }

            if (!TryResolveTimeZone(template.Timezone, out _))
            {
                error = $"模板时区无效：{template.Timezone}";
                return false;
            }

            if (template.Days == null || template.Days.Count == 0)
            {
                error = "模板必须选择星期";
                return false;
            }

            foreach (var day in template.Days)
            {
                if (!TryParseDay(day))
                {
                    error = $"模板包含无效星期：{day}";
                    return false;
                }
            }

            if (template.TimeRanges == null || template.TimeRanges.Count == 0)
            {
                error = "模板必须配置时间段";
                return false;
            }

            foreach (var range in template.TimeRanges)
            {
                if (!TryParseTime(range?.Start, out var start))
                {
                    error = $"模板开始时间格式错误：{range?.Start}";
                    return false;
                }

                if (!TryParseTime(range?.End, out var end))
                {
                    error = $"模板结束时间格式错误：{range?.End}";
                    return false;
                }

                var duration = GetDuration(start, end);
                if (duration < TimeSpan.FromMinutes(1))
                {
                    error = "模板时间段最小为 1 分钟";
                    return false;
                }
            }

            if (template.Calendar == null || template.Calendar.Count == 0)
            {
                return true;
            }

            var seenDates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var calendar in template.Calendar)
            {
                if (calendar == null)
                {
                    continue;
                }

                if (!DateOnly.TryParseExact(
                        calendar.Date?.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    error = $"日历日期格式错误：{calendar.Date}";
                    return false;
                }

                var dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!seenDates.Add(dateKey))
                {
                    error = $"日历日期重复：{dateKey}";
                    return false;
                }

                var type = calendar.Type?.Trim() ?? "Closed";
                if (!IsCalendarType(type))
                {
                    error = $"日历类型无效：{calendar.Type}";
                    return false;
                }

                if (type.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (calendar.TimeRanges == null || calendar.TimeRanges.Count == 0)
                {
                    error = $"日历 {dateKey} 必须配置时间段";
                    return false;
                }

                foreach (var range in calendar.TimeRanges)
                {
                    if (!TryParseTime(range?.Start, out var start))
                    {
                        error = $"日历开始时间格式错误：{range?.Start}";
                        return false;
                    }

                    if (!TryParseTime(range?.End, out var end))
                    {
                        error = $"日历结束时间格式错误：{range?.End}";
                        return false;
                    }

                    var duration = GetDuration(start, end);
                    if (duration < TimeSpan.FromMinutes(1))
                    {
                        error = "日历时间段最小为 1 分钟";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsCalendarType(string type)
        {
            return type.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Override", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Append", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseTime(string? raw, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return TimeSpan.TryParseExact(
                raw.Trim(),
                new[] { "hh\\:mm", "h\\:mm" },
                CultureInfo.InvariantCulture,
                out time);
        }

        private static TimeSpan GetDuration(TimeSpan start, TimeSpan end)
        {
            if (start == end)
            {
                return TimeSpan.FromHours(24);
            }

            if (start < end)
            {
                return end - start;
            }

            return TimeSpan.FromHours(24) - start + end;
        }

        private static bool TryParseDay(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var value = raw.Trim().ToLowerInvariant();
            return value switch
            {
                "mon" or "monday" or "周一" or "星期一" or "1" => true,
                "tue" or "tues" or "tuesday" or "周二" or "星期二" or "2" => true,
                "wed" or "weds" or "wednesday" or "周三" or "星期三" or "3" => true,
                "thu" or "thur" or "thurs" or "thursday" or "周四" or "星期四" or "4" => true,
                "fri" or "friday" or "周五" or "星期五" or "5" => true,
                "sat" or "saturday" or "周六" or "星期六" or "6" => true,
                "sun" or "sunday" or "周日" or "星期日" or "星期天" or "0" or "7" => true,
                _ => false
            };
        }

        private static bool TryResolveTimeZone(string? timezoneId, out TimeZoneInfo zone)
        {
            zone = TimeZoneInfo.Utc;
            if (string.IsNullOrWhiteSpace(timezoneId))
            {
                return false;
            }

            var trimmed = timezoneId.Trim();
            if (string.Equals(trimmed, "local", StringComparison.OrdinalIgnoreCase))
            {
                zone = TimeZoneInfo.Local;
                return true;
            }

            if (TryFindTimeZone(trimmed, out zone))
            {
                return true;
            }

            if (TimeZoneMap.TryGetValue(trimmed, out var mapped) && TryFindTimeZone(mapped, out zone))
            {
                return true;
            }

            return false;
        }

        private static bool TryFindTimeZone(string timezoneId, out TimeZoneInfo zone)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                zone = TimeZoneInfo.Utc;
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                zone = TimeZoneInfo.Utc;
                return false;
            }
        }

        // IANA -> Windows 时区映射（覆盖当前模板常用时区）
        private static readonly Dictionary<string, string> TimeZoneMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Shanghai", "China Standard Time" },
            { "America/New_York", "Eastern Standard Time" },
            { "UTC", "UTC" }
        };
    }
}
