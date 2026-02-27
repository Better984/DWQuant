namespace ServerTest.WebSockets
{
    public interface IConnectionManager
    {
        bool TryReserve(string userId, string system, Guid connectionId);
        void RegisterLocal(WebSocketConnection connection);
        void Refresh(string userId, string system);
        void Remove(string userId, string system, Guid connectionId);
        void ClearUserSystem(string userId, string system);
        void BroadcastKick(string userId, string system, string reason);
        IReadOnlyList<WebSocketConnection> GetConnections(string userId);
        IReadOnlyList<WebSocketConnection> GetAllConnections();
    }
}
