namespace ServerTest.RateLimit
{
    public enum Protocol
    {
        Http,
        Ws
    }

    public interface IRateLimiter
    {
        bool Allow(string userId, Protocol protocol);
    }
}
