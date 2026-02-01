namespace ServerTest.Models.Exchange
{
    public sealed class UserExchangeApiKeyDto
    {
        public long Id { get; set; }
        public string ExchangeType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ApiKeyMasked { get; set; } = string.Empty;
        public string ApiSecretMasked { get; set; } = string.Empty;
        public bool HasPassword { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class CreateUserExchangeApiKeyRequest
    {
        public string ExchangeType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string? ApiPassword { get; set; }
    }
}
