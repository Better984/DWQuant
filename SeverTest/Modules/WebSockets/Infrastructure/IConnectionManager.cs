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

        /// <summary>
        /// 清除本节点因进程重启而残留在 Redis 中的连接槽位。单机/内存模式可空实现。
        /// </summary>
        void ClearStaleEntriesForCurrentNode();

        /// <summary>
        /// 按 userId + system 定向清理当前节点的陈旧槽位，返回删除数量。
        /// 仅用于连接握手失败后的兜底重试，避免误删其他节点仍有效的连接槽位。
        /// </summary>
        long ClearStaleEntriesForCurrentNode(string userId, string system);
    }
}
