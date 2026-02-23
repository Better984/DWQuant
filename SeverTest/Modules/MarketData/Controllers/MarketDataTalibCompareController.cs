using System.Text.Json;
using System.Text.Json.Serialization;
using ccxt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/MarketData")]
    public sealed class MarketDataTalibCompareController : BaseController
    {
        private static readonly Dictionary<string, string> TalibCodeAlias = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CONCEALINGBABYSWALLOW"] = "CDLCONCEALBABYSWALL",
            ["GAPSIDEBYSIDEWHITELINES"] = "CDLGAPSIDESIDEWHITE",
            ["HIKKAKEMODIFIED"] = "CDLHIKKAKEMOD",
            ["IDENTICALTHREECROWS"] = "CDLIDENTICAL3CROWS",
            ["PIERCINGLINE"] = "CDLPIERCING",
            ["RISINGFALLINGTHREEMETHODS"] = "CDLRISEFALL3METHODS",
            ["TAKURILINE"] = "CDLTAKURI",
            ["UNIQUETHREERIVER"] = "CDLUNIQUE3RIVER",
            ["UPDOWNSIDEGAPTHREEMETHODS"] = "CDLXSIDEGAP3METHODS",
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Lazy<TalibRandomCatalog> CatalogLoader = new(LoadTalibCatalog);

        private readonly HistoricalMarketDataCache _historicalCache;
        private readonly TalibWasmNodeInvoker _wasmInvoker;

        public MarketDataTalibCompareController(
            ILogger<MarketDataTalibCompareController> logger,
            HistoricalMarketDataCache historicalCache,
            TalibWasmNodeInvoker wasmInvoker) : base(logger)
        {
            _historicalCache = historicalCache ?? throw new ArgumentNullException(nameof(historicalCache));
            _wasmInvoker = wasmInvoker ?? throw new ArgumentNullException(nameof(wasmInvoker));
        }

        public sealed class TaRandomCompareRequest
        {
            public int Bars { get; set; } = 2000;
            public int? Seed { get; set; }
        }

        public sealed class TaRandomCompareResponse
        {
            public TaRandomSampleDto Sample { get; set; } = new();
            public List<MarketKlineDto> Klines { get; set; } = new();
            public int IndicatorCount { get; set; }
            public int SuccessCount { get; set; }
            public int FailedCount { get; set; }
            public List<TaIndicatorResultDto> Indicators { get; set; } = new();
        }

        public sealed class TaRandomSampleDto
        {
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Timeframe { get; set; } = string.Empty;
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
            public int Bars { get; set; }
            public int WindowStartIndex { get; set; }
            public int TotalCachedBars { get; set; }
            public int? Seed { get; set; }
        }

        public sealed class MarketKlineDto
        {
            public long Timestamp { get; set; }
            public double? Open { get; set; }
            public double? High { get; set; }
            public double? Low { get; set; }
            public double? Close { get; set; }
            public double? Volume { get; set; }
        }

        public sealed class TaIndicatorResultDto
        {
            public string IndicatorCode { get; set; } = string.Empty;
            public string TalibCode { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<TaInputBindingDto> Inputs { get; set; } = new();
            public List<TaOptionDto> Options { get; set; } = new();
            public List<string> OutputNames { get; set; } = new();
            public List<List<double?>> Outputs { get; set; } = new();
            public string? Error { get; set; }
        }

        public sealed class TaInputBindingDto
        {
            public string Name { get; set; } = string.Empty;
            public string Source { get; set; } = "CLOSE";
        }

        public sealed class TaOptionDto
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "Double";
            public double Value { get; set; }
        }

        [ProtocolType("marketdata.ta.random.compare")]
        [HttpPost("ta-random-compare")]
        public IActionResult RunRandomCompare([FromBody] ProtocolRequest<TaRandomCompareRequest> request)
        {
            try
            {
                var payload = request.Data ?? new TaRandomCompareRequest();
                var bars = Math.Clamp(payload.Bars <= 0 ? 2000 : payload.Bars, 50, 2000);
                var random = payload.Seed.HasValue ? new Random(payload.Seed.Value) : Random.Shared;

                if (!_wasmInvoker.IsEnabled)
                {
                    return BadRequest(ApiResponse<object>.Error("当前后端未启用 TalibWasmNode 同核心模式，无法执行一致性随机测试"));
                }

                if (!TrySelectRandomWindow(bars, random, out var selection, out var error))
                {
                    return BadRequest(ApiResponse<object>.Error(error));
                }

                TalibRandomCatalog catalog;
                try
                {
                    catalog = CatalogLoader.Value;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "加载 talib 对比目录失败");
                    return StatusCode(500, ApiResponse<object>.Error($"加载指标目录失败: {ex.Message}"));
                }

                var indicatorResults = new List<TaIndicatorResultDto>(catalog.Indicators.Count);
                var successCount = 0;
                var failedCount = 0;

                foreach (var indicator in catalog.Indicators)
                {
                    var result = new TaIndicatorResultDto
                    {
                        IndicatorCode = indicator.IndicatorCode,
                        TalibCode = indicator.TalibCode,
                        DisplayName = indicator.DisplayName,
                        Inputs = indicator.Inputs.Select(input => new TaInputBindingDto
                        {
                            Name = input.Name,
                            Source = input.Source,
                        }).ToList(),
                        Options = indicator.Options.Select(option => new TaOptionDto
                        {
                            Name = option.Name,
                            Type = option.Type,
                            Value = option.Value,
                        }).ToList(),
                        OutputNames = new List<string>(indicator.OutputNames),
                    };

                    var inputs = BuildInputs(selection.Window, indicator.Inputs);
                    var options = indicator.Options.Select(option => option.Value).ToArray();

                    if (_wasmInvoker.TryCompute(
                        indicator.TalibCode,
                        inputs,
                        options,
                        selection.Window.Count,
                        out var outputs,
                        out var computeError))
                    {
                        var alignedOutputs = AlignOutputs(outputs, indicator.OutputNames.Count, selection.Window.Count);
                        result.Outputs = alignedOutputs.Select(output => output.ToList()).ToList();
                        result.Error = null;
                        successCount++;
                    }
                    else
                    {
                        result.Outputs = new List<List<double?>>();
                        result.Error = string.IsNullOrWhiteSpace(computeError) ? "计算失败" : computeError;
                        failedCount++;
                    }

                    indicatorResults.Add(result);
                }

                var response = new TaRandomCompareResponse
                {
                    Sample = new TaRandomSampleDto
                    {
                        Exchange = selection.Snapshot.Exchange,
                        Symbol = selection.Snapshot.Symbol,
                        Timeframe = selection.Snapshot.Timeframe,
                        StartTime = selection.StartTime.ToString("O"),
                        EndTime = selection.EndTime.ToString("O"),
                        Bars = selection.Window.Count,
                        WindowStartIndex = selection.StartIndex,
                        TotalCachedBars = selection.TotalBars,
                        Seed = payload.Seed,
                    },
                    Klines = selection.Window.Select(ToKlineDto).ToList(),
                    IndicatorCount = catalog.Indicators.Count,
                    SuccessCount = successCount,
                    FailedCount = failedCount,
                    Indicators = indicatorResults,
                };

                Logger.LogInformation(
                    "随机一致性测试完成: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} bars={Bars} success={Success} failed={Failed}",
                    response.Sample.Exchange,
                    response.Sample.Symbol,
                    response.Sample.Timeframe,
                    response.Sample.Bars,
                    response.SuccessCount,
                    response.FailedCount);

                return Ok(ApiResponse<TaRandomCompareResponse>.Ok(response, "随机一致性测试完成"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "随机一致性测试失败");
                return StatusCode(500, ApiResponse<object>.Error($"随机一致性测试失败: {ex.Message}"));
            }
        }

        private bool TrySelectRandomWindow(
            int bars,
            Random random,
            out RandomWindowSelection selection,
            out string error)
        {
            selection = default!;
            error = string.Empty;

            var snapshots = _historicalCache.GetCacheSnapshots()
                .Where(snapshot => snapshot.Count >= bars)
                .ToList();
            if (snapshots.Count == 0)
            {
                error = $"缓存中不存在可用样本（要求至少 {bars} 根 K 线）";
                return false;
            }

            var attempts = Math.Max(20, snapshots.Count * 3);
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var snapshot = snapshots[random.Next(snapshots.Count)];
                if (!_historicalCache.TryGetHistoryFromCache(
                        snapshot.Exchange,
                        snapshot.Timeframe,
                        snapshot.Symbol,
                        null,
                        null,
                        null,
                        out var cached,
                        out var missReason))
                {
                    Logger.LogWarning(
                        "随机一致性测试跳过缓存组合: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} reason={Reason}",
                        snapshot.Exchange,
                        snapshot.Symbol,
                        snapshot.Timeframe,
                        missReason);
                    continue;
                }

                if (cached.Count < bars)
                {
                    continue;
                }

                // 在已缓存序列中随机截取固定长度窗口，保证连续性。
                var startIndex = random.Next(0, cached.Count - bars + 1);
                var window = cached.GetRange(startIndex, bars);
                if (window.Count != bars)
                {
                    continue;
                }

                var startMs = (long)(window[0].timestamp ?? 0);
                var endMs = (long)(window[^1].timestamp ?? 0);
                if (startMs <= 0 || endMs <= 0)
                {
                    continue;
                }

                selection = new RandomWindowSelection(
                    snapshot,
                    cached.Count,
                    startIndex,
                    DateTimeOffset.FromUnixTimeMilliseconds(startMs).LocalDateTime,
                    DateTimeOffset.FromUnixTimeMilliseconds(endMs).LocalDateTime,
                    window);
                return true;
            }

            error = "未能从缓存中抽取到有效的随机连续 K 线样本";
            return false;
        }

        private static MarketKlineDto ToKlineDto(OHLCV candle)
        {
            return new MarketKlineDto
            {
                Timestamp = (long)(candle.timestamp ?? 0),
                Open = candle.open,
                High = candle.high,
                Low = candle.low,
                Close = candle.close,
                Volume = candle.volume,
            };
        }

        private static double[][] BuildInputs(IReadOnlyList<OHLCV> candles, IReadOnlyList<TalibInputBinding> bindings)
        {
            var result = new double[bindings.Count][];
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var values = new double[candles.Count];
                for (var k = 0; k < candles.Count; k++)
                {
                    values[k] = TalibCalcRules.ResolveSourceValue(candles[k], binding.Source);
                }

                result[i] = values;
            }

            return result;
        }

        private static List<double?[]> AlignOutputs(IReadOnlyList<double?[]> outputs, int expectedCount, int expectedLength)
        {
            var result = new List<double?[]>(Math.Max(expectedCount, outputs.Count));
            var count = Math.Max(expectedCount, outputs.Count);

            for (var i = 0; i < count; i++)
            {
                if (i < outputs.Count && outputs[i] != null)
                {
                    var source = outputs[i];
                    if (source.Length == expectedLength)
                    {
                        result.Add(source);
                        continue;
                    }

                    var aligned = new double?[expectedLength];
                    var copy = Math.Min(source.Length, expectedLength);
                    var start = Math.Max(0, expectedLength - copy);
                    // 与前端桥接逻辑保持一致：输出尾部对齐到原始 K 线长度。
                    for (var j = 0; j < copy; j++)
                    {
                        aligned[start + j] = source[j];
                    }
                    result.Add(aligned);
                    continue;
                }

                result.Add(new double?[expectedLength]);
            }

            return result;
        }

        private static TalibRandomCatalog LoadTalibCatalog()
        {
            var configPath = ResolveConfigPath();
            var metaPath = ResolveMetaPath();

            if (!System.IO.File.Exists(configPath))
            {
                throw new FileNotFoundException("未找到 talib 指标配置文件", configPath);
            }

            if (!System.IO.File.Exists(metaPath))
            {
                throw new FileNotFoundException("未找到 talib meta 文件", metaPath);
            }

            var configRoot = JsonSerializer.Deserialize<TalibConfigRoot>(System.IO.File.ReadAllText(configPath), JsonOptions)
                             ?? new TalibConfigRoot();
            var metaRoot = JsonSerializer.Deserialize<Dictionary<string, TalibMetaIndicator>>(System.IO.File.ReadAllText(metaPath), JsonOptions)
                           ?? new Dictionary<string, TalibMetaIndicator>(StringComparer.OrdinalIgnoreCase);

            var commonOptions = configRoot.Common != null
                ? new Dictionary<string, TalibConfigCommonOption>(configRoot.Common, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, TalibConfigCommonOption>(StringComparer.OrdinalIgnoreCase);

            var indicators = new List<TalibIndicatorDefinition>();
            foreach (var configIndicator in configRoot.Indicators ?? Enumerable.Empty<TalibConfigIndicator>())
            {
                var code = NormalizeCode(configIndicator.Code);
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var talibCode = ResolveTalibCode(code, metaRoot);
                if (string.IsNullOrWhiteSpace(talibCode))
                {
                    continue;
                }

                if (!metaRoot.TryGetValue(talibCode, out var metaDef) || metaDef == null)
                {
                    continue;
                }

                var configuredSeries = configIndicator.Inputs?.Series ?? new List<string>();
                var inputs = BuildInputBindings(metaDef, configuredSeries);
                if (inputs.Count == 0)
                {
                    continue;
                }

                var options = BuildOptionBindings(metaDef, configIndicator.Options ?? new List<TalibConfigOption>(), commonOptions);
                var outputNames = BuildOutputNames(metaDef, configIndicator);
                if (outputNames.Count == 0)
                {
                    outputNames.Add("output");
                }

                indicators.Add(new TalibIndicatorDefinition
                {
                    IndicatorCode = code,
                    TalibCode = talibCode,
                    DisplayName = string.IsNullOrWhiteSpace(configIndicator.NameEn) ? code : configIndicator.NameEn!,
                    Inputs = inputs,
                    Options = options,
                    OutputNames = outputNames,
                });
            }

            return new TalibRandomCatalog
            {
                Indicators = indicators,
                ConfigPath = configPath,
                MetaPath = metaPath,
            };
        }

        private static string ResolveConfigPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Config", "talib_indicators_config.json");
        }

        private static string ResolveMetaPath()
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "Config", "talib_web_api_meta.json");
            if (System.IO.File.Exists(direct))
            {
                return direct;
            }

            foreach (var root in BuildProbeRoots())
            {
                var candidate = Path.Combine(root, "Client", "public", "talib_web_api_meta.json");
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return direct;
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

        private static string NormalizeCode(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string? ResolveTalibCode(string code, IReadOnlyDictionary<string, TalibMetaIndicator> meta)
        {
            if (meta.ContainsKey(code))
            {
                return code;
            }

            if (TalibCodeAlias.TryGetValue(code, out var alias) && meta.ContainsKey(alias))
            {
                return alias;
            }

            return null;
        }

        private static List<TalibInputBinding> BuildInputBindings(TalibMetaIndicator meta, IReadOnlyList<string> configuredSeries)
        {
            var bindings = new List<TalibInputBinding>();
            if (meta.Inputs == null || meta.Inputs.Count == 0)
            {
                return bindings;
            }

            for (var i = 0; i < meta.Inputs.Count; i++)
            {
                var inputName = meta.Inputs[i].Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(inputName))
                {
                    continue;
                }

                var source = ResolveConfiguredSource(inputName, i, configuredSeries);
                bindings.Add(new TalibInputBinding
                {
                    Name = inputName,
                    Source = source,
                });
            }

            return bindings;
        }

        private static string ResolveConfiguredSource(string inputName, int inputIndex, IReadOnlyList<string> configuredSeries)
        {
            var normalizedName = NormalizeInputName(inputName);
            if (normalizedName == "OPEN")
            {
                return "OPEN";
            }

            if (normalizedName == "HIGH")
            {
                return "HIGH";
            }

            if (normalizedName == "LOW")
            {
                return "LOW";
            }

            if (normalizedName == "CLOSE")
            {
                return "CLOSE";
            }

            if (normalizedName == "VOLUME")
            {
                return "VOLUME";
            }

            if (normalizedName is "INREAL" or "INREAL0" or "INREAL1" or "INPERIODS")
            {
                var configured = inputIndex < configuredSeries.Count ? configuredSeries[inputIndex] : "Close";
                return NormalizeConfiguredSource(configured);
            }

            var fallback = inputIndex < configuredSeries.Count ? configuredSeries[inputIndex] : "Close";
            return NormalizeConfiguredSource(fallback);
        }

        private static string NormalizeInputName(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToUpperInvariant();
        }

        private static string NormalizeConfiguredSource(string? source)
        {
            var normalized = (source ?? string.Empty).Trim().ToUpperInvariant();
            return normalized switch
            {
                "OPEN" => "OPEN",
                "HIGH" => "HIGH",
                "LOW" => "LOW",
                "CLOSE" => "CLOSE",
                "VOLUME" => "VOLUME",
                "REAL" => "CLOSE",
                "PERIODS" => "CLOSE",
                _ => TalibCalcRules.NormalizeInputSource(normalized),
            };
        }

        private static List<TalibOptionBinding> BuildOptionBindings(
            TalibMetaIndicator meta,
            IReadOnlyList<TalibConfigOption> configOptions,
            IReadOnlyDictionary<string, TalibConfigCommonOption> commonOptions)
        {
            var bindings = new List<TalibOptionBinding>();
            if (meta.Options == null || meta.Options.Count == 0)
            {
                return bindings;
            }

            for (var i = 0; i < meta.Options.Count; i++)
            {
                var option = meta.Options[i];
                var optionName = option.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(optionName))
                {
                    continue;
                }

                var configOption = i < configOptions.Count ? configOptions[i] : null;
                var defaultValue = ResolveOptionDefaultValue(option, configOption, commonOptions);
                var normalizedValue = NormalizeOptionValue(defaultValue, option.Type);
                bindings.Add(new TalibOptionBinding
                {
                    Name = optionName,
                    Type = string.IsNullOrWhiteSpace(option.Type) ? "Double" : option.Type!,
                    Value = normalizedValue,
                });
            }

            return bindings;
        }

        private static double ResolveOptionDefaultValue(
            TalibMetaOption option,
            TalibConfigOption? configOption,
            IReadOnlyDictionary<string, TalibConfigCommonOption> commonOptions)
        {
            if (option.DefaultValue.HasValue && double.IsFinite(option.DefaultValue.Value))
            {
                return option.DefaultValue.Value;
            }

            if (!string.IsNullOrWhiteSpace(configOption?.Ref))
            {
                var refKey = ExtractRefKey(configOption.Ref!);
                if (!string.IsNullOrWhiteSpace(refKey)
                    && commonOptions.TryGetValue(refKey, out var commonOption)
                    && commonOption.Default.HasValue
                    && double.IsFinite(commonOption.Default.Value))
                {
                    return commonOption.Default.Value;
                }
            }

            return 0;
        }

        private static string ExtractRefKey(string value)
        {
            var parts = value.Split('/');
            return parts.Length == 0 ? string.Empty : parts[^1];
        }

        private static double NormalizeOptionValue(double value, string? optionType)
        {
            var normalizedType = (optionType ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedType is "integer" or "matype")
            {
                return TalibCalcRules.RoundToIntOption(value);
            }

            return value;
        }

        private static List<string> BuildOutputNames(TalibMetaIndicator meta, TalibConfigIndicator configIndicator)
        {
            var outputNames = new List<string>();
            if (meta.Outputs != null)
            {
                outputNames.AddRange(meta.Outputs
                    .Select(output => output.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>());
            }

            if (outputNames.Count > 0)
            {
                return outputNames;
            }

            if (configIndicator.Outputs != null)
            {
                outputNames.AddRange(configIndicator.Outputs
                    .Select(output => !string.IsNullOrWhiteSpace(output.Hint) ? output.Hint : output.Key)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>());
            }

            return outputNames;
        }

        private sealed class RandomWindowSelection
        {
            public RandomWindowSelection(
                HistoricalCacheSnapshot snapshot,
                int totalBars,
                int startIndex,
                DateTime startTime,
                DateTime endTime,
                List<OHLCV> window)
            {
                Snapshot = snapshot;
                TotalBars = totalBars;
                StartIndex = startIndex;
                StartTime = startTime;
                EndTime = endTime;
                Window = window;
            }

            public HistoricalCacheSnapshot Snapshot { get; }
            public int TotalBars { get; }
            public int StartIndex { get; }
            public DateTime StartTime { get; }
            public DateTime EndTime { get; }
            public List<OHLCV> Window { get; }
        }

        private sealed class TalibRandomCatalog
        {
            public string ConfigPath { get; set; } = string.Empty;
            public string MetaPath { get; set; } = string.Empty;
            public List<TalibIndicatorDefinition> Indicators { get; set; } = new();
        }

        private sealed class TalibIndicatorDefinition
        {
            public string IndicatorCode { get; set; } = string.Empty;
            public string TalibCode { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<TalibInputBinding> Inputs { get; set; } = new();
            public List<TalibOptionBinding> Options { get; set; } = new();
            public List<string> OutputNames { get; set; } = new();
        }

        private sealed class TalibInputBinding
        {
            public string Name { get; set; } = string.Empty;
            public string Source { get; set; } = "CLOSE";
        }

        private sealed class TalibOptionBinding
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "Double";
            public double Value { get; set; }
        }

        private sealed class TalibConfigRoot
        {
            [JsonPropertyName("common")]
            public Dictionary<string, TalibConfigCommonOption>? Common { get; set; }

            [JsonPropertyName("indicators")]
            public List<TalibConfigIndicator>? Indicators { get; set; }
        }

        private sealed class TalibConfigCommonOption
        {
            [JsonPropertyName("default")]
            public double? Default { get; set; }
        }

        private sealed class TalibConfigIndicator
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("name_en")]
            public string? NameEn { get; set; }

            [JsonPropertyName("inputs")]
            public TalibConfigInputs? Inputs { get; set; }

            [JsonPropertyName("options")]
            public List<TalibConfigOption>? Options { get; set; }

            [JsonPropertyName("outputs")]
            public List<TalibConfigOutput>? Outputs { get; set; }
        }

        private sealed class TalibConfigInputs
        {
            [JsonPropertyName("series")]
            public List<string>? Series { get; set; }
        }

        private sealed class TalibConfigOption
        {
            [JsonPropertyName("$ref")]
            public string? Ref { get; set; }
        }

        private sealed class TalibConfigOutput
        {
            [JsonPropertyName("key")]
            public string? Key { get; set; }

            [JsonPropertyName("hint")]
            public string? Hint { get; set; }
        }

        private sealed class TalibMetaIndicator
        {
            [JsonPropertyName("inputs")]
            public List<TalibMetaInput>? Inputs { get; set; }

            [JsonPropertyName("options")]
            public List<TalibMetaOption>? Options { get; set; }

            [JsonPropertyName("outputs")]
            public List<TalibMetaOutput>? Outputs { get; set; }
        }

        private sealed class TalibMetaInput
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private sealed class TalibMetaOption
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("defaultValue")]
            public double? DefaultValue { get; set; }
        }

        private sealed class TalibMetaOutput
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }
    }
}

