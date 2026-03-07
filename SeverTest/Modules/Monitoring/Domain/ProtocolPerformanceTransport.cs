namespace ServerTest.Modules.Monitoring.Domain
{
    public static class ProtocolPerformanceTransport
    {
        public const string Http = "http";
        public const string WebSocket = "ws";

        public static string Normalize(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                WebSocket => WebSocket,
                _ => Http
            };
        }
    }
}
