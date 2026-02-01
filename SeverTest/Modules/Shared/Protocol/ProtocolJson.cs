using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerTest.Protocol
{
    /// <summary>
    /// HTTP/WS 统一序列化配置
    /// </summary>
    public static class ProtocolJson
    {
        private static readonly JsonSerializerOptions SharedOptions = CreateOptions();

        public static JsonSerializerOptions Options => SharedOptions;

        public static void Apply(JsonSerializerOptions options)
        {
            // 统一输出为 camelCase，兼容大小写输入，并忽略空值
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
            {
                options.Converters.Add(new JsonStringEnumConverter());
            }
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            Apply(options);
            return options;
        }

        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, SharedOptions);
        }

        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, SharedOptions);
        }

        public static T? DeserializePayload<T>(object? payload)
        {
            if (payload == null)
            {
                return default;
            }

            if (payload is T typed)
            {
                return typed;
            }

            if (payload is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(element.GetRawText(), SharedOptions);
            }

            if (payload is string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(text, SharedOptions);
            }

            var serialized = JsonSerializer.Serialize(payload, SharedOptions);
            return JsonSerializer.Deserialize<T>(serialized, SharedOptions);
        }
    }
}
