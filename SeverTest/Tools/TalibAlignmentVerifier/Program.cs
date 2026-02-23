using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TALib;

var argumentMap = ParseArgs(args);
var defaultCasesPath = ResolveDefaultConfigPath("ta_alignment_cases.json");
var defaultBaselinePath = ResolveDefaultConfigPath("ta_alignment_baseline.frontend.json");
var casesPath = GetArgument(argumentMap, "--cases", defaultCasesPath);
var baselinePath = GetArgument(argumentMap, "--baseline", defaultBaselinePath);
var engine = GetStringArgument(argumentMap, "--engine", "talibnet").Trim().ToLowerInvariant();
var useWasmCore = engine is "wasm" or "wasmcore" or "wasm-core" or "talibwasmnode";

if (!File.Exists(casesPath))
{
    Console.WriteLine($"[对齐校验] 未找到样本配置文件: {casesPath}");
    return 2;
}

if (!File.Exists(baselinePath))
{
    Console.WriteLine($"[对齐校验] 未找到前端基线文件: {baselinePath}");
    return 2;
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
};

var casesRoot = JsonSerializer.Deserialize<AlignmentCasesRoot>(File.ReadAllText(casesPath), jsonOptions)
    ?? throw new InvalidOperationException("解析样本配置失败");
var baselineRoot = JsonSerializer.Deserialize<FrontendBaselineRoot>(File.ReadAllText(baselinePath), jsonOptions)
    ?? throw new InvalidOperationException("解析前端基线失败");

var tolerance = argumentMap.TryGetValue("--tolerance", out var toleranceText) && double.TryParse(toleranceText, out var cliTolerance)
    ? cliTolerance
    : (casesRoot.Tolerance > 0 ? casesRoot.Tolerance : 1e-10);

Console.WriteLine($"[对齐校验] 样本文件: {casesPath}");
Console.WriteLine($"[对齐校验] 基线文件: {baselinePath}");
Console.WriteLine($"[对齐校验] 误差阈值: {tolerance:E6}");
Console.WriteLine($"[对齐校验] 后端引擎: {(useWasmCore ? "TalibWasmNode" : "TalibNet")}");

var baselineByName = baselineRoot.Cases
    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
    .ToDictionary(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase);

var series = GenerateSeries(casesRoot.Generator ?? new GeneratorConfig());
using var wasmBridge = useWasmCore ? CreateWasmBridge(argumentMap) : null;

var totalCases = 0;
var passedCases = 0;
var failedCases = 0;
var globalMaxDiff = 0.0;

foreach (var caseDef in casesRoot.Cases)
{
    totalCases += 1;
    if (!baselineByName.TryGetValue(caseDef.Name, out var baselineCase))
    {
        Console.WriteLine($"[对齐校验][失败] 未找到同名基线: {caseDef.Name}");
        failedCases += 1;
        continue;
    }

    var backendResult = ComputeBackend(caseDef, series, useWasmCore, wasmBridge);
    var compare = CompareCase(caseDef.Name, baselineCase, backendResult, tolerance);

    globalMaxDiff = Math.Max(globalMaxDiff, compare.CaseMaxDiff);
    if (compare.Success)
    {
        passedCases += 1;
        Console.WriteLine($"[对齐校验][通过] {caseDef.Name} 指标={caseDef.Indicator} 输出={backendResult.Outputs.Count} 最大误差={compare.CaseMaxDiff:E6}");
        continue;
    }

    failedCases += 1;
    Console.WriteLine($"[对齐校验][失败] {caseDef.Name} 指标={caseDef.Indicator} 最大误差={compare.CaseMaxDiff:E6}");
    foreach (var detail in compare.Details.Take(20))
    {
        Console.WriteLine($"  - {detail}");
    }

    if (compare.Details.Count > 20)
    {
        Console.WriteLine($"  - ... 其余 {compare.Details.Count - 20} 条差异已省略");
    }
}

Console.WriteLine("============================================================");
Console.WriteLine($"[对齐校验] 总案例={totalCases} 通过={passedCases} 失败={failedCases} 全局最大误差={globalMaxDiff:E6}");

return failedCases == 0 ? 0 : 1;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var token = args[i];
        if (!token.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            map[token] = args[i + 1];
            i += 1;
            continue;
        }

        map[token] = "true";
    }

    return map;
}

static string GetArgument(Dictionary<string, string> map, string name, string fallback)
{
    return map.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? Path.GetFullPath(value)
        : fallback;
}

static string GetStringArgument(Dictionary<string, string> map, string name, string fallback)
{
    return map.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : fallback;
}

