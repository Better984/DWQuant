namespace ServerTest.Options
{
    public class RateLimitOptions
    {
        public int HttpRps { get; set; } = 5;
        public int WsRps { get; set; } = 20;
    }
}
