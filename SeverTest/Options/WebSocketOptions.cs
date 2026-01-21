namespace ServerTest.Options
{
    public class WebSocketOptions
    {
        public string Path { get; set; } = "/ws";
        public int MaxMessageBytes { get; set; } = 1048576;
        public string KickPolicy { get; set; } = "KickOld";
        public int KeepAliveSeconds { get; set; } = 30;
        public int MaxConnectionsPerSystem { get; set; } = 3;
    }
}
