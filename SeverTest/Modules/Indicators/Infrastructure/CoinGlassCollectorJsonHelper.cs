using System.Globalization;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass 指标采集器通用 JSON 读取辅助。
    /// </summary>
    internal static class CoinGlassCollectorJsonHelper
    {
        public static void ValidateResponseCode(JsonElement root, string moduleName)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var codeElement))
            {
                return;
            }

            var codeText = codeElement.ValueKind switch
            {
                JsonValueKind.String => codeElement.GetString(),
                JsonValueKind.Number when codeElement.TryGetInt64(out var numeric) => numeric.ToString(CultureInfo.InvariantCulture),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(codeText) || string.Equals(codeText, "0", StringComparison.Ordinal))
            {
                return;
            }

            var message = TryReadString(root, "msg", "message")
                ?? "未知错误";
            throw new InvalidOperationException($"CoinGlass {moduleName}接口返回错误: code={codeText}, msg={message}");
        }

        public static string? TryReadString(JsonElement obj, params string[] fieldNames)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (!obj.TryGetProperty(fieldName, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return value.ToString();
                }
            }

            return null;
        }

        public static decimal? TryReadDecimal(JsonElement obj, params string[] fieldNames)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (!obj.TryGetProperty(fieldName, out var value))
                {
                    continue;
                }

                var parsed = TryReadDecimal(value);
                if (parsed.HasValue)
                {
                    return parsed.Value;
                }
            }

            return null;
        }

        public static decimal? TryReadDecimal(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        public static long? TryReadLong(JsonElement obj, params string[] fieldNames)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (!obj.TryGetProperty(fieldName, out var value))
                {
                    continue;
                }

                var parsed = TryReadLong(value);
                if (parsed.HasValue)
                {
                    return parsed.Value;
                }
            }

            return null;
        }

        public static long? TryReadLong(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return NormalizeTimestamp(number);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return NormalizeTimestamp(parsed);
                    }

                    if (DateTimeOffset.TryParse(text, out var parsedDateTime))
                    {
                        return parsedDateTime.ToUnixTimeMilliseconds();
                    }
                }
            }

            return null;
        }

        public static List<long?> ReadTimestampArray(JsonElement arrayElement)
        {
            var result = new List<long?>();
            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in arrayElement.EnumerateArray())
            {
                result.Add(TryReadLong(item));
            }

            return result;
        }

        public static List<decimal?> ReadDecimalArray(JsonElement arrayElement)
        {
            var result = new List<decimal?>();
            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in arrayElement.EnumerateArray())
            {
                result.Add(TryReadDecimal(item));
            }

            return result;
        }

        public static JsonElement? TryReadArray(JsonElement obj, params string[] fieldNames)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (!obj.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                return value;
            }

            return null;
        }

        public static JsonElement? TryReadObject(JsonElement obj, params string[] fieldNames)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (!obj.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                return value;
            }

            return null;
        }

        public static IEnumerable<JsonElement> EnumerateCandidateObjects(JsonElement obj)
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            yield return obj;

            foreach (var property in obj.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    yield return property.Value;
                }
            }
        }

        public static long NormalizeTimestamp(long raw)
        {
            return raw > 0 && raw < 100_000_000_000L ? raw * 1000 : raw;
        }
    }
}
