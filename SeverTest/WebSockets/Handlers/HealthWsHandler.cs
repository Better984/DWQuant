namespace ServerTest.WebSockets.Handlers
{
    public sealed class HealthWsHandler : IWsMessageHandler
    {
        public string Type => "health";

        public Task<WsMessageEnvelope> HandleAsync(WebSocketConnection connection, WsMessageEnvelope envelope, CancellationToken ct)
        {
            var payload = new
            {
                serverTime = DateTime.UtcNow,
                connectionId = connection.ConnectionId,
                userId = connection.UserId,
                system = connection.System
            };

            return Task.FromResult(WsMessageEnvelope.Create("health", envelope.ReqId, payload, null));
        }
    }
}
