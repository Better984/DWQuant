using System;
using System.Globalization;
using System.Linq;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyEngine.Domain
{
    public enum StrategyRuntimeScheduleType
    {
        Always,
        Custom,
        Template
    }

    public enum StrategyRuntimeOutOfSessionPolicy
    {
        BlockEntryAllowExit,
        BlockAll
    }

    internal enum StrategyRuntimeCustomMode
    {
        Allow,
        Deny
    }

    public readonly struct RuntimeScheduleEvaluation
    {
        public RuntimeScheduleEvaluation(bool allowed, bool changed)
        {
            Allowed = allowed;
            Changed = changed;
        }

        public bool Allowed { get; }
        public bool Changed { get; }
    }

    public sealed class StrategyRuntimeSchedule
    {
        private readonly StrategyRuntimeScheduleType _scheduleType;
        private readonly StrategyRuntimeOutOfSessionPolicy _policy;
        private readonly StrategyRuntimeCustomMode _customMode;
        private readonly CompiledSchedule? _customSchedule;
        private readonly List<CompiledSchedule> _templateSchedules = new();
        private readonly IStrategyRuntimeTemplateProvider? _templateProvider;
        private long _lastMinute;
        private bool _hasCache;
        private bool _lastAllowed;

        public StrategyRuntimeSchedule(StrategyRuntimeConfig? config, IStrategyRuntimeTemplateProvider? templateProvider = null)
        {
            _templateProvider = templateProvider;
            if (config == null)
            {
                _scheduleType = StrategyRuntimeScheduleType.Always;
                _policy = StrategyRuntimeOutOfSessionPolicy.BlockEntryAllowExit;
                Summary = "全天运行";
                return;
            }

            _scheduleType = ParseScheduleType(config.ScheduleType);
            _policy = ParsePolicy(config.OutOfSessionPolicy);
            _customMode = ParseCustomMode(config.Custom?.Mode);

            if (_scheduleType == StrategyRuntimeScheduleType.Custom)
            {
                _customSchedule = BuildSchedule(
                    config.Custom,
                    requireHourPrecision: true,
                    out var error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Error = error;
                }
                Summary = BuildCustomSummary(config.Custom);
                return;
            }

            if (_scheduleType == StrategyRuntimeScheduleType.Template)
            {
                var hasTemplateIds = config.TemplateIds != null && config.TemplateIds.Count > 0;
                var hasLegacyTemplates = config.Templates != null && config.Templates.Count > 0;
                if (!hasTemplateIds && !hasLegacyTemplates)
                {
                    _scheduleType = StrategyRuntimeScheduleType.Always;
                    Summary = "全天运行";
                    return;
                }

                var templateNames = new List<string>();
                if (hasTemplateIds)
                {
                    if (_templateProvider == null)
                    {
                        Error = "模板列表未加载";
                    }
                    else
                    {
                        foreach (var templateId in config.TemplateIds)
                        {
                            if (string.IsNullOrWhiteSpace(templateId))
                            {
                                continue;
                            }

                            if (!_templateProvider.TryGet(templateId, out var template))
                            {
                                Error = $"未找到模板：{templateId}";
                                continue;
                            }

                            var schedule = BuildSchedule(template, requireHourPrecision: false, out var error);
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                Error = error;
                                continue;
                            }

                            if (schedule != null)
                            {
                                _templateSchedules.Add(schedule);
                                var name = string.IsNullOrWhiteSpace(template.Name) ? template.Id : template.Name;
                                templateNames.Add(name);
                            }
                        }
                    }
                }
                else if (hasLegacyTemplates)
                {
                    foreach (var template in config.Templates)
                    {
                        var schedule = BuildSchedule(template, requireHourPrecision: false, out var error);
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Error = error;
                            continue;
                        }

                        if (schedule != null)
                        {
                            _templateSchedules.Add(schedule);
                            if (!string.IsNullOrWhiteSpace(template.Name))
                            {
                                templateNames.Add(template.Name);
                            }
                        }
                    }
                }

                Summary = templateNames.Count == 0
                    ? "未选择模板"
                    : $"模板：{string.Join(" / ", templateNames.Distinct())}";
                if (_templateSchedules.Count == 0 && string.IsNullOrWhiteSpace(Error))
                {
                    Error = "模板配置为空";
                }
                return;
            }


            Summary = "全天运行";
        }

        public StrategyRuntimeOutOfSessionPolicy Policy => _policy;

        public StrategyRuntimeScheduleType ScheduleType => _scheduleType;

        public string Summary { get; } = string.Empty;

        public string? Error { get; }

        public RuntimeScheduleEvaluation Evaluate(DateTimeOffset utcTime)
        {
            var minuteKey = utcTime.ToUnixTimeMilliseconds() / 60000;
            if (_hasCache && minuteKey == _lastMinute)
            {
                return new RuntimeScheduleEvaluation(_lastAllowed, false);
            }

            var allowed = EvaluateInternal(utcTime);
            var changed = _hasCache && allowed != _lastAllowed;

            _lastMinute = minuteKey;
            _lastAllowed = allowed;
            _hasCache = true;

            return new RuntimeScheduleEvaluation(allowed, changed);
        }

        private bool EvaluateInternal(DateTimeOffset utcTime)
        {
            switch (_scheduleType)
            {
                case StrategyRuntimeScheduleType.Always:
                    return true;
                case StrategyRuntimeScheduleType.Custom:
                    if (_customSchedule == null)
                    {
                        return false;
                    }
                    var inCustom = _customSchedule.IsInSchedule(utcTime);
                    return _customMode == StrategyRuntimeCustomMode.Allow ? inCustom : !inCustom;
                case StrategyRuntimeScheduleType.Template:
                    if (_templateSchedules.Count == 0)
                    {
                        return false;
                    }
                    for (var i = 0; i < _templateSchedules.Count; i++)
                    {
                        if (_templateSchedules[i].IsInSchedule(utcTime))
                        {
                            return true;
                        }
                    }
                    return false;
                default:
                    return true;
            }
        }

        private static StrategyRuntimeScheduleType ParseScheduleType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return StrategyRuntimeScheduleType.Always;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "custom":
                    return StrategyRuntimeScheduleType.Custom;
                case "template":
                    return StrategyRuntimeScheduleType.Template;
                default:
                    return StrategyRuntimeScheduleType.Always;
            }
        }

        private static StrategyRuntimeOutOfSessionPolicy ParsePolicy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return StrategyRuntimeOutOfSessionPolicy.BlockEntryAllowExit;
            }

            return raw.Trim().Equals("BlockAll", StringComparison.OrdinalIgnoreCase)
                ? StrategyRuntimeOutOfSessionPolicy.BlockAll
                : StrategyRuntimeOutOfSessionPolicy.BlockEntryAllowExit;
        }

        private static StrategyRuntimeCustomMode ParseCustomMode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return StrategyRuntimeCustomMode.Deny;
            }

            return raw.Trim().Equals("Deny", StringComparison.OrdinalIgnoreCase)
                ? StrategyRuntimeCustomMode.Deny
                : StrategyRuntimeCustomMode.Allow;
        }

        private static string BuildCustomSummary(StrategyRuntimeCustomConfig? config)
        {
            if (config == null)
            {
                return "自定义时间";
            }

            var modeText = ParseCustomMode(config.Mode) == StrategyRuntimeCustomMode.Deny ? "不交易" : "交易";
            var daysText = config.Days == null || config.Days.Count == 0
                ? "未选择星期"
                : string.Join("、", config.Days);
            return $"自定义（{modeText}）：{daysText}";
        }

        private static CompiledSchedule? BuildSchedule(
            StrategyRuntimeCustomConfig? config,
            bool requireHourPrecision,
            out string? error)
        {
            error = null;
            if (config == null)
            {
                error = "自定义时间配置为空";
                return null;
            }

            var timezone = ResolveTimeZone(config.Timezone, out var tzError);
            if (!string.IsNullOrWhiteSpace(tzError))
            {
                error = tzError;
                return null;
            }

            var days = ParseDays(config.Days, out var dayError);
            if (!string.IsNullOrWhiteSpace(dayError))
            {
                error = dayError;
                return null;
            }

            var ranges = ParseRanges(config.TimeRanges, requireHourPrecision, out var rangeError);
            if (!string.IsNullOrWhiteSpace(rangeError))
            {
                error = rangeError;
                return null;
            }

            return new CompiledSchedule(config.Timezone, timezone, days, ranges, config.TimeRanges, new Dictionary<DateOnly, CalendarException>());
        }

        private static CompiledSchedule? BuildSchedule(
            StrategyRuntimeTemplateConfig? config,
            bool requireHourPrecision,
            out string? error)
        {
            error = null;
            if (config == null)
            {
                error = "模板时间配置为空";
                return null;
            }

            var timezone = ResolveTimeZone(config.Timezone, out var tzError);
            if (!string.IsNullOrWhiteSpace(tzError))
            {
                error = tzError;
                return null;
            }

            var days = ParseDays(config.Days, out var dayError);
            if (!string.IsNullOrWhiteSpace(dayError))
            {
                error = dayError;
                return null;
            }

            var ranges = ParseRanges(config.TimeRanges, requireHourPrecision, out var rangeError);
            if (!string.IsNullOrWhiteSpace(rangeError))
            {
                error = rangeError;
                return null;
            }

            return new CompiledSchedule(config.Timezone, timezone, days, ranges, config.TimeRanges, new Dictionary<DateOnly, CalendarException>());
        }

        private static CompiledSchedule? BuildSchedule(
            StrategyRuntimeTemplateDefinition? config,
            bool requireHourPrecision,
            out string? error)
        {
            error = null;
            if (config == null)
            {
                error = "模板配置为空";
                return null;
            }

            var timezone = ResolveTimeZone(config.Timezone, out var tzError);
            if (!string.IsNullOrWhiteSpace(tzError))
            {
                error = tzError;
                return null;
            }

            var days = ParseDays(config.Days, out var dayError);
            if (!string.IsNullOrWhiteSpace(dayError))
            {
                error = dayError;
                return null;
            }

            var ranges = ParseRanges(config.TimeRanges, requireHourPrecision, out var rangeError);
            if (!string.IsNullOrWhiteSpace(rangeError))
            {
                error = rangeError;
                return null;
            }

            var calendar = ParseCalendar(config.Calendar, out var calendarError);
            if (!string.IsNullOrWhiteSpace(calendarError))
            {
                error = calendarError;
                return null;
            }

            return new CompiledSchedule(config.Timezone, timezone, days, ranges, config.TimeRanges, calendar);
        }

        private static TimeZoneInfo ResolveTimeZone(string? timezoneId, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(timezoneId))
            {
                return TimeZoneInfo.Utc;
            }

            var trimmed = timezoneId.Trim();
            if (string.Equals(trimmed, "local", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.Local;
            }

            if (TryFindTimeZone(trimmed, out var tz))
            {
                return tz;
            }

            if (TimeZoneMap.TryGetValue(trimmed, out var mapped) && TryFindTimeZone(mapped, out tz))
            {
                return tz;
            }

            error = $"未知时区：{timezoneId}";
            return TimeZoneInfo.Utc;
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

        private static HashSet<int> ParseDays(List<string>? raw, out string? error)
        {
            error = null;
            var result = new HashSet<int>();
            if (raw == null || raw.Count == 0)
            {
                error = "未选择星期";
                return result;
            }

            foreach (var item in raw)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                if (TryParseDay(item, out var day))
                {
                    result.Add(day);
                }
            }

            if (result.Count == 0)
            {
                error = "未选择有效星期";
            }

            return result;
        }

        private static bool TryParseDay(string raw, out int day)
        {
            day = 0;
            var value = raw.Trim().ToLowerInvariant();
            switch (value)
            {
                case "mon":
                case "monday":
                case "周一":
                case "星期一":
                case "1":
                    day = (int)DayOfWeek.Monday;
                    return true;
                case "tue":
                case "tues":
                case "tuesday":
                case "周二":
                case "星期二":
                case "2":
                    day = (int)DayOfWeek.Tuesday;
                    return true;
                case "wed":
                case "weds":
                case "wednesday":
                case "周三":
                case "星期三":
                case "3":
                    day = (int)DayOfWeek.Wednesday;
                    return true;
                case "thu":
                case "thur":
                case "thurs":
                case "thursday":
                case "周四":
                case "星期四":
                case "4":
                    day = (int)DayOfWeek.Thursday;
                    return true;
                case "fri":
                case "friday":
                case "周五":
                case "星期五":
                case "5":
                    day = (int)DayOfWeek.Friday;
                    return true;
                case "sat":
                case "saturday":
                case "周六":
                case "星期六":
                case "6":
                    day = (int)DayOfWeek.Saturday;
                    return true;
                case "sun":
                case "sunday":
                case "周日":
                case "星期日":
                case "星期天":
                case "0":
                case "7":
                    day = (int)DayOfWeek.Sunday;
                    return true;
                default:
                    return false;
            }
        }

        private enum CalendarExceptionType
        {
            Closed,
            Override,
            Append
        }

        private sealed class CalendarException
        {
            public CalendarException(CalendarExceptionType type, List<TimeRange> ranges)
            {
                Type = type;
                Ranges = ranges;
            }

            public CalendarExceptionType Type { get; }

            public List<TimeRange> Ranges { get; }
        }

        private static Dictionary<DateOnly, CalendarException> ParseCalendar(
            List<StrategyRuntimeCalendarException>? calendar,
            out string? error)
        {
            error = null;
            var result = new Dictionary<DateOnly, CalendarException>();
            if (calendar == null || calendar.Count == 0)
            {
                return result;
            }

            foreach (var exception in calendar)
            {
                if (exception == null)
                {
                    continue;
                }

                if (!DateOnly.TryParseExact(
                        exception.Date?.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    error = $"日历日期格式错误：{exception.Date}";
                    return result;
                }

                var type = ParseCalendarExceptionType(exception.Type, out var typeError);
                if (!string.IsNullOrWhiteSpace(typeError))
                {
                    error = typeError;
                    return result;
                }

                List<TimeRange> ranges = new();
                if (type != CalendarExceptionType.Closed)
                {
                    ranges = ParseRanges(exception.TimeRanges, requireHourPrecision: false, out var rangeError);
                    if (!string.IsNullOrWhiteSpace(rangeError))
                    {
                        error = rangeError;
                        return result;
                    }
                }

                if (result.ContainsKey(date))
                {
                    error = $"日历日期重复：{exception.Date}";
                    return result;
                }

                result[date] = new CalendarException(type, ranges);
            }

            return result;
        }

        private static CalendarExceptionType ParseCalendarExceptionType(string? raw, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return CalendarExceptionType.Closed;
            }

            var value = raw.Trim();
            if (value.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            {
                return CalendarExceptionType.Closed;
            }

            if (value.Equals("Override", StringComparison.OrdinalIgnoreCase))
            {
                return CalendarExceptionType.Override;
            }

            if (value.Equals("Append", StringComparison.OrdinalIgnoreCase))
            {
                return CalendarExceptionType.Append;
            }

            error = $"未知日历异常类型：{raw}";
            return CalendarExceptionType.Closed;
        }

        private static List<TimeRange> ParseRanges(
            List<StrategyRuntimeTimeRange>? ranges,
            bool requireHourPrecision,
            out string? error)
        {
            error = null;
            var result = new List<TimeRange>();
            if (ranges == null || ranges.Count == 0)
            {
                error = "未配置时间段";
                return result;
            }

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range == null)
                {
                    continue;
                }

                if (!TryParseTime(range.Start, out var start))
                {
                    error = $"开始时间格式错误：{range.Start}";
                    return result;
                }

                if (!TryParseTime(range.End, out var end))
                {
                    error = $"结束时间格式错误：{range.End}";
                    return result;
                }

                if (requireHourPrecision && (start.Minutes != 0 || end.Minutes != 0))
                {
                    error = "自定义时间段必须按整点配置";
                    return result;
                }

                var duration = GetDuration(start, end);
                var minDuration = requireHourPrecision ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1);
                if (duration < minDuration)
                {
                    error = requireHourPrecision ? "单个时间段至少 1 小时" : "单个时间段至少 1 分钟";
                    return result;
                }
                result.Add(new TimeRange(start, end));
            }

            return result;
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

        private sealed class CompiledSchedule
        {
            public CompiledSchedule(
                string timezoneId,
                TimeZoneInfo timezone,
                HashSet<int> enabledDays,
                List<TimeRange> ranges,
                List<StrategyRuntimeTimeRange> originalRanges,
                Dictionary<DateOnly, CalendarException> calendar)
            {
                TimezoneId = timezoneId;
                Timezone = timezone;
                EnabledDays = enabledDays;
                Ranges = ranges;
                OriginalRanges = originalRanges;
                Calendar = calendar;
            }

            public string TimezoneId { get; }

            public TimeZoneInfo Timezone { get; }

            public HashSet<int> EnabledDays { get; }

            public List<TimeRange> Ranges { get; }

            public List<StrategyRuntimeTimeRange> OriginalRanges { get; }

            public Dictionary<DateOnly, CalendarException> Calendar { get; }

            public bool IsInSchedule(DateTimeOffset utcTime)
            {
                var local = TimeZoneInfo.ConvertTime(utcTime, Timezone);
                var day = (int)local.DayOfWeek;
                var time = local.TimeOfDay;
                var baseAllowed = EnabledDays.Contains(day) && IsInRanges(time, Ranges);

                if (Calendar.Count == 0)
                {
                    return baseAllowed;
                }

                var date = DateOnly.FromDateTime(local.DateTime);
                if (!Calendar.TryGetValue(date, out var exception))
                {
                    return baseAllowed;
                }

                switch (exception.Type)
                {
                    case CalendarExceptionType.Closed:
                        return false;
                    case CalendarExceptionType.Override:
                        return IsInRanges(time, exception.Ranges);
                    case CalendarExceptionType.Append:
                        return baseAllowed || IsInRanges(time, exception.Ranges);
                    default:
                        return baseAllowed;
                }
            }

            private static bool IsInRanges(TimeSpan time, List<TimeRange> ranges)
            {
                for (var i = 0; i < ranges.Count; i++)
                {
                    if (ranges[i].Contains(time))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private readonly struct TimeRange
        {
            public TimeRange(TimeSpan start, TimeSpan end)
            {
                Start = start;
                End = end;
            }

            public TimeSpan Start { get; }

            public TimeSpan End { get; }

            public bool Contains(TimeSpan time)
            {
                if (Start == End)
                {
                    return true;
                }

                if (Start < End)
                {
                    return time >= Start && time <= End;
                }

                return time >= Start || time <= End;
            }
        }

        // IANA -> Windows 时区映射（仅覆盖当前模板与常用时区）
        private static readonly Dictionary<string, string> TimeZoneMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Shanghai", "China Standard Time" },
            { "America/New_York", "Eastern Standard Time" },
            { "UTC", "UTC" }
        };
    }
}
