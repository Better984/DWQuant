namespace ServerTest.WebSockets
{
    public interface IConnectionManager
    {
        bool TryReserve(string userId, string system, Guid connectionId);
        void RegisterLocal(WebSocketConnection connection);
        void Remove(string userId, string system, Guid connectionId);
        void ClearUserSystem(string userId, string system);
        IReadOnlyList<WebSocketConnection> GetConnections(string userId);
        IReadOnlyList<WebSocketConnection> GetAllConnections();
    }
}
