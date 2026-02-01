using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class StrategyJsonLoader
    {
        private readonly JsonSerializerSettings _settings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            Converters = { new StrategyMethodJsonConverter(), new StringEnumConverter() }
        };

        public StrategyModel? LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            StrategyModel? strategy = null;
            try
            {
                var root = JObject.Parse(json);
                if (root["userStrategy"] != null && root["version"] != null)
                {
                    var document = root.ToObject<StrategyDocument>(JsonSerializer.Create(_settings));
                    strategy = BuildFromDocument(document);
                }
                else
                {
                    strategy = JsonConvert.DeserializeObject<StrategyModel>(json, _settings);
                }
            }
            catch (JsonReaderException)
            {
                return null;
            }

            if (strategy == null)
            {
                return null;
            }

            NormalizeStrategy(strategy);
            return strategy;
        }

        public StrategyModel? LoadFromDocument(StrategyDocument document)
        {
            var strategy = BuildFromDocument(document);
            if (strategy == null)
            {
                return null;
            }

            NormalizeStrategy(strategy);
            return strategy;
        }

        public StrategyConfig? ParseConfig(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<StrategyConfig>(json, _settings);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void NormalizeStrategy(StrategyModel strategy)
        {
            var trade = strategy.StrategyConfig?.Trade;
            if (trade == null)
            {
                return;
            }

            trade.Exchange = MarketDataKeyNormalizer.NormalizeExchange(trade.Exchange);
            trade.Symbol = MarketDataKeyNormalizer.NormalizeSymbol(trade.Symbol);
        }

        private static StrategyModel? BuildFromDocument(StrategyDocument? document)
        {
            if (document == null)
            {
                return null;
            }

            var user = document.UserStrategy ?? new StrategyUserStrategy();
            var definition = document.Definition ?? new StrategyDefinition();
            var version = document.Version ?? new StrategyVersion();

            var name = !string.IsNullOrWhiteSpace(user.AliasName) ? user.AliasName : definition.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Unnamed Strategy";
            }

            var description = !string.IsNullOrWhiteSpace(user.Description)
                ? user.Description
                : definition.Description;

            return new StrategyModel
            {
                Id = user.UsId,
                UidCode = BuildUidCode(user, definition),
                Name = name,
                Description = description ?? string.Empty,
                State = MapState(user.State),
                CreatorUserId = definition.CreatorUid != 0 ? definition.CreatorUid : user.Uid,
                ExchangeApiKeyId = user.ExchangeApiKeyId,
                Version = version.VersionNo > 0 ? version.VersionNo : 1,
                Visibility = new StrategyVisibility
                {
                    IsPublic = string.Equals(user.Visibility, "public_sale", StringComparison.OrdinalIgnoreCase),
                    PriceUsdt = user.PriceUsdt,
                    ShareCode = user.ShareCode
                },
                Source = new StrategySource
                {
                    Type = string.IsNullOrWhiteSpace(user.Source?.Type) ? "custom" : user.Source.Type,
                    SourceId = ParseSourceId(user.Source?.Ref),
                    SourceCreatorUserId = null
                },
                StrategyConfig = version.ConfigJson ?? new StrategyConfig(),
                Timestamps = new StrategyTimestamps
                {
                    CreatedAt = user.UpdatedAt == default ? DateTimeOffset.UtcNow : user.UpdatedAt,
                    UpdatedAt = user.UpdatedAt == default ? DateTimeOffset.UtcNow : user.UpdatedAt,
                    DeletedAt = null
                }
            };
        }

        private static string BuildUidCode(StrategyUserStrategy user, StrategyDefinition definition)
        {
            if (user.UsId > 0)
            {
                return user.UsId.ToString();
            }

            if (definition.DefId > 0)
            {
                return $"def_{definition.DefId}";
            }

            if (!string.IsNullOrWhiteSpace(user.ShareCode))
            {
                return $"share_{user.ShareCode}";
            }

            return "strategy";
        }

        private static StrategyState MapState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return StrategyState.Draft;
            }

            switch (state.Trim().ToLowerInvariant())
            {
                case "running":
                    return StrategyState.Running;
                case "testing":
                    return StrategyState.Testing;
                case "paused":
                    return StrategyState.Paused;
                case "paused_open_position":
                    return StrategyState.PausedOpenPosition;
                case "draft":
                    return StrategyState.Draft;
                case "ready":
                    return StrategyState.Draft;
                case "archived":
                    return StrategyState.Deleted;
                case "completed":
                    return StrategyState.Completed;
                default:
                    return StrategyState.Draft;
            }
        }

        private static long? ParseSourceId(string? sourceRef)
        {
            if (string.IsNullOrWhiteSpace(sourceRef))
            {
                return null;
            }

            if (long.TryParse(sourceRef, out var numeric))
            {
                return numeric;
            }

            var trimmed = sourceRef.Trim();
            var lastColon = trimmed.LastIndexOf(':');
            if (lastColon >= 0 && lastColon < trimmed.Length - 1)
            {
                var tail = trimmed[(lastColon + 1)..];
                if (long.TryParse(tail, out numeric))
                {
                    return numeric;
                }
            }

            return null;
        }
    }

    internal sealed class StrategyMethodJsonConverter : JsonConverter<StrategyMethod>
    {
        public override StrategyMethod? ReadJson(
            JsonReader reader,
            Type objectType,
            StrategyMethod? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            var result = new StrategyMethod();

            var argsToken = obj["args"];
            obj.Remove("args");

            serializer.Populate(obj.CreateReader(), result);

            if (argsToken is not JArray argsArray)
            {
                return result;
            }

            if (argsArray.Count == 0)
            {
                return result;
            }

            if (argsArray[0] is JObject)
            {
                result.Args = argsArray.ToObject<System.Collections.Generic.List<StrategyValueRef>>(serializer)
                              ?? new System.Collections.Generic.List<StrategyValueRef>();
                return result;
            }

            var param = new string[argsArray.Count];
            for (var i = 0; i < argsArray.Count; i++)
            {
                param[i] = argsArray[i]?.ToString() ?? string.Empty;
            }

            result.Param = param;
            return result;
        }

        public override void WriteJson(JsonWriter writer, StrategyMethod? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var obj = JObject.FromObject(value, serializer);
            if (value.Args != null && value.Args.Count > 0)
            {
                obj["args"] = JArray.FromObject(value.Args, serializer);
            }
            else if (value.Param != null && value.Param.Length > 0)
            {
                obj["args"] = JArray.FromObject(value.Param, serializer);
            }

            obj.WriteTo(writer);
        }
    }
}
