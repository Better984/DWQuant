using ServerTest.Protocol;

namespace ServerTest.WebSockets
{
    public interface IWsMessageHandler
    {
        string Type { get; }
        Task<ProtocolEnvelope<object>> HandleAsync(WebSocketConnection connection, ProtocolEnvelope<object> envelope, CancellationToken ct);
    }
}
