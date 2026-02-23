using System.Net.WebSockets;
using System.Threading;

namespace ServerTest.WebSockets
{
    public class WebSocketConnection
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

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

        /// <summary>
        /// 同一连接的发送必须串行，避免并发 SendAsync 导致随机异常或断连。
        /// </summary>
        public async Task<bool> SendTextAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            if (Socket.State != WebSocketState.Open)
            {
                return false;
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open)
                {
                    return false;
                }

                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
