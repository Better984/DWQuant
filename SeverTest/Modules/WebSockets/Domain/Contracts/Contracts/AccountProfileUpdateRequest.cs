namespace ServerTest.WebSockets.Contracts
{
    public sealed class AccountProfileUpdateRequest
    {
        public string? Nickname { get; set; }
        public string? Signature { get; set; }
    }
}
