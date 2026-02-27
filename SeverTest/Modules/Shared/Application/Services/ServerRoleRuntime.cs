using System;

namespace ServerTest.Services
{
    /// <summary>
    /// 服务器角色定义。
    /// </summary>
    public enum ServerRole
    {
        Core = 1,
        BacktestWorker = 2,
        Full = 3,
    }

    /// <summary>
    /// 服务器角色运行时上下文。
    /// </summary>
    public sealed class ServerRoleRuntime
    {
        public ServerRoleRuntime(ServerRole role, string source, string roleFilePath, int promptSeconds)
        {
            Role = role;
            Source = source ?? string.Empty;
            RoleFilePath = roleFilePath ?? string.Empty;
            PromptSeconds = promptSeconds;
        }

        public ServerRole Role { get; }

        public string Source { get; }

        public string RoleFilePath { get; }

        public int PromptSeconds { get; }

        public bool IsCoreLike => Role == ServerRole.Core || Role == ServerRole.Full;

        public bool IsBacktestWorker => Role == ServerRole.BacktestWorker;
    }

    public static class ServerRoleHelper
    {
        public const int DefaultPromptSeconds = 10;

        public static ServerRole ParseOrDefault(string? value, ServerRole fallback)
        {
            if (TryParse(value, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public static bool TryParse(string? value, out ServerRole role)
        {
            role = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "1":
                case "core":
                case "core-server":
                case "core_server":
                    role = ServerRole.Core;
                    return true;

                case "2":
                case "backtest":
                case "backtest-worker":
                case "backtest_worker":
                case "worker":
                    role = ServerRole.BacktestWorker;
                    return true;

                case "3":
                case "full":
                case "all":
                case "full-server":
                case "full_server":
                    role = ServerRole.Full;
                    return true;

                default:
                    return false;
            }
        }

        public static string ToValue(ServerRole role)
        {
            return role switch
            {
                ServerRole.Core => "core",
                ServerRole.BacktestWorker => "backtest-worker",
                ServerRole.Full => "full",
                _ => "full",
            };
        }

        public static string ToDisplayName(ServerRole role)
        {
            return role switch
            {
                ServerRole.Core => "核心服务器",
                ServerRole.BacktestWorker => "回测算力服务器",
                ServerRole.Full => "全功能服务器",
                _ => "未知服务器",
            };
        }
    }
}
