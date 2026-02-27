using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using System.Security.Cryptography;
using System.Text;

namespace ServerTest.WebSockets
{
    public sealed class WebSocketNodeId
    {
        public WebSocketNodeId(
            IOptions<WebSocketOptions> options,
            ILogger<WebSocketNodeId>? logger = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var machineName = string.IsNullOrWhiteSpace(Environment.MachineName)
                ? "unknown-machine"
                : Environment.MachineName.Trim();

            var configuredSuffix = FirstNonEmpty(
                options.Value.NodeId,
                Environment.GetEnvironmentVariable("WS_NODE_ID"));

            if (string.IsNullOrWhiteSpace(configuredSuffix))
            {
                configuredSuffix = BuildDirectoryHashSuffix();
                logger?.LogWarning(
                    "WebSocket NodeId 未显式配置，已使用机器名+目录哈希自动生成: {NodeId}。多实例部署建议设置 WebSocket:NodeId 或环境变量 WS_NODE_ID。",
                    $"{machineName}-{configuredSuffix}");
            }

            var normalizedSuffix = NormalizeSuffix(configuredSuffix);
            if (normalizedSuffix.StartsWith(machineName + "-", StringComparison.OrdinalIgnoreCase))
            {
                Value = normalizedSuffix;
                return;
            }

            // 统一保持 MachineName-后缀 结构，便于运维识别与分组。
            Value = $"{machineName}-{normalizedSuffix}";
        }

        public string Value { get; }

        private static string BuildDirectoryHashSuffix()
        {
            var baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? "unknown-dir"
                : AppContext.BaseDirectory.Trim();
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(baseDir));
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            return hex.Length > 12 ? hex[..12] : hex;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string NormalizeSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "node";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('-');
                }
            }

            var normalized = builder.ToString().Trim('-', '_');
            return string.IsNullOrWhiteSpace(normalized) ? "node" : normalized;
        }
    }
}
