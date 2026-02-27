namespace ServerTest.WebSockets
{
    public sealed class WebSocketNodeId
    {
        public WebSocketNodeId()
        {
            // 进程级节点标识，分布式踢下线时用于过滤自身消息
            Value = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        }

        public string Value { get; }
    }
}
