using System.Threading;
using System.Threading.Tasks;

namespace ServerTest.WebSockets
{
    public interface IWsMessageHandler
    {
        string Type { get; }
        Task<WsMessageEnvelope> HandleAsync(WebSocketConnection connection, WsMessageEnvelope envelope, CancellationToken ct);
    }
}
