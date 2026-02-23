using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// 使用 Node + talib-web + talib.wasm 的同核心调用器。
    /// </summary>
    public sealed class TalibWasmNodeInvoker : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly TalibCoreOptions _options;
        private readonly ILogger<TalibWasmNodeInvoker> _logger;
        private readonly object _sync = new();
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private long _nextRequestId;
        private bool _disposed;

        public TalibWasmNodeInvoker(
            IOptions<TalibCoreOptions> options,
            ILogger<TalibWasmNodeInvoker> logger)
        {
            _options = options?.Value ?? new TalibCoreOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsEnabled =>
            string.Equals(_options.Mode, "TalibWasmNode", StringComparison.OrdinalIgnoreCase);

        public bool StrictWasmCore => _options.StrictWasmCore;

        public bool TryCompute(
            string indicator,
            double[][] inputs,
            double[] options,
            int expectedLength,
            out List<double?[]> outputs,
            out string? error)
        {
            outputs = new List<double?[]>();
            error = null;

            if (!IsEnabled)
            {
                error = "TalibCore.Mode 非 TalibWasmNode";
                return false;
            }

            lock (_sync)
            {
                if (!EnsureProcessStarted(out error))
                {
                    return false;
                }

                var requestId = Interlocked.Increment(ref _nextRequestId);
                var request = new BridgeRequest
                {
                    Id = requestId,
                    Type = "compute",
                    Indicator = indicator,
                    Inputs = inputs?.Select(item => item?.ToArray() ?? Array.Empty<double>()).ToArray() ?? Array.Empty<double[]>(),
                    Options = options?.ToArray() ?? Array.Empty<double>(),
                    ExpectedLength = expectedLength
                };

                var line = JsonSerializer.Serialize(request, JsonOptions);
                try
                {
                    _stdin!.WriteLine(line);
                    _stdin.Flush();
                }
                catch (Exception ex)
                {
                    error = $"写入 Node 桥接进程失败: {ex.Message}";
                    StopProcessUnsafe();
                    return false;
                }

                if (!TryReadBridgeResponse(_stdout!, _options.RequestTimeoutMs, out var responseLine, out error))
                {
                    StopProcessUnsafe();
                    return false;
                }

                BridgeResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize<BridgeResponse>(responseLine!, JsonOptions);
                }
                catch (Exception ex)
                {
                    error = $"解析 Node 返回失败: {ex.Message}";
                    return false;
                }

                if (response == null)
                {
                    error = "Node 返回为空";
                    return false;
                }

                if (response.Id != requestId)
                {
                    error = $"Node 返回请求 ID 不匹配: expected={requestId}, actual={response.Id}";
                    return false;
                }

                if (!response.Ok)
                {
                    error = string.IsNullOrWhiteSpace(response.Error) ? "Node 计算失败" : response.Error;
                    return false;
                }

                if (response.Outputs == null)
                {
                    error = "Node 返回缺少 outputs";
                    return false;
                }

                foreach (var output in response.Outputs)
                {
                    outputs.Add((output ?? new List<double?>()).ToArray());
                }

                return true;
            }
        }

        private bool EnsureProcessStarted(out string? error)
        {
            error = null;

            if (_process is { HasExited: false } && _stdin != null && _stdout != null)
            {
                return true;
            }

            StopProcessUnsafe();

            if (!TryResolveRuntimePaths(out var nodeExe, out var bridgeScriptPath, out var metaPath, out var wasmPath, out error))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = nodeExe,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add(bridgeScriptPath);
            startInfo.ArgumentList.Add("--meta");
            startInfo.ArgumentList.Add(metaPath);
            startInfo.ArgumentList.Add("--wasm");
            startInfo.ArgumentList.Add(wasmPath);

            try
            {
                _process = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                error = $"启动 Node 进程失败: {ex.Message}";
                return false;
            }

            if (_process == null)
            {
                error = "启动 Node 进程失败: Process.Start 返回空";
                return false;
            }

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    _logger.LogInformation("TalibNodeBridge: {Message}", args.Data);
                }
            };
            _process.BeginErrorReadLine();

            var pingId = Interlocked.Increment(ref _nextRequestId);
            var ping = JsonSerializer.Serialize(new BridgeRequest
            {
                Id = pingId,
                Type = "ping"
            }, JsonOptions);

            try
            {
                _stdin.WriteLine(ping);
                _stdin.Flush();
            }
            catch (Exception ex)
            {
                error = $"Node 启动后握手写入失败: {ex.Message}";
                StopProcessUnsafe();
                return false;
            }

            if (!TryReadBridgeResponse(_stdout, _options.StartupTimeoutMs, out var responseLine, out error))
            {
                StopProcessUnsafe();
                return false;
            }

            BridgeResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<BridgeResponse>(responseLine!, JsonOptions);
            }
            catch (Exception ex)
            {
                error = $"Node 握手解析失败: {ex.Message}";
                StopProcessUnsafe();
                return false;
            }

            if (response == null || !response.Ok || !string.Equals(response.Type, "pong", StringComparison.OrdinalIgnoreCase))
            {
                error = response?.Error ?? "Node 握手失败";
                StopProcessUnsafe();
                return false;
            }

            _logger.LogInformation("TalibNodeBridge 已启动: {BridgeScript}", bridgeScriptPath);
            return true;
        }

        private static bool TryReadBridgeResponse(StreamReader reader, int timeoutMs, out string? line, out string? error)
        {
            line = null;
            error = null;

            if (timeoutMs <= 0)
            {
                timeoutMs = 1000;
            }

            try
            {
                var readTask = reader.ReadLineAsync();
                var completed = Task.WhenAny(readTask, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
                if (completed != readTask)
                {
                    error = $"读取 Node 返回超时: {timeoutMs}ms";
                    return false;
                }

                line = readTask.GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(line))
                {
                    error = "Node 返回为空行";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"读取 Node 返回失败: {ex.Message}";
                return false;
            }
        }

        private bool TryResolveRuntimePaths(
            out string nodeExe,
            out string bridgeScriptPath,
            out string metaPath,
            out string wasmPath,
            out string? error)
        {
            nodeExe = string.IsNullOrWhiteSpace(_options.NodeExecutable) ? "node" : _options.NodeExecutable.Trim();
            bridgeScriptPath = string.Empty;
            metaPath = string.Empty;
            wasmPath = string.Empty;
            error = null;

            var roots = BuildProbeRoots();
            bridgeScriptPath = ResolvePath(_options.BridgeScriptPath, roots, "Client", "scripts", "talib-node-bridge.mjs");
            metaPath = ResolvePath(_options.MetaPath, roots, "Client", "public", "talib_web_api_meta.json");
            wasmPath = ResolvePath(_options.WasmPath, roots, "Client", "public", "talib.wasm");

            if (!File.Exists(bridgeScriptPath))
            {
                error = $"未找到 Node 桥接脚本: {bridgeScriptPath}";
                return false;
            }

            if (!File.Exists(metaPath))
            {
                error = $"未找到 talib meta 文件: {metaPath}";
                return false;
            }

            if (!File.Exists(wasmPath))
            {
                error = $"未找到 talib wasm 文件: {wasmPath}";
                return false;
            }

            return true;
        }

        private static List<string> BuildProbeRoots()
        {
            var roots = new List<string>();
            TryAddRoot(roots, AppContext.BaseDirectory);
            TryAddRoot(roots, Environment.CurrentDirectory);
            TryAddRoot(roots, Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty));

            var expanded = new List<string>(roots);
            foreach (var root in roots)
            {
                var current = root;
                for (var i = 0; i < 10; i++)
                {
                    var parent = Directory.GetParent(current);
                    if (parent == null)
                    {
                        break;
                    }

                    current = parent.FullName;
                    TryAddRoot(expanded, current);
                }
            }

            return expanded;
        }

        private static void TryAddRoot(List<string> roots, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = Path.GetFullPath(path);
            if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(normalized);
            }
        }

        private static string ResolvePath(string configuredPath, IReadOnlyList<string> roots, params string[] relativeSegments)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            foreach (var root in roots)
            {
                var candidate = Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(new[] { roots.FirstOrDefault() ?? Environment.CurrentDirectory }.Concat(relativeSegments).ToArray());
        }

        private void StopProcessUnsafe()
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch
            {
                // 忽略关闭过程中的异常，避免影响主流程。
            }
            finally
            {
                _stdin?.Dispose();
                _stdout?.Dispose();
                _process?.Dispose();
                _stdin = null;
                _stdout = null;
                _process = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                StopProcessUnsafe();
                _disposed = true;
            }
        }

        private sealed class BridgeRequest
        {
            public long Id { get; set; }

            public string Type { get; set; } = "compute";

            public string Indicator { get; set; } = string.Empty;

            public double[][] Inputs { get; set; } = Array.Empty<double[]>();

            public double[] Options { get; set; } = Array.Empty<double>();

            public int ExpectedLength { get; set; }
        }

        private sealed class BridgeResponse
        {
            public long Id { get; set; }

            public bool Ok { get; set; }

            public string Type { get; set; } = string.Empty;

            public string? Error { get; set; }

            public List<List<double?>>? Outputs { get; set; }
        }
    }
}
