namespace ServerTest.Protocol
{
    public interface IProtocolRequest
    {
        string Type { get; }
        string ReqId { get; }
        long Ts { get; }
    }
}
