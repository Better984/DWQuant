using System.Net.WebSockets;

namespace ServerTest.WebSockets
{
    public class WebSocketConnection
    {
        public WebSocketConnection(Guid connectionId, string userId, string system, WebSocket socket, DateTime connectedAt, string? remoteIp)
        {
            ConnectionId = connectionId;
            UserId = userId;
            System = system;
            Socket = socket;
            ConnectedAt = connectedAt;
            RemoteIp = remoteIp;
        }

        public Guid ConnectionId { get; }
        public string UserId { get; }
        public string System { get; }
        public WebSocket Socket { get; }
        public DateTime ConnectedAt { get; }
        public string? RemoteIp { get; }
    }
}
