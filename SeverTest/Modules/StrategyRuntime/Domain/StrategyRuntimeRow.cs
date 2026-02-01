namespace ServerTest.Modules.StrategyRuntime.Domain
{
    public sealed class StrategyRuntimeRow
    {
        public long UsId { get; set; }
        public long Uid { get; set; }
        public long DefId { get; set; }
        public long PinnedVersionId { get; set; }
        public string? AliasName { get; set; }
        public string? Description { get; set; }
        public string? State { get; set; }
        public string? Visibility { get; set; }
        public string? ShareCode { get; set; }
        public decimal PriceUsdt { get; set; }
        public string? SourceType { get; set; }
        public string? SourceRef { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? DefName { get; set; }
        public string? DefDescription { get; set; }
        public string? DefType { get; set; }
        public long CreatorUid { get; set; }
        public long VersionId { get; set; }
        public int VersionNo { get; set; }
        public string? ConfigJson { get; set; }
        public string? ContentHash { get; set; }
        public string? ArtifactUri { get; set; }
    }
}
