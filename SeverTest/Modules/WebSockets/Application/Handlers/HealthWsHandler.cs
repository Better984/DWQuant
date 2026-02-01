using ServerTest.Protocol;

namespace ServerTest.WebSockets.Handlers
{
    public sealed class HealthWsHandler : IWsMessageHandler
    {
        public string Type => "health";

        public Task<ProtocolEnvelope<object>> HandleAsync(WebSocketConnection connection, ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            var payload = new
            {
                serverTime = DateTime.UtcNow,
                connectionId = connection.ConnectionId,
                userId = connection.UserId,
                system = connection.System
            };

            var response = ProtocolEnvelopeFactory.Ok<object>("health", envelope.ReqId, payload);
            return Task.FromResult(response);
        }
    }
}
