using System.Text.Json;

namespace ServerTest.Services
{
    /// <summary>
    /// 启动前服务器角色选择器：
    /// 1. 支持命令行指定；
    /// 2. 支持本地持久化默认值；
    /// 3. 支持三步交互：选择模式 -> 是否更新分布式核心地址 -> 确认启动。
    /// </summary>
    public static class ServerRoleBootstrap
    {
        private sealed class PersistedRoleState
        {
            public string Role { get; set; } = string.Empty;

            public string WorkerCoreWsUrls { get; set; } = string.Empty;

            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        }

        private readonly struct PromptResult
        {
            public PromptResult(ServerRole role, string workerCoreWsUrls, string source)
            {
                Role = role;
                WorkerCoreWsUrls = workerCoreWsUrls;
                Source = source;
            }

            public ServerRole Role { get; }

            public string WorkerCoreWsUrls { get; }

            public string Source { get; }
        }

        public sealed class ServerRoleSelection
        {
            public required ServerRole Role { get; init; }

            public required string Source { get; init; }

            public required string RoleFilePath { get; init; }

            public required int PromptSeconds { get; init; }

            public string WorkerCoreWsUrls { get; init; } = string.Empty;
        }

        public static ServerRoleSelection Resolve(
            string[] args,
            string contentRootPath,
            int promptSeconds = ServerRoleHelper.DefaultPromptSeconds)
        {
            var roleFilePath = BuildRoleFilePath(contentRootPath);
            var fallbackRole = ServerRole.Full;
            var timeoutSeconds = Math.Max(1, promptSeconds);

            var persisted = LoadState(roleFilePath, fallbackRole);
            var selectedRole = ServerRoleHelper.ParseOrDefault(persisted.Role, fallbackRole);
            var selectedWorkerCoreWsUrls = persisted.WorkerCoreWsUrls;
            var source = "persisted_default";

            if (TryGetRoleFromArgs(args, out var argRole))
            {
                selectedRole = argRole;
                source = "command_line";
            }

            if (TryGetWorkerCoreWsUrlsFromArgs(args, out var argWorkerCoreWsUrls))
            {
                selectedWorkerCoreWsUrls = argWorkerCoreWsUrls;
                source = source == "command_line" ? "command_line_with_worker_ws" : "command_line_worker_ws";
            }

            if (source == "command_line" || source == "command_line_with_worker_ws" || source == "command_line_worker_ws")
            {
                SaveState(roleFilePath, selectedRole, selectedWorkerCoreWsUrls);
                return new ServerRoleSelection
                {
                    Role = selectedRole,
                    Source = source,
                    RoleFilePath = roleFilePath,
                    PromptSeconds = timeoutSeconds,
                    WorkerCoreWsUrls = selectedWorkerCoreWsUrls
                };
            }

            if (CanPrompt())
            {
                var picked = PromptSelection(selectedRole, selectedWorkerCoreWsUrls, timeoutSeconds);
                if (picked.HasValue)
                {
                    selectedRole = picked.Value.Role;
                    selectedWorkerCoreWsUrls = picked.Value.WorkerCoreWsUrls;
                    source = picked.Value.Source;
                }
                else
                {
                    source = "timeout_default";
                }
            }

            SaveState(roleFilePath, selectedRole, selectedWorkerCoreWsUrls);
            return new ServerRoleSelection
            {
                Role = selectedRole,
                Source = source,
                RoleFilePath = roleFilePath,
                PromptSeconds = timeoutSeconds,
                WorkerCoreWsUrls = selectedWorkerCoreWsUrls
            };
        }

        private static bool TryGetRoleFromArgs(string[] args, out ServerRole role)
        {
            role = default;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith("--server-role=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("--server-role=".Length).Trim();
                    return ServerRoleHelper.TryParse(value, out role);
                }

                if (arg.Equals("--server-role", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return ServerRoleHelper.TryParse(args[i + 1], out role);
                }
            }

            return false;
        }

