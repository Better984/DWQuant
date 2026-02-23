using System.Text;

namespace ServerTest.Modules.Indicators.Domain
{
    /// <summary>
    /// 统一 scope 归一化规则，保证同一语义生成稳定 key。
    /// </summary>
    public static class IndicatorScopeKey
    {
        public static string Build(IDictionary<string, string>? scope, string? defaultScopeKey = null)
        {
            if (scope == null || scope.Count == 0)
            {
                return string.IsNullOrWhiteSpace(defaultScopeKey) ? "global" : defaultScopeKey.Trim();
            }

            var filtered = scope
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => new KeyValuePair<string, string>(item.Key.Trim(), item.Value.Trim()))
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filtered.Count == 0)
            {
                return string.IsNullOrWhiteSpace(defaultScopeKey) ? "global" : defaultScopeKey.Trim();
            }

            var builder = new StringBuilder();
            for (var i = 0; i < filtered.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('&');
                }

                builder.Append(filtered[i].Key.ToLowerInvariant());
                builder.Append('=');
                builder.Append(filtered[i].Value.ToLowerInvariant());
            }

            return builder.ToString();
        }
    }
}
