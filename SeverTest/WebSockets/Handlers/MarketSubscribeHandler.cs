using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServerTest.WebSockets.Contracts;
using ServerTest.WebSockets.Subscriptions;

namespace ServerTest.WebSockets.Handlers
{
    public sealed class MarketSubscribeHandler : IWsMessageHandler
    {
        private readonly IMarketSubscriptionStore _store;

        public MarketSubscribeHandler(IMarketSubscriptionStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string Type => "market.subscribe";

        public Task<WsMessageEnvelope> HandleAsync(WebSocketConnection connection, WsMessageEnvelope envelope, CancellationToken ct)
        {
            var payload = ParsePayload<MarketSubscribeRequest>(envelope.Payload);
            if (payload?.Symbols == null || payload.Symbols.Length == 0)
            {
                return Task.FromResult(WsMessageEnvelope.Error(envelope.ReqId, "bad_request", "symbols are required"));
            }

            var symbols = payload.Symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (symbols.Length == 0)
            {
                return Task.FromResult(WsMessageEnvelope.Error(envelope.ReqId, "bad_request", "symbols are required"));
            }

            _store.Subscribe(connection.UserId, symbols);

            var responsePayload = new
            {
                success = true,
                symbols
            };

            return Task.FromResult(WsMessageEnvelope.Create("market.subscribe.ack", envelope.ReqId, responsePayload, null));
        }

        private static T? ParsePayload<T>(object? payload)
        {
            if (payload == null)
            {
                return default;
            }

            if (payload is JObject obj)
            {
                return obj.ToObject<T>();
            }

            if (payload is JToken token)
            {
                return token.ToObject<T>();
            }

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(payload));
        }
    }
}