        private static bool TryGetWorkerCoreWsUrlsFromArgs(string[] args, out string value)
        {
            value = string.Empty;
            if (args == null || args.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (arg.StartsWith("--worker-core-ws-urls=", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = arg.Substring("--worker-core-ws-urls=".Length).Trim();
                    return TryParseWorkerCoreWsUrls(raw, out value);
                }

                if (arg.Equals("--worker-core-ws-urls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return TryParseWorkerCoreWsUrls(args[i + 1], out value);
                }
            }

            return false;
        }

        private static string BuildRoleFilePath(string contentRootPath)
        {
            var root = string.IsNullOrWhiteSpace(contentRootPath)
                ? AppContext.BaseDirectory
                : contentRootPath;
            return Path.Combine(root, "Config", "server-role.local.json");
        }

        private static PersistedRoleState LoadState(string roleFilePath, ServerRole fallbackRole)
        {
            var fallback = new PersistedRoleState
            {
                Role = ServerRoleHelper.ToValue(fallbackRole),
                WorkerCoreWsUrls = string.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            };

            try
            {
                if (!File.Exists(roleFilePath))
                {
                    return fallback;
                }

                var json = File.ReadAllText(roleFilePath);
                var model = JsonSerializer.Deserialize<PersistedRoleState>(json);
                if (model == null)
                {
                    return fallback;
                }

                var parsedRole = ServerRoleHelper.ParseOrDefault(model.Role, fallbackRole);
                var workerCoreWsUrls = NormalizeWorkerCoreWsUrlsForPersist(model.WorkerCoreWsUrls);
                return new PersistedRoleState
                {
                    Role = ServerRoleHelper.ToValue(parsedRole),
                    WorkerCoreWsUrls = workerCoreWsUrls,
                    UpdatedAtUtc = model.UpdatedAtUtc
                };
            }
            catch
            {
                return fallback;
            }
        }

        private static void SaveState(string roleFilePath, ServerRole role, string workerCoreWsUrls)
        {
            try
            {
                var dir = Path.GetDirectoryName(roleFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var state = new PersistedRoleState
                {
                    Role = ServerRoleHelper.ToValue(role),
                    WorkerCoreWsUrls = NormalizeWorkerCoreWsUrlsForPersist(workerCoreWsUrls),
                    UpdatedAtUtc = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(roleFilePath, json);
            }
            catch
            {
                // 持久化失败不阻断启动
            }
        }

        private static bool CanPrompt()
        {
            try
            {
                if (Console.IsInputRedirected)
                {
                    return false;
                }

                return Environment.UserInteractive;
            }
            catch
            {
                return false;
            }
        }

        private static PromptResult? PromptSelection(ServerRole defaultRole, string defaultWorkerCoreWsUrls, int promptSeconds)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("===============================================");
                Console.WriteLine("DWQuant 服务器启动配置");
                Console.WriteLine("===============================================");
                Console.WriteLine($"已保存角色: {ServerRoleHelper.ToDisplayName(defaultRole)} ({ServerRoleHelper.ToValue(defaultRole)})");
                Console.WriteLine($"已保存核心地址: {(string.IsNullOrWhiteSpace(defaultWorkerCoreWsUrls) ? "(空)" : defaultWorkerCoreWsUrls)}");
                Console.WriteLine();
                Console.WriteLine("步骤 1/3 选择模式（输入序号后回车，空输入使用默认）：");
                Console.WriteLine("1. 核心服务器（对外 HTTP/WS，分发回测任务）");
                Console.WriteLine("2. 回测算力服务器（连接核心服务器，执行回测）");
                Console.WriteLine("3. 全功能服务器（单机模式，兼容开发环境）");
                Console.Write($"请输入 [1/2/3]（{promptSeconds} 秒超时默认）：");

                var inputTask = Task.Run(Console.ReadLine);
                var completed = Task.WhenAny(inputTask, Task.Delay(TimeSpan.FromSeconds(promptSeconds))).GetAwaiter().GetResult();
                Console.WriteLine();

                if (completed != inputTask)
                {
                    Console.WriteLine($"输入超时，使用已保存角色：{ServerRoleHelper.ToDisplayName(defaultRole)}");
                    return null;
                }

                var roleInput = inputTask.GetAwaiter().GetResult();
                var selectedRole = defaultRole;
                if (!string.IsNullOrWhiteSpace(roleInput))
                {
                    if (ServerRoleHelper.TryParse(roleInput, out var parsedRole))
                    {
                        selectedRole = parsedRole;
                    }
                    else
                    {
                        Console.WriteLine("模式输入无效，将使用已保存角色。");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("步骤 2/3 是否新增/更新分布式核心节点地址？");
                Console.Write("输入 [y/N]：");
                var addIpInput = Console.ReadLine();

                var selectedWorkerCoreWsUrls = defaultWorkerCoreWsUrls;
                if (IsYes(addIpInput))
                {
                    Console.WriteLine(
                        "请输入核心节点地址，多个地址请用 | 分隔。示例：127.0.0.1:9635|192.168.1.10:9635");
                    Console.Write("地址列表：");
                    var urlsInput = Console.ReadLine();
                    if (TryParseWorkerCoreWsUrls(urlsInput, out var normalizedUrls))
                    {
                        selectedWorkerCoreWsUrls = normalizedUrls;
                    }
                    else
                    {
                        Console.WriteLine("地址格式无效，保留原有配置。");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("步骤 3/3 确认启动");
                Console.WriteLine($"- 角色：{ServerRoleHelper.ToDisplayName(selectedRole)} ({ServerRoleHelper.ToValue(selectedRole)})");
                Console.WriteLine($"- 核心地址：{(string.IsNullOrWhiteSpace(selectedWorkerCoreWsUrls) ? "(空)" : selectedWorkerCoreWsUrls)}");
                Console.Write("确认启动？[Y/n]：");
                var confirmInput = Console.ReadLine();

                if (IsNo(confirmInput))
                {
                    Console.WriteLine("已取消本次确认，请重新选择。");
                    defaultRole = selectedRole;
                    defaultWorkerCoreWsUrls = selectedWorkerCoreWsUrls;
                    continue;
                }

                return new PromptResult(selectedRole, selectedWorkerCoreWsUrls, "interactive");
            }
        }

        private static bool IsYes(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            return string.Equals(input.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(input.Trim(), "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(input.Trim(), "1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNo(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            return string.Equals(input.Trim(), "n", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(input.Trim(), "no", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(input.Trim(), "0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseWorkerCoreWsUrls(string? rawInput, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return false;
            }

            var values = rawInput
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
            {
                return false;
            }

            var endpoints = new List<string>(values.Length);
            foreach (var value in values)
            {
                var endpoint = NormalizeSingleWorkerEndpoint(value);
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    continue;
                }

                if (!endpoints.Contains(endpoint, StringComparer.OrdinalIgnoreCase))
                {
                    endpoints.Add(endpoint);
                }
            }

            if (endpoints.Count == 0)
            {
                return false;
            }

            normalized = string.Join("|", endpoints);
            return true;
        }

        private static string NormalizeWorkerCoreWsUrlsForPersist(string? value)
        {
            return TryParseWorkerCoreWsUrls(value, out var normalized)
                ? normalized
                : string.Empty;
        }

        private static string? NormalizeSingleWorkerEndpoint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var endpoint = value.Trim();
            if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = "ws://" + endpoint.Substring("http://".Length);
            }
            else if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = "wss://" + endpoint.Substring("https://".Length);
            }
            else if (!endpoint.Contains("://", StringComparison.Ordinal))
            {
                endpoint = "ws://" + endpoint;
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var builder = new UriBuilder(uri);
            if (string.IsNullOrWhiteSpace(builder.Path) || builder.Path == "/")
            {
                builder.Path = "/ws/worker";
            }
            else
            {
                builder.Path = builder.Path.TrimEnd('/');
            }

            return builder.Uri.ToString();
        }
    }
}
