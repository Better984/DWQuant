using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyRuntime.Domain;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 策略运行时文档构建工具。
    /// </summary>
    internal static class StrategyRuntimeDocumentBuilder
    {
        public static StrategyDocument BuildDocument(StrategyRuntimeRow row, StrategyConfig config)
        {
            return new StrategyDocument
            {
                UserStrategy = new StrategyUserStrategy
                {
                    UsId = row.UsId,
                    Uid = row.Uid,
                    DefId = row.DefId,
                    AliasName = row.AliasName ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    State = row.State ?? string.Empty,
                    Visibility = row.Visibility ?? "private",
                    ShareCode = row.ShareCode,
                    PriceUsdt = row.PriceUsdt,
                    Source = new StrategySourceRef
                    {
                        Type = row.SourceType ?? "custom",
                        Ref = row.SourceRef
                    },
                    PinnedVersionId = row.PinnedVersionId,
                    ExchangeApiKeyId = row.ExchangeApiKeyId,
                    UpdatedAt = row.UpdatedAt.HasValue
                        ? new DateTimeOffset(row.UpdatedAt.Value, TimeSpan.Zero)
                        : DateTimeOffset.UtcNow
                },
                Definition = new StrategyDefinition
                {
                    DefId = row.DefId,
                    DefType = row.DefType ?? "custom",
                    Name = row.DefName ?? string.Empty,
                    Description = row.DefDescription ?? string.Empty,
                    CreatorUid = row.CreatorUid
                },
                Version = new StrategyVersion
                {
                    VersionId = row.VersionId,
                    VersionNo = row.VersionNo,
                    ContentHash = row.ContentHash ?? string.Empty,
                    ArtifactUri = row.ArtifactUri,
                    ConfigJson = config
                }
            };
        }
    }
}
