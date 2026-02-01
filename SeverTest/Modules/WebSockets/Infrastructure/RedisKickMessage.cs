namespace ServerTest.WebSockets
{
    public sealed class RedisKickMessage
    {
        public string NodeId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string System { get; set; } = string.Empty;
        public string Reason { get; set; } = "replaced";
        public long Ts { get; set; }
    }
}
