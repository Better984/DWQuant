using ServerTest.Modules.MarketStreaming.Infrastructure;
using ServerTest.WebSockets;
using ServerTest.WebSockets.Contracts;
using ServerTest.Protocol;

namespace ServerTest.Modules.MarketStreaming.Application
{
    public sealed class MarketUnsubscribeHandler : IWsMessageHandler
    {
        private readonly IMarketSubscriptionStore _store;

        public MarketUnsubscribeHandler(IMarketSubscriptionStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string Type => "market.unsubscribe";

        public Task<ProtocolEnvelope<object>> HandleAsync(WebSocketConnection connection, ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            var payload = ProtocolJson.DeserializePayload<MarketSubscribeRequest>(envelope.Data);
            if (payload?.Symbols == null || payload.Symbols.Length == 0)
            {
                return Task.FromResult(ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InvalidRequest, "订阅标的不能为空"));
            }

            var symbols = payload.Symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (symbols.Length == 0)
            {
                return Task.FromResult(ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InvalidRequest, "订阅标的不能为空"));
            }

            _store.Unsubscribe(connection.UserId, symbols);

            var responsePayload = new
            {
                success = true,
                symbols
            };

            return Task.FromResult(ProtocolEnvelopeFactory.Ok<object>("market.unsubscribe.ack", envelope.ReqId, responsePayload));
        }

    }
}