static int GetIntArgument(Dictionary<string, string> map, string name, int fallback)
{
    return map.TryGetValue(name, out var value) && int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : fallback;
}

static string ResolveDefaultConfigPath(string fileName)
{
    var roots = BuildProbeRoots();

    foreach (var root in roots)
    {
        var resolved = FindConfigFileUpward(root, fileName, maxDepth: 8);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }
    }

    return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Config", fileName));
}

static string ResolveDefaultClientPath(params string[] relativeSegments)
{
    var roots = BuildProbeRoots();
    foreach (var root in roots)
    {
        var candidate = Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return Path.GetFullPath(Path.Combine(new[] { Environment.CurrentDirectory }.Concat(relativeSegments).ToArray()));
}

static List<string> BuildProbeRoots()
{
    var roots = new List<string>();
    TryAddRoot(roots, Environment.CurrentDirectory);
    TryAddRoot(roots, AppContext.BaseDirectory);
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

static void TryAddRoot(List<string> roots, string? path)
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

static string? FindConfigFileUpward(string startPath, string fileName, int maxDepth)
{
    var current = Path.GetFullPath(startPath);
    for (var depth = 0; depth <= maxDepth; depth++)
    {
        var candidate = Path.Combine(current, "Config", fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var parent = Directory.GetParent(current);
        if (parent is null)
        {
            break;
        }

        current = parent.FullName;
    }

    return null;
}

static WasmBridgeClient CreateWasmBridge(Dictionary<string, string> argumentMap)
{
    var nodeExe = GetStringArgument(argumentMap, "--node", "node");
    var bridgePath = GetArgument(argumentMap, "--bridge", ResolveDefaultClientPath("Client", "scripts", "talib-node-bridge.mjs"));
    var metaPath = GetArgument(argumentMap, "--meta", ResolveDefaultClientPath("Client", "public", "talib_web_api_meta.json"));
    var wasmPath = GetArgument(argumentMap, "--wasm", ResolveDefaultClientPath("Client", "public", "talib.wasm"));
    var startupTimeoutMs = GetIntArgument(argumentMap, "--startup-timeout", 15000);
    var requestTimeoutMs = GetIntArgument(argumentMap, "--request-timeout", 30000);

    Console.WriteLine($"[对齐校验] Node 路径: {nodeExe}");
    Console.WriteLine($"[对齐校验] 桥接脚本: {bridgePath}");
    Console.WriteLine($"[对齐校验] Meta 文件: {metaPath}");
    Console.WriteLine($"[对齐校验] Wasm 文件: {wasmPath}");

    return new WasmBridgeClient(
        nodeExe,
        bridgePath,
        metaPath,
        wasmPath,
        startupTimeoutMs,
        requestTimeoutMs);
}

static MarketSeries GenerateSeries(GeneratorConfig config)
{
    var length = Math.Max(120, config.Length > 0 ? config.Length : 360);
    var basePrice = config.BasePrice;
    var trendStep = config.TrendStep;
    var openAmp = config.OpenWaveAmplitude;
    var closeAmp = config.CloseWaveAmplitude;
    var highPadding = config.HighPadding;
    var lowPadding = config.LowPadding;
    var volumeBase = config.VolumeBase;
    var volumeAmp = config.VolumeWaveAmplitude;

    var open = new double[length];
    var high = new double[length];
    var low = new double[length];
    var close = new double[length];
    var volume = new double[length];

    for (var i = 0; i < length; i++)
    {
        var trend = basePrice + i * trendStep;
        var o = trend + Math.Sin(i * 0.17) * openAmp;
        var c = trend + Math.Cos(i * 0.11) * closeAmp;
        var h = Math.Max(o, c) + highPadding + Math.Sin(i * 0.07) * 0.25;
        var l = Math.Min(o, c) - lowPadding - Math.Cos(i * 0.09) * 0.22;

        if (h <= l)
        {
            h = l + 0.01;
        }

        var v = volumeBase + ((i % 37) - 18) * 8 + Math.Cos(i * 0.13) * volumeAmp;
        if (!double.IsFinite(v) || v < 1)
        {
            v = 1;
        }

        open[i] = o;
        high[i] = h;
        low[i] = l;
        close[i] = c;
        volume[i] = v;
    }

    return new MarketSeries(open, high, low, close, volume);
}

static BackendCaseResult ComputeBackend(
    AlignmentCase caseDef,
    MarketSeries series,
    bool useWasmCore,
    WasmBridgeClient? wasmBridge)
{
    var indicator = caseDef.Indicator.Trim().ToUpperInvariant();
    var func = Abstract.Function(indicator)
        ?? throw new InvalidOperationException($"未找到指标函数: {indicator}");

    var inputNames = func.Inputs;
    var inputArrays = new double[inputNames.Length][];
    var realSources = caseDef.RealSources ?? new List<string>();
    var realIndex = 0;

    for (var i = 0; i < inputNames.Length; i++)
    {
        var source = ResolveInputSource(inputNames[i], realSources, ref realIndex);
        inputArrays[i] = BuildSourceSeries(series, source);
    }

    var optionCount = func.Options.Length;
    var parameters = new double[optionCount];
    for (var i = 0; i < optionCount; i++)
    {
        parameters[i] = i < caseDef.Parameters.Count ? caseDef.Parameters[i] : 0.0;
    }

    var outputNames = func.Outputs
        .Select(output => string.IsNullOrWhiteSpace(output.displayName) ? "OUTPUT" : output.displayName)
        .ToList();

    if (useWasmCore)
    {
        if (wasmBridge == null)
        {
            throw new InvalidOperationException("Wasm 引擎未初始化");
        }

        var wasmOutputs = wasmBridge.Compute(
            indicator,
            inputArrays,
            parameters,
            series.Length,
            outputNames.Count,
            caseDef.Name);

        return new BackendCaseResult(caseDef.Name, indicator, outputNames, wasmOutputs);
    }

    var outputCount = func.Outputs.Length;
    var outputArrays = new double[outputCount][];
    for (var i = 0; i < outputCount; i++)
    {
        outputArrays[i] = new double[series.Length];
    }

    var retCode = func.Run<double>(
        inputArrays,
        parameters,
        outputArrays,
        new Range(0, series.Length - 1),
        out var outRange);

    if (retCode != Core.RetCode.Success)
    {
        throw new InvalidOperationException($"指标计算失败: {caseDef.Name} retCode={retCode}");
    }

    var outStart = outRange.Start.Value;
    var outEnd = outRange.End.Value;
    var fullOutputs = new List<List<double?>>();
    for (var outputIndex = 0; outputIndex < outputCount; outputIndex++)
    {
        var full = new List<double?>(series.Length);
        for (var i = 0; i < series.Length; i++)
        {
            if (i < outStart || i >= outEnd)
            {
                full.Add(null);
                continue;
            }

            var valueIndex = i - outStart;
            if (valueIndex < 0 || valueIndex >= outputArrays[outputIndex].Length)
            {
                full.Add(null);
                continue;
            }

            var value = outputArrays[outputIndex][valueIndex];
            if (!double.IsFinite(value))
            {
                full.Add(null);
                continue;
            }

            full.Add(value);
        }
        fullOutputs.Add(full);
    }

    return new BackendCaseResult(caseDef.Name, indicator, outputNames, fullOutputs);
}

static string ResolveInputSource(string inputName, IReadOnlyList<string> realSources, ref int realIndex)
{
    var normalized = NormalizeKey(inputName);
    var canonical = normalized.StartsWith("IN", StringComparison.Ordinal) && normalized.Length > 2
        ? normalized[2..]
        : normalized;

    if (canonical is "OPEN" or "HIGH" or "LOW" or "CLOSE" or "VOLUME")
    {
        return canonical;
    }

    var source = realIndex < realSources.Count ? realSources[realIndex] : "CLOSE";
    realIndex += 1;
    return NormalizeSource(source);
}

static double[] BuildSourceSeries(MarketSeries series, string source)
{
    var normalized = NormalizeSource(source);
    var result = new double[series.Length];
    for (var i = 0; i < series.Length; i++)
    {
        result[i] = ResolveSourceValue(series, normalized, i);
    }

    return result;
}

static string NormalizeKey(string value)
{
    return new string(value
        .Trim()
        .ToUpperInvariant()
        .Where(ch => ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')
        .ToArray());
}

static string NormalizeSource(string value)
{
    var normalized = NormalizeKey(value);
    return normalized switch
    {
        "OPEN" => "OPEN",
        "HIGH" => "HIGH",
        "LOW" => "LOW",
        "CLOSE" => "CLOSE",
        "VOLUME" => "VOLUME",
        "HL2" => "HL2",
        "HLC3" => "HLC3",
        "OHLC4" => "OHLC4",
        "OC2" => "OC2",
        "HLCC4" => "HLCC4",
        _ => "CLOSE"
    };
}

static double ResolveSourceValue(MarketSeries series, string source, int index)
{
    var open = series.Open[index];
    var high = series.High[index];
    var low = series.Low[index];
    var close = series.Close[index];
    var volume = series.Volume[index];

    return source switch
    {
        "OPEN" => open,
        "HIGH" => high,
        "LOW" => low,
        "CLOSE" => close,
        "VOLUME" => volume,
        "HL2" => (high + low) / 2.0,
        "HLC3" => (high + low + close) / 3.0,
        "OHLC4" => (open + high + low + close) / 4.0,
        "OC2" => (open + close) / 2.0,
        "HLCC4" => (high + low + close + close) / 4.0,
        _ => close
    };
}

static CompareResult CompareCase(string caseName, FrontendBaselineCase baseline, BackendCaseResult backend, double tolerance)
{
    var details = new List<string>();
    var caseMaxDiff = 0.0;

    if (baseline.Outputs.Count != backend.Outputs.Count)
    {
        details.Add($"输出数量不一致: 前端={baseline.Outputs.Count}, 后端={backend.Outputs.Count}");
        return new CompareResult(false, caseMaxDiff, details);
    }

    for (var outputIndex = 0; outputIndex < baseline.Outputs.Count; outputIndex++)
    {
        var frontArray = baseline.Outputs[outputIndex];
        var backArray = backend.Outputs[outputIndex];
        var maxLength = Math.Max(frontArray.Count, backArray.Count);

        for (var i = 0; i < maxLength; i++)
        {
            var front = i < frontArray.Count ? frontArray[i] : null;
            var back = i < backArray.Count ? backArray[i] : null;

            if (front is null && back is null)
            {
                continue;
            }

            if (front is null || back is null)
            {
                details.Add($"output#{outputIndex} index={i} 空值不一致 front={FormatNullable(front)} back={FormatNullable(back)}");
                continue;
            }

            var diff = Math.Abs(front.Value - back.Value);
            caseMaxDiff = Math.Max(caseMaxDiff, diff);
            if (diff > tolerance)
            {
                details.Add($"output#{outputIndex} index={i} diff={diff:E6} front={front.Value:E15} back={back.Value:E15}");
            }
        }
    }

    return new CompareResult(details.Count == 0, caseMaxDiff, details);
}

static string FormatNullable(double? value)
{
    return value is null ? "null" : value.Value.ToString("G17");
}

public sealed record GeneratorConfig
{
    public int Length { get; init; } = 360;
    public double BasePrice { get; init; } = 100.0;
    public double TrendStep { get; init; } = 0.18;
    public double OpenWaveAmplitude { get; init; } = 2.4;
    public double CloseWaveAmplitude { get; init; } = 1.1;
    public double HighPadding { get; init; } = 1.35;
    public double LowPadding { get; init; } = 1.25;
    public double VolumeBase { get; init; } = 1200.0;
    public double VolumeWaveAmplitude { get; init; } = 95.0;
}

public sealed record AlignmentCase
{
    public string Name { get; init; } = string.Empty;
    public string Indicator { get; init; } = string.Empty;
    public List<string> RealSources { get; init; } = new();
    public List<double> Parameters { get; init; } = new();
}

public sealed record AlignmentCasesRoot
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public double Tolerance { get; init; } = 1e-10;
    public GeneratorConfig? Generator { get; init; }
    public List<AlignmentCase> Cases { get; init; } = new();
}

public sealed record FrontendBaselineCase
{
    public string Name { get; init; } = string.Empty;
    public string Indicator { get; init; } = string.Empty;
    public List<string> OutputNames { get; init; } = new();
    public List<List<double?>> Outputs { get; init; } = new();
}

public sealed record FrontendBaselineRoot
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public int Length { get; init; }
    public int CaseCount { get; init; }
    public List<FrontendBaselineCase> Cases { get; init; } = new();
}

public sealed record MarketSeries(double[] Open, double[] High, double[] Low, double[] Close, double[] Volume)
{
    public int Length => Open.Length;
}

public sealed record BackendCaseResult(string Name, string Indicator, List<string> OutputNames, List<List<double?>> Outputs);

public sealed record CompareResult(bool Success, double CaseMaxDiff, List<string> Details);

public sealed class WasmBridgeClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly object _sync = new();
    private readonly int _requestTimeoutMs;
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private long _nextId;
    private bool _disposed;

    public WasmBridgeClient(
        string nodeExecutable,
        string bridgeScriptPath,
        string metaPath,
        string wasmPath,
        int startupTimeoutMs,
        int requestTimeoutMs)
    {
        if (!File.Exists(bridgeScriptPath))
        {
            throw new FileNotFoundException($"桥接脚本不存在: {bridgeScriptPath}", bridgeScriptPath);
        }

        if (!File.Exists(metaPath))
        {
            throw new FileNotFoundException($"meta 文件不存在: {metaPath}", metaPath);
        }

        if (!File.Exists(wasmPath))
        {
            throw new FileNotFoundException($"wasm 文件不存在: {wasmPath}", wasmPath);
        }

        _requestTimeoutMs = Math.Max(1000, requestTimeoutMs);

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(nodeExecutable) ? "node" : nodeExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add(Path.GetFullPath(bridgeScriptPath));
        startInfo.ArgumentList.Add("--meta");
        startInfo.ArgumentList.Add(Path.GetFullPath(metaPath));
        startInfo.ArgumentList.Add("--wasm");
        startInfo.ArgumentList.Add(Path.GetFullPath(wasmPath));

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("启动 Node 桥接进程失败");

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                Console.WriteLine($"[WasmBridge] {eventArgs.Data}");
            }
        };
        _process.BeginErrorReadLine();

        var pingId = Interlocked.Increment(ref _nextId);
        WriteRequest(new BridgeRequest
        {
            Id = pingId,
            Type = "ping",
        });

        var pingResponse = ReadResponse(startupTimeoutMs);
        if (!pingResponse.Ok || !string.Equals(pingResponse.Type, "pong", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"WasmBridge 握手失败: {pingResponse.Error ?? "unknown"}");
        }
    }

    public List<List<double?>> Compute(
        string indicator,
        double[][] inputs,
        double[] options,
        int expectedLength,
        int expectedOutputCount,
        string caseName)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var requestId = Interlocked.Increment(ref _nextId);
            WriteRequest(new BridgeRequest
            {
                Id = requestId,
                Type = "compute",
                Indicator = indicator,
                Inputs = inputs,
                Options = options,
                ExpectedLength = expectedLength,
            });

            var response = ReadResponse(_requestTimeoutMs);
            if (response.Id != requestId)
            {
                throw new InvalidOperationException($"WasmBridge 返回 ID 不匹配: expected={requestId}, actual={response.Id}");
            }

            if (!response.Ok)
            {
                throw new InvalidOperationException($"WasmBridge 计算失败: case={caseName}, error={response.Error}");
            }

            var outputList = new List<List<double?>>();
            var sourceOutputs = response.Outputs ?? new List<List<double?>>();
            var count = Math.Max(expectedOutputCount, sourceOutputs.Count);
            for (var i = 0; i < count; i++)
            {
                var source = i < sourceOutputs.Count ? sourceOutputs[i] : new List<double?>();
                outputList.Add(NormalizeOutput(source, expectedLength));
            }

            return outputList;
        }
    }

    private void WriteRequest(BridgeRequest request)
    {
        var line = JsonSerializer.Serialize(request, JsonOptions);
        _stdin.WriteLine(line);
        _stdin.Flush();
    }

    private BridgeResponse ReadResponse(int timeoutMs)
    {
        var timeout = Math.Max(1000, timeoutMs);
        var task = _stdout.ReadLineAsync();
        var completed = Task.WhenAny(task, Task.Delay(timeout)).GetAwaiter().GetResult();
        if (completed != task)
        {
            throw new TimeoutException($"读取 WasmBridge 超时: {timeout}ms");
        }

        var line = task.GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("WasmBridge 返回空行");
        }

        var response = JsonSerializer.Deserialize<BridgeResponse>(line, JsonOptions);
        if (response == null)
        {
            throw new InvalidOperationException("WasmBridge 返回解析为空");
        }

        return response;
    }

    private static List<double?> NormalizeOutput(List<double?> source, int expectedLength)
    {
        var length = Math.Max(0, expectedLength);
        if (length == 0)
        {
            return new List<double?>();
        }

        var output = Enumerable.Repeat<double?>(null, length).ToList();
        if (source == null || source.Count == 0)
        {
            return output;
        }

        var limit = Math.Min(length, source.Count);
        var startIndex = Math.Max(0, length - limit);
        for (var i = 0; i < limit; i++)
        {
            output[startIndex + i] = source[i];
        }

        return output;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WasmBridgeClient));
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

            try
            {
                _stdin.Dispose();
                _stdout.Dispose();
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(1500);
                }
            }
            catch
            {
                // 忽略释放阶段异常
            }
            finally
            {
                _process.Dispose();
                _disposed = true;
            }
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

        public string? Type { get; set; }

        public string? Error { get; set; }

        public List<List<double?>>? Outputs { get; set; }
    }
}
