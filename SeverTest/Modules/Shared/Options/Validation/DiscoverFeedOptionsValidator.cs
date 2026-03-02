using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// 发现页资讯配置校验器。
    /// </summary>
    public sealed class DiscoverFeedOptionsValidator : IValidateOptions<DiscoverFeedOptions>
    {
        public ValidateOptionsResult Validate(string? name, DiscoverFeedOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("DiscoverFeed 配置不能为空");
            }

            var failures = new List<string>();

            if (options.PollIntervalSeconds <= 0)
            {
                failures.Add("DiscoverFeed.PollIntervalSeconds 必须大于 0");
            }

            if (options.InitialLatestCount <= 0)
            {
                failures.Add("DiscoverFeed.InitialLatestCount 必须大于 0");
            }

            if (options.MaxPullLimit <= 0)
            {
                failures.Add("DiscoverFeed.MaxPullLimit 必须大于 0");
            }

            if (options.MemoryCacheMaxItems <= 0)
            {
                failures.Add("DiscoverFeed.MemoryCacheMaxItems 必须大于 0");
            }

            if (options.ProviderPerPage <= 0 || options.ProviderPerPage > 1000)
            {
                failures.Add("DiscoverFeed.ProviderPerPage 必须在 1-1000 之间");
            }

            if (options.InitBackfillMaxPages <= 0)
            {
                failures.Add("DiscoverFeed.InitBackfillMaxPages 必须大于 0");
            }

            if (string.IsNullOrWhiteSpace(options.ArticleListPath))
            {
                failures.Add("DiscoverFeed.ArticleListPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.NewsflashListPath))
            {
                failures.Add("DiscoverFeed.NewsflashListPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ArticleLanguage))
            {
                failures.Add("DiscoverFeed.ArticleLanguage 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.NewsflashLanguage))
            {
                failures.Add("DiscoverFeed.NewsflashLanguage 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
