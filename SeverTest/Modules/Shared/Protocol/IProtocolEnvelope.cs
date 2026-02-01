namespace ServerTest.Protocol
{
    public interface IProtocolEnvelope
    {
        string Type { get; }
        string? ReqId { get; }
        long Ts { get; }
        int Code { get; }
        string? Msg { get; }
        object? Data { get; }
        string? TraceId { get; }
    }
}
