// ??? API Key ????
namespace ServerTest.Modules.ExchangeApiKeys.Domain
{
    public sealed class UserExchangeApiKeyRecord
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public string ExchangeType { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string? ApiPassword { get; set; }
    }

    public sealed class UserExchangeApiKeyDetail
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public string ExchangeType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string? ApiPassword { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
