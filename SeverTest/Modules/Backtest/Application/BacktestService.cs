using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Application
{
    public sealed class BacktestService : BaseService
    {
        private const string DefaultSingleScopeKey = "single:primary";
        private const string PortfolioScopePrefix = "portfolio:strategy";

        private readonly BacktestRunner _runner;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly DatabaseService _db;
        private readonly ContractDetailsCacheService _contractCache;
        private readonly BacktestProgressPushService _progressPushService;

        public BacktestService(
            BacktestRunner runner,
            StrategyJsonLoader strategyLoader,
            DatabaseService db,
            ContractDetailsCacheService contractCache,
            BacktestProgressPushService progressPushService,
            ILogger<BacktestService> logger)
            : base(logger)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _contractCache = contractCache ?? throw new ArgumentNullException(nameof(contractCache));
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
        }

        public async Task<BacktestRunResult> RunAsync(
            BacktestRunRequest request,
            string? reqId,
            long? userId,
            long? taskId,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var progressContext = new BacktestProgressContext
            {
                ReqId = reqId,
                UserId = userId,
                TaskId = taskId
            };

            try
            {
                // 合约详情为共享缓存，组合回测前统一预热，避免执行阶段重复初始化。
                await _contractCache.InitializeAsync(ct).ConfigureAwait(false);

                var plans = await BuildExecutionPlansAsync(request, ct).ConfigureAwait(false);
                if (plans.Count <= 1)
                {
                    var single = plans[0];
                    Logger.LogInformation(
                        "回测按单策略执行：Scope={Scope} StrategyIndex={Index}",
                        single.ScopeKey,
                        single.StrategyIndex);
                    return await _runner
                        .RunAsync(single.Request, single.Config, progressContext, ct)
                        .ConfigureAwait(false);
                }

                Logger.LogInformation("回测按多策略组合执行：策略数={Count}", plans.Count);
                return await RunPortfolioAsync(request, plans, progressContext, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await _progressPushService
                    .PublishStageAsync(
                        progressContext,
                        "failed",
                        "回测失败",
                        $"回测执行失败: {ex.Message}",
                        null,
                        null,
                        null,
                        true,
                        ct)
                    .ConfigureAwait(false);
                throw;
            }
        }

        private async Task<List<BacktestStrategyExecutionPlan>> BuildExecutionPlansAsync(
            BacktestRunRequest request,
            CancellationToken ct)
        {
            var outputOptions = GetOutputOptions(request.Output);
            var strategyItems = request.Strategies?
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();

            var configCache = new Dictionary<long, string?>();
            var usedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plans = new List<BacktestStrategyExecutionPlan>();

            if (strategyItems == null || strategyItems.Count == 0)
            {
                var singleRequest = CloneRequest(request);
                singleRequest.Strategies = null;
                singleRequest.Output = outputOptions;
                var singleStrategyIndex = singleRequest.StrategyIndex.GetValueOrDefault(1);
                if (singleStrategyIndex <= 0)
                {
                    singleStrategyIndex = 1;
                }

                singleRequest.StrategyIndex = singleStrategyIndex;
                singleRequest.StrategyScopeKey = BuildUniqueScopeKey(
                    singleRequest.StrategyScopeKey,
                    singleStrategyIndex,
                    usedScopes,
                    allowDefaultSingleScope: true);

                var singleConfig = await ResolveStrategyConfigAsync(singleRequest, configCache, ct).ConfigureAwait(false);
                plans.Add(new BacktestStrategyExecutionPlan(
                    singleStrategyIndex,
                    singleRequest.StrategyScopeKey!,
                    singleRequest,
                    singleConfig));
                return plans;
            }

            if (strategyItems.Count == 1)
            {
                var singleLegRequest = BuildStrategyLegRequest(
                    request,
                    strategyItems[0],
                    1,
                    outputOptions,
                    usedScopes,
                    forceFullOutputForMerge: false,
                    allowDefaultSingleScope: true);
                var singleLegConfig = await ResolveStrategyConfigAsync(singleLegRequest, configCache, ct).ConfigureAwait(false);

                plans.Add(new BacktestStrategyExecutionPlan(
                    1,
                    singleLegRequest.StrategyScopeKey!,
                    singleLegRequest,
                    singleLegConfig));
                return plans;
            }

            for (var i = 0; i < strategyItems.Count; i++)
            {
                var strategyIndex = i + 1;
                var legRequest = BuildStrategyLegRequest(
                    request,
                    strategyItems[i],
                    strategyIndex,
                    outputOptions,
                    usedScopes,
                    forceFullOutputForMerge: true,
                    allowDefaultSingleScope: false);
                var legConfig = await ResolveStrategyConfigAsync(legRequest, configCache, ct).ConfigureAwait(false);

                plans.Add(new BacktestStrategyExecutionPlan(
                    strategyIndex,
                    legRequest.StrategyScopeKey!,
                    legRequest,
                    legConfig));
            }

            return plans;
        }

        private BacktestRunRequest BuildStrategyLegRequest(
            BacktestRunRequest source,
            BacktestStrategyRequestItem leg,
            int strategyIndex,
            BacktestOutputOptions sourceOutput,
            HashSet<string> usedScopes,
            bool forceFullOutputForMerge,
            bool allowDefaultSingleScope)
        {
            var request = CloneRequest(source);
            request.Strategies = null;

            request.UsId = leg.UsId ?? source.UsId;
            request.ConfigJson = string.IsNullOrWhiteSpace(leg.ConfigJson) ? source.ConfigJson : leg.ConfigJson;

            if (!string.IsNullOrWhiteSpace(leg.Exchange))
            {
                request.Exchange = leg.Exchange;
            }

            if (!string.IsNullOrWhiteSpace(leg.Timeframe))
            {
                request.Timeframe = leg.Timeframe;
            }

            if (leg.Symbols != null && leg.Symbols.Count > 0)
            {
                request.Symbols = leg.Symbols
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            request.StrategyIndex = strategyIndex;
            request.StrategyScopeKey = BuildUniqueScopeKey(
                leg.StrategyScopeKey ?? source.StrategyScopeKey,
                strategyIndex,
                usedScopes,
                allowDefaultSingleScope: allowDefaultSingleScope);

            if (forceFullOutputForMerge)
            {
                // 组合回测需要完整交易与权益序列用于合并，内部强制打开；
                // 最终返回前会按用户原始输出选项裁剪。
                request.Output = new BacktestOutputOptions
                {
                    IncludeTrades = true,
                    IncludeEquityCurve = true,
                    IncludeEvents = sourceOutput.IncludeEvents,
                    EquityCurveGranularity = sourceOutput.EquityCurveGranularity
                };
            }
            else
            {
                request.Output = sourceOutput;
            }

            return request;
        }

        private async Task<StrategyConfig> ResolveStrategyConfigAsync(
            BacktestRunRequest request,
            Dictionary<long, string?> configCache,
            CancellationToken ct)
        {
            var configJson = request.ConfigJson;
            if (string.IsNullOrWhiteSpace(configJson))
            {
                if (!request.UsId.HasValue || request.UsId.Value <= 0)
                {
                    throw new InvalidOperationException("缺少策略配置或策略实例ID");
                }

                if (!configCache.TryGetValue(request.UsId.Value, out configJson))
                {
                    configJson = await LoadConfigJsonAsync(request.UsId.Value, ct).ConfigureAwait(false);
                    configCache[request.UsId.Value] = configJson;
                }

                if (string.IsNullOrWhiteSpace(configJson))
                {
                    throw new InvalidOperationException($"未找到策略配置: usId={request.UsId.Value}");
                }
            }

            var config = _strategyLoader.ParseConfig(configJson);
            if (config == null)
            {
                throw new InvalidOperationException("策略配置解析失败");
            }

            return config;
        }

        private async Task<BacktestRunResult> RunPortfolioAsync(
            BacktestRunRequest originalRequest,
            IReadOnlyList<BacktestStrategyExecutionPlan> plans,
            BacktestProgressContext progressContext,
            CancellationToken ct)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var strategyCount = plans.Count;
            var runs = new List<BacktestStrategyRunOutput>(strategyCount);

            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "portfolio_prepare",
                    "组合回测准备",
                    $"组合回测启动，共 {strategyCount} 条策略",
                    0,
                    strategyCount,
                    stopwatch.ElapsedMilliseconds,
                    false,
                    ct)
                .ConfigureAwait(false);

            for (var i = 0; i < plans.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var plan = plans[i];

                await _progressPushService
                    .PublishStageAsync(
                        progressContext,
                        "portfolio_run",
                        "组合回测执行",
                        $"正在执行第 {i + 1}/{strategyCount} 条策略（scope={plan.ScopeKey}）",
                        i,
                        strategyCount,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        ct)
                    .ConfigureAwait(false);

                // 组合模式下由外层统一控制进度，子策略执行时关闭子进度推送。
                var result = await _runner
                    .RunAsync(plan.Request, plan.Config, null, ct)
                    .ConfigureAwait(false);
                runs.Add(new BacktestStrategyRunOutput(plan, result));

                await _progressPushService
                    .PublishStageAsync(
                        progressContext,
                        "portfolio_run",
                        "组合回测执行",
                        $"第 {i + 1}/{strategyCount} 条策略执行完成（scope={plan.ScopeKey}）",
                        i + 1,
                        strategyCount,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        ct)
                    .ConfigureAwait(false);
            }

            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "portfolio_merge",
                    "组合结果合并",
                    "正在合并多策略交易与资金曲线",
                    strategyCount,
                    strategyCount,
                    stopwatch.ElapsedMilliseconds,
                    false,
                    ct)
                .ConfigureAwait(false);

            var resultOptions = GetOutputOptions(originalRequest.Output);
            var merged = BuildPortfolioResult(originalRequest, runs, stopwatch.ElapsedMilliseconds);
            ApplyOutputFilter(merged, resultOptions);

            stopwatch.Stop();
            merged.DurationMs = stopwatch.ElapsedMilliseconds;

            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "completed",
                    "回测完成",
                    "组合回测执行完成，已返回完整结果",
                    strategyCount,
                    strategyCount,
                    merged.DurationMs,
                    true,
                    ct)
                .ConfigureAwait(false);

            return merged;
        }

        private BacktestRunResult BuildPortfolioResult(
            BacktestRunRequest originalRequest,
            IReadOnlyList<BacktestStrategyRunOutput> runs,
            long elapsedMs)
        {
            if (runs.Count == 0)
            {
                return new BacktestRunResult
                {
                    DurationMs = elapsedMs
                };
            }

            var sharedInitialCapital = originalRequest.InitialCapital != 0m
                ? originalRequest.InitialCapital
                : runs[0].Plan.Request.InitialCapital;
            var symbols = FlattenSymbolResults(runs);
            var trades = CollectPortfolioTrades(runs);
            var totalEquity = BuildPortfolioEquityCurve(runs);

            var exchange = ResolveAggregateValue(runs.Select(item => item.Result.Exchange), "multi");
            var timeframe = ResolveAggregateValue(runs.Select(item => item.Result.Timeframe), "multi");
            var granularity = ResolveAggregateValue(
                runs.Select(item => item.Result.EquityCurveGranularity),
                string.IsNullOrWhiteSpace(runs[0].Result.EquityCurveGranularity) ? "1m" : runs[0].Result.EquityCurveGranularity);
            var startTimestamp = ResolveStartTimestamp(runs);
            var endTimestamp = ResolveEndTimestamp(runs);
            var totalBars = totalEquity.Count > 0
                ? totalEquity.Count
                : runs.Max(item => item.Result.TotalBars);

            return new BacktestRunResult
            {
                Exchange = exchange,
                Timeframe = timeframe,
                EquityCurveGranularity = granularity,
                StartTimestamp = startTimestamp,
                EndTimestamp = endTimestamp,
                TotalBars = totalBars,
                DurationMs = elapsedMs,
                TotalEquityCurveRaw = BacktestStatisticsBuilder.SerializeToRawList(totalEquity),
                TotalEquitySummary = BacktestStatisticsBuilder.BuildEquitySummary(totalEquity),
                TotalStats = BacktestStatisticsBuilder.BuildStats(trades, totalEquity, sharedInitialCapital),
                Symbols = symbols
            };
        }

        private static List<BacktestSymbolResult> FlattenSymbolResults(IReadOnlyList<BacktestStrategyRunOutput> runs)
        {
            var list = new List<BacktestSymbolResult>();
            foreach (var run in runs)
            {
                foreach (var symbolResult in run.Result.Symbols)
                {
                    var clone = CloneSymbolResult(symbolResult);
                    clone.StrategyScopeKey = string.IsNullOrWhiteSpace(clone.StrategyScopeKey)
                        ? run.Plan.ScopeKey
                        : clone.StrategyScopeKey;
                    clone.StrategyIndex = clone.StrategyIndex.GetValueOrDefault() > 0
                        ? clone.StrategyIndex
                        : run.Plan.StrategyIndex;
                    list.Add(clone);
                }
            }

            return list;
        }

        private List<BacktestTrade> CollectPortfolioTrades(IReadOnlyList<BacktestStrategyRunOutput> runs)
        {
            var trades = new List<BacktestTrade>(2048);

            foreach (var run in runs)
            {
                foreach (var symbolResult in run.Result.Symbols)
                {
                    if (symbolResult.TradesRaw == null || symbolResult.TradesRaw.Count == 0)
                    {
                        continue;
                    }

                    foreach (var raw in symbolResult.TradesRaw)
                    {
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            continue;
                        }

                        BacktestTrade? trade;
                        try
                        {
                            trade = ProtocolJson.Deserialize<BacktestTrade>(raw);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(
                                ex,
                                "组合回测解析交易明细失败：scope={Scope} symbol={Symbol}",
                                run.Plan.ScopeKey,
                                symbolResult.Symbol);
                            continue;
                        }

                        if (trade == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(trade.StrategyScopeKey))
                        {
                            trade.StrategyScopeKey = run.Plan.ScopeKey;
                        }

                        if (!trade.StrategyIndex.HasValue || trade.StrategyIndex.Value <= 0)
                        {
                            trade.StrategyIndex = run.Plan.StrategyIndex;
                        }

                        trades.Add(trade);
                    }
                }
            }

            trades.Sort((left, right) =>
            {
                var exitCompare = left.ExitTime.CompareTo(right.ExitTime);
                if (exitCompare != 0) return exitCompare;
                var entryCompare = left.EntryTime.CompareTo(right.EntryTime);
                if (entryCompare != 0) return entryCompare;
                return string.Compare(left.Symbol, right.Symbol, StringComparison.OrdinalIgnoreCase);
            });

            return trades;
        }

        private List<BacktestEquityPoint> BuildPortfolioEquityCurve(IReadOnlyList<BacktestStrategyRunOutput> runs)
        {
            var timeline = new SortedSet<long>();
            var cursors = new List<PortfolioEquityCursor>(runs.Count);

            foreach (var run in runs)
            {
                var curve = ParseEquityCurveRaw(run.Result.TotalEquityCurveRaw);
                if (curve.Count == 0)
                {
                    continue;
                }

                var legInitialBaseline = run.Result.Symbols.Sum(item => item.InitialCapital);
                if (legInitialBaseline <= 0m)
                {
                    legInitialBaseline = run.Plan.Request.InitialCapital * Math.Max(1, run.Result.Symbols.Count);
                }

                var normalized = new List<BacktestEquityPoint>(curve.Count);
                foreach (var point in curve)
                {
                    normalized.Add(new BacktestEquityPoint
                    {
                        Timestamp = point.Timestamp,
                        Equity = point.Equity - legInitialBaseline,
                        RealizedPnl = point.RealizedPnl,
                        UnrealizedPnl = point.UnrealizedPnl,
                        PeriodRealizedPnl = point.PeriodRealizedPnl,
                        PeriodUnrealizedPnl = point.PeriodUnrealizedPnl
                    });

                    if (point.Timestamp > 0)
                    {
                        timeline.Add(point.Timestamp);
                    }
                }

                if (normalized.Count > 0)
                {
                    cursors.Add(new PortfolioEquityCursor(normalized));
                }
            }

            if (cursors.Count == 0 || timeline.Count == 0)
            {
                return new List<BacktestEquityPoint>();
            }

            var sharedInitialCapital = runs[0].Plan.Request.InitialCapital;
            var merged = new List<BacktestEquityPoint>(timeline.Count);
            var previousRealized = 0m;
            var previousUnrealized = 0m;

            foreach (var timestamp in timeline)
            {
                foreach (var cursor in cursors)
                {
                    while (cursor.Index < cursor.Curve.Count && cursor.Curve[cursor.Index].Timestamp <= timestamp)
                    {
                        var point = cursor.Curve[cursor.Index];
                        cursor.CurrentNetEquity = point.Equity;
                        cursor.CurrentRealized = point.RealizedPnl;
                        cursor.CurrentUnrealized = point.UnrealizedPnl;
                        cursor.Index++;
                    }
                }

                var totalNetEquity = 0m;
                var totalRealized = 0m;
                var totalUnrealized = 0m;
                foreach (var cursor in cursors)
                {
                    totalNetEquity += cursor.CurrentNetEquity;
                    totalRealized += cursor.CurrentRealized;
                    totalUnrealized += cursor.CurrentUnrealized;
                }

                var equity = sharedInitialCapital + totalNetEquity;
                var periodRealized = totalRealized - previousRealized;
                var periodUnrealized = totalUnrealized - previousUnrealized;

                merged.Add(new BacktestEquityPoint
                {
                    Timestamp = timestamp,
                    Equity = equity,
                    RealizedPnl = totalRealized,
                    UnrealizedPnl = totalUnrealized,
                    PeriodRealizedPnl = periodRealized,
                    PeriodUnrealizedPnl = periodUnrealized
                });

                previousRealized = totalRealized;
                previousUnrealized = totalUnrealized;
            }

            return merged;
        }

        private List<BacktestEquityPoint> ParseEquityCurveRaw(IReadOnlyList<string>? rawCurve)
        {
            var points = new List<BacktestEquityPoint>();
            if (rawCurve == null || rawCurve.Count == 0)
            {
                return points;
            }

            foreach (var raw in rawCurve)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                try
                {
                    var point = ProtocolJson.Deserialize<BacktestEquityPoint>(raw);
                    if (point != null)
                    {
                        points.Add(point);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "组合回测解析资金曲线失败");
                }
            }

            points.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            return points;
        }

        private static long ResolveStartTimestamp(IReadOnlyList<BacktestStrategyRunOutput> runs)
        {
            long? min = null;
            foreach (var run in runs)
            {
                if (run.Result.StartTimestamp <= 0)
                {
                    continue;
                }

                min = !min.HasValue || run.Result.StartTimestamp < min.Value
                    ? run.Result.StartTimestamp
                    : min;
            }

            return min ?? 0L;
        }

        private static long ResolveEndTimestamp(IReadOnlyList<BacktestStrategyRunOutput> runs)
        {
            long max = 0L;
            foreach (var run in runs)
            {
                if (run.Result.EndTimestamp > max)
                {
                    max = run.Result.EndTimestamp;
                }
            }

            return max;
        }

        private static string ResolveAggregateValue(IEnumerable<string?> values, string mixedValue)
        {
            var distinct = values
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                return string.Empty;
            }

            if (distinct.Count == 1)
            {
                return distinct[0];
            }

            return mixedValue;
        }

        private static BacktestOutputOptions GetOutputOptions(BacktestOutputOptions? source)
        {
            source ??= new BacktestOutputOptions();
            return new BacktestOutputOptions
            {
                IncludeTrades = source.IncludeTrades,
                IncludeEquityCurve = source.IncludeEquityCurve,
                IncludeEvents = source.IncludeEvents,
                EquityCurveGranularity = string.IsNullOrWhiteSpace(source.EquityCurveGranularity)
                    ? "1m"
                    : source.EquityCurveGranularity
            };
        }

        private static void ApplyOutputFilter(BacktestRunResult result, BacktestOutputOptions options)
        {
            if (!options.IncludeTrades)
            {
                foreach (var symbol in result.Symbols)
                {
                    symbol.TradesRaw = new List<string>();
                }
            }

            if (!options.IncludeEquityCurve)
            {
                result.TotalEquityCurveRaw = new List<string>();
                result.TotalEquitySummary = new BacktestEquitySummary();
                foreach (var symbol in result.Symbols)
                {
                    symbol.EquityCurveRaw = new List<string>();
                }
            }

            if (!options.IncludeEvents)
            {
                foreach (var symbol in result.Symbols)
                {
                    symbol.EventsRaw = new List<string>();
                }
            }
        }

        private static string BuildUniqueScopeKey(
            string? rawScopeKey,
            int strategyIndex,
            HashSet<string> usedScopes,
            bool allowDefaultSingleScope)
        {
            var baseScope = string.IsNullOrWhiteSpace(rawScopeKey)
                ? (allowDefaultSingleScope ? DefaultSingleScopeKey : $"{PortfolioScopePrefix}:{strategyIndex}")
                : rawScopeKey.Trim();

            var candidate = baseScope;
            var suffix = 2;
            while (!usedScopes.Add(candidate))
            {
                candidate = $"{baseScope}_{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static BacktestRunRequest CloneRequest(BacktestRunRequest source)
        {
            return new BacktestRunRequest
            {
                Strategies = source.Strategies?.ToList(),
                StrategyScopeKey = source.StrategyScopeKey,
                StrategyIndex = source.StrategyIndex,
                UsId = source.UsId,
                ConfigJson = source.ConfigJson,
                Exchange = source.Exchange,
                Symbols = source.Symbols?.ToList() ?? new List<string>(),
                Timeframe = source.Timeframe,
                StartTime = source.StartTime,
                EndTime = source.EndTime,
                BarCount = source.BarCount,
                InitialCapital = source.InitialCapital,
                OrderQtyOverride = source.OrderQtyOverride,
                LeverageOverride = source.LeverageOverride,
                TakeProfitPctOverride = source.TakeProfitPctOverride,
                StopLossPctOverride = source.StopLossPctOverride,
                FeeRate = source.FeeRate,
                FundingRate = source.FundingRate,
                SlippageBps = source.SlippageBps,
                AutoReverse = source.AutoReverse,
                Runtime = source.Runtime,
                UseStrategyRuntime = source.UseStrategyRuntime,
                ExecutionMode = source.ExecutionMode,
                Output = source.Output == null
                    ? null
                    : new BacktestOutputOptions
                    {
                        IncludeTrades = source.Output.IncludeTrades,
                        IncludeEquityCurve = source.Output.IncludeEquityCurve,
                        IncludeEvents = source.Output.IncludeEvents,
                        EquityCurveGranularity = source.Output.EquityCurveGranularity
                    }
            };
        }

        private static BacktestSymbolResult CloneSymbolResult(BacktestSymbolResult source)
        {
            return new BacktestSymbolResult
            {
                Symbol = source.Symbol,
                StrategyScopeKey = source.StrategyScopeKey,
                StrategyIndex = source.StrategyIndex,
                Bars = source.Bars,
                InitialCapital = source.InitialCapital,
                Stats = source.Stats,
                TradeSummary = source.TradeSummary,
                EquitySummary = source.EquitySummary,
                EventSummary = source.EventSummary,
                TradesRaw = source.TradesRaw?.ToList() ?? new List<string>(),
                EquityCurveRaw = source.EquityCurveRaw?.ToList() ?? new List<string>(),
                EventsRaw = source.EventsRaw?.ToList() ?? new List<string>()
            };
        }

        private async Task<string?> LoadConfigJsonAsync(long usId, CancellationToken ct)
        {
            await using var connection = await _db.GetConnectionAsync().ConfigureAwait(false);
            var cmd = new MySqlCommand(@"
SELECT sv.config_json
FROM user_strategy us
JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.us_id = @us_id
LIMIT 1
", connection);
            cmd.Parameters.AddWithValue("@us_id", usId);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return Convert.ToString(result);
        }

        private sealed class BacktestStrategyExecutionPlan
        {
            public BacktestStrategyExecutionPlan(
                int strategyIndex,
                string scopeKey,
                BacktestRunRequest request,
                StrategyConfig config)
            {
                StrategyIndex = strategyIndex;
                ScopeKey = scopeKey;
                Request = request;
                Config = config;
            }

            public int StrategyIndex { get; }
            public string ScopeKey { get; }
            public BacktestRunRequest Request { get; }
            public StrategyConfig Config { get; }
        }

        private sealed class BacktestStrategyRunOutput
        {
            public BacktestStrategyRunOutput(
                BacktestStrategyExecutionPlan plan,
                BacktestRunResult result)
            {
                Plan = plan;
                Result = result;
            }

            public BacktestStrategyExecutionPlan Plan { get; }
            public BacktestRunResult Result { get; }
        }

        private sealed class PortfolioEquityCursor
        {
            public PortfolioEquityCursor(List<BacktestEquityPoint> curve)
            {
                Curve = curve;
            }

            public List<BacktestEquityPoint> Curve { get; }
            public int Index { get; set; }
            public decimal CurrentNetEquity { get; set; }
            public decimal CurrentRealized { get; set; }
            public decimal CurrentUnrealized { get; set; }
        }
    }
}
