using System.Globalization;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public static class StrategyRuntimeConfigValidator
    {
        public static bool TryValidate(StrategyRuntimeConfig? config, IStrategyRuntimeTemplateProvider? provider, out string error)
        {
            error = string.Empty;
            if (config == null)
            {
                return true;
            }

            var scheduleType = (config.ScheduleType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(scheduleType) ||
                scheduleType.Equals("Always", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (scheduleType.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                return TryValidateCustom(config.Custom, out error);
            }

            if (scheduleType.Equals("Template", StringComparison.OrdinalIgnoreCase))
            {
                if (config.TemplateIds != null && config.TemplateIds.Count > 0)
                {
                    return TryValidateTemplateIds(config.TemplateIds, provider, out error);
                }

                if (config.Templates != null && config.Templates.Count > 0)
                {
                    return TryValidateTemplates(config.Templates, out error);
                }

                // 存量策略缺少模板配置时按全天运行处理
                return true;
            }

            return true;
        }

        private static bool TryValidateCustom(StrategyRuntimeCustomConfig? config, out string error)
        {
            error = string.Empty;
            if (config == null)
            {
                error = "自定义时间配置为空";
                return false;
            }

            if (!TryResolveTimeZone(config.Timezone, out _))
            {
                error = $"自定义时区无效：{config.Timezone}";
                return false;
            }

            if (config.Days == null || config.Days.Count == 0)
            {
                error = "自定义时间必须选择星期";
                return false;
            }

            foreach (var day in config.Days)
            {
                if (!TryParseDay(day))
                {
                    error = $"自定义时间包含无效星期：{day}";
                    return false;
                }
            }

            if (config.TimeRanges == null || config.TimeRanges.Count == 0)
            {
                error = "自定义时间必须配置时间段";
                return false;
            }

            foreach (var range in config.TimeRanges)
            {
                if (!TryParseTime(range?.Start, out var start))
                {
                    error = $"自定义开始时间格式错误：{range?.Start}";
                    return false;
                }

                if (!TryParseTime(range?.End, out var end))
                {
                    error = $"自定义结束时间格式错误：{range?.End}";
                    return false;
                }

                if (start.Minutes != 0 || end.Minutes != 0)
                {
                    error = "自定义时间段必须按整点配置";
                    return false;
                }

                var duration = GetDuration(start, end);
                if (duration < TimeSpan.FromHours(1))
                {
                    error = "自定义时间段最小为 1 小时";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateTemplates(
            List<StrategyRuntimeTemplateConfig>? templates,
            out string error)
        {
            error = string.Empty;
            if (templates == null || templates.Count == 0)
            {
                error = "请选择至少一个时间模板";
                return false;
            }

            foreach (var template in templates)
            {
                if (template == null)
                {
                    continue;
                }

                if (!TryResolveTimeZone(template.Timezone, out _))
                {
                    error = $"模板时区无效：{template.Timezone}";
                    return false;
                }

                if (template.Days == null || template.Days.Count == 0)
                {
                    error = $"模板 {template.Name} 必须选择星期";
                    return false;
                }

                foreach (var day in template.Days)
                {
                    if (!TryParseDay(day))
                    {
                        error = $"模板 {template.Name} 包含无效星期：{day}";
                        return false;
                    }
                }

                if (template.TimeRanges == null || template.TimeRanges.Count == 0)
                {
                    error = $"模板 {template.Name} 必须配置时间段";
                    return false;
                }

                foreach (var range in template.TimeRanges)
                {
                    if (!TryParseTime(range?.Start, out var start))
                    {
                        error = $"模板 {template.Name} 开始时间格式错误：{range?.Start}";
                        return false;
                    }

                    if (!TryParseTime(range?.End, out var end))
                    {
                        error = $"模板 {template.Name} 结束时间格式错误：{range?.End}";
                        return false;
                    }

                    var duration = GetDuration(start, end);
                    if (duration < TimeSpan.FromMinutes(1))
                    {
                        error = $"模板 {template.Name} 时间段无效";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryValidateTemplateIds(
            List<string> templateIds,
            IStrategyRuntimeTemplateProvider? provider,
            out string error)
        {
            error = string.Empty;
            if (templateIds == null || templateIds.Count == 0)
            {
                error = "请选择至少一个时间模板";
                return false;
            }

            if (provider == null)
            {
                error = "模板列表未加载";
                return false;
            }

            var hasValid = false;
            foreach (var templateId in templateIds)
            {
                if (string.IsNullOrWhiteSpace(templateId))
                {
                    continue;
                }

                if (!provider.TryGet(templateId, out _))
                {
                    error = $"未找到模板：{templateId}";
                    return false;
                }

                hasValid = true;
            }

            if (!hasValid)
            {
                error = "请选择至少一个时间模板";
                return false;
            }

            return true;
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

        private static readonly Dictionary<string, string> TimeZoneMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Shanghai", "China Standard Time" },
            { "America/New_York", "Eastern Standard Time" },
            { "UTC", "UTC" }
        };
    }
}
