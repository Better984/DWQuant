using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyEngine.Domain;
using ServerTest.Options;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Modules.Backtest.Application
{
    public sealed class BacktestRunner
    {
        private const int DefaultBarCount = 1000;
        private static readonly long MainLoopProgressIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 2;
        private static readonly long PositionProgressIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 10;
        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);
        private static readonly string[] SupportedEquityGranularities = { "1m", "15m", "1h", "4h", "1d", "3d", "7d" };
        private static readonly Dictionary<string, long> EquityGranularityToMs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 60_000L,
            ["15m"] = 15 * 60_000L,
            ["1h"] = 60 * 60_000L,
            ["4h"] = 4 * 60 * 60_000L,
            ["1d"] = 24 * 60 * 60_000L,
            ["3d"] = 3 * 24 * 60 * 60_000L,
            ["7d"] = 7 * 24 * 60 * 60_000L
        };

        private readonly HistoricalMarketDataRepository _repository;
        private readonly HistoricalMarketDataCache _historicalCache;
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly ContractDetailsCacheService _contractCache;
        private readonly IStrategyRuntimeTemplateProvider _templateProvider;
        private readonly RuntimeQueueOptions _queueOptions;
        private readonly HistoricalMarketDataOptions _historyOptions;
        private readonly BacktestProgressPushService _progressPushService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<BacktestRunner> _logger;

        // 统一回测日志前缀，便于排查性能问题
        private void LogSystemInfo(string message, params object[] args)
        {
            _logger.LogInformation($"回测系统Log：{message}", args);
        }

        private void LogSystemWarning(string message, params object[] args)
        {
            _logger.LogWarning($"回测系统Log：{message}", args);
        }

        private static string BuildLogicSummary(StrategyConfig config)
        {
            var logic = config?.Logic;
            if (logic == null)
            {
                return "logic=null";
            }

            var entry = BuildBranchSummary(logic.Entry?.Long);
            var exit = BuildBranchSummary(logic.Exit?.Long);
            return $"entry={entry} exit={exit}";
        }

        private static string BuildBranchSummary(StrategyLogicBranch? branch)
        {
            if (branch == null)
            {
                return "null";
            }

            var containers = branch.Containers ?? new List<ConditionContainer>();
            var containerCount = containers.Count;
            var groupCount = 0;
            var conditionCount = 0;
            foreach (var container in containers)
            {
                if (container?.Checks?.Groups == null)
                {
                    continue;
                }

                foreach (var group in container.Checks.Groups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    groupCount++;
                    if (group.Conditions != null)
                    {
                        conditionCount += group.Conditions.Count;
                    }
                }
            }

            var actionCount = branch.OnPass?.Conditions?.Count ?? 0;
            return $"containers={containerCount} groups={groupCount} conditions={conditionCount} actions={actionCount} enabled={branch.Enabled}";
        }

        private static long ToMs(long ticks)
        {
            if (ticks <= 0)
            {
                return 0;
            }

            return (long)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        private static bool ShouldPublishProgress(ref long nextTick, long intervalTicks)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < nextTick)
            {
                return false;
            }

            nextTick = now + intervalTicks;
            return true;
        }

        private enum LogicBranchKind
        {
            Entry,
            Exit
        }

        private sealed class LogicTiming
        {
            public long ExitTicks { get; set; }
            public long EntryTicks { get; set; }
            public long EntryFilterTicks { get; set; }
            public long ExitCheckTicks { get; set; }
            public long EntryCheckTicks { get; set; }
            public long ExitActionTicks { get; set; }
            public long EntryActionTicks { get; set; }

            public void AddBranch(LogicBranchKind kind, long ticks)
            {
                if (kind == LogicBranchKind.Exit)
                {
                    ExitTicks += ticks;
                }
                else
                {
                    EntryTicks += ticks;
                }
            }

            public void AddCheck(LogicBranchKind kind, long ticks)
            {
                if (kind == LogicBranchKind.Exit)
                {
                    ExitCheckTicks += ticks;
                }
                else
                {
                    EntryCheckTicks += ticks;
                }
            }

            public void AddAction(LogicBranchKind kind, long ticks)
            {
                if (kind == LogicBranchKind.Exit)
                {
                    ExitActionTicks += ticks;
                }
                else
                {
                    EntryActionTicks += ticks;
                }
            }
        }

        private sealed class EquityCurveAggregator
        {
            private readonly long _intervalMs;
            private readonly List<BacktestEquityPoint> _points;
            private BacktestEquityPoint? _previousPoint;
            private BacktestEquityPoint? _bucketLastPoint;
            private decimal _bucketStartRealized;
            private decimal _bucketStartUnrealized;
            private long _bucketStart;
            private bool _hasBucket;

            public EquityCurveAggregator(long intervalMs, int totalBarsHint)
            {
                _intervalMs = intervalMs <= 0 ? 60_000L : intervalMs;
                var divider = Math.Max(1L, _intervalMs / 60_000L);
                var estimatedCount = totalBarsHint > 0
                    ? (int)Math.Min(totalBarsHint, totalBarsHint / divider + 4)
                    : 16;
                _points = new List<BacktestEquityPoint>(Math.Max(16, estimatedCount));
            }

            public void Add(BacktestEquityPoint point)
            {
                var bucketStart = AlignBucketStart(point.Timestamp, _intervalMs);
                if (!_hasBucket)
                {
                    StartBucket(bucketStart, point);
                    _previousPoint = point;
                    return;
                }

                if (bucketStart != _bucketStart)
                {
                    FlushBucket();
                    StartBucket(bucketStart, point);
                    _previousPoint = point;
                    return;
                }

                _bucketLastPoint = point;
                _previousPoint = point;
            }

            public List<BacktestEquityPoint> Build()
            {
                FlushBucket();
                return _points;
            }

            private void StartBucket(long bucketStart, BacktestEquityPoint point)
            {
                _bucketStart = bucketStart;
                _bucketLastPoint = point;
                _hasBucket = true;

                if (_previousPoint == null)
                {
                    // 第一桶以 0 作为起点，便于直接观察回测起始后的区间变化。
                    _bucketStartRealized = 0m;
                    _bucketStartUnrealized = 0m;
                }
                else
                {
                    _bucketStartRealized = _previousPoint.RealizedPnl;
                    _bucketStartUnrealized = _previousPoint.UnrealizedPnl;
                }
            }

            private void FlushBucket()
            {
                if (!_hasBucket || _bucketLastPoint == null)
                {
                    return;
                }

                var last = _bucketLastPoint;
                _points.Add(new BacktestEquityPoint
                {
                    Timestamp = last.Timestamp,
                    Equity = last.Equity,
                    RealizedPnl = last.RealizedPnl,
                    UnrealizedPnl = last.UnrealizedPnl,
                    PeriodRealizedPnl = last.RealizedPnl - _bucketStartRealized,
                    PeriodUnrealizedPnl = last.UnrealizedPnl - _bucketStartUnrealized
                });

                _bucketLastPoint = null;
                _hasBucket = false;
            }
        }

        public BacktestRunner(
            HistoricalMarketDataRepository repository,
            HistoricalMarketDataCache historicalCache,
            IMarketDataProvider marketDataProvider,
            ContractDetailsCacheService contractCache,
            IStrategyRuntimeTemplateProvider templateProvider,
            IOptions<RuntimeQueueOptions> queueOptions,
            IOptions<HistoricalMarketDataOptions> historyOptions,
            BacktestProgressPushService progressPushService,
            ILoggerFactory loggerFactory,
            ILogger<BacktestRunner> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _historicalCache = historicalCache ?? throw new ArgumentNullException(nameof(historicalCache));
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _contractCache = contractCache ?? throw new ArgumentNullException(nameof(contractCache));
            _templateProvider = templateProvider;
            _queueOptions = queueOptions?.Value ?? new RuntimeQueueOptions();
            _historyOptions = historyOptions?.Value ?? new HistoricalMarketDataOptions();
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<BacktestRunResult> RunAsync(
            BacktestRunRequest request,
            StrategyConfig config,
            BacktestProgressContext? progressContext,
            CancellationToken ct)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var trade = config.Trade ?? throw new InvalidOperationException("策略配置缺少 Trade 信息");
            var exchange = MarketDataKeyNormalizer.NormalizeExchange(
                !string.IsNullOrWhiteSpace(request.Exchange) ? request.Exchange : trade.Exchange);
            if (string.IsNullOrWhiteSpace(exchange))
            {
                throw new InvalidOperationException("交易所不能为空");
            }

            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(
                !string.IsNullOrWhiteSpace(request.Timeframe)
                    ? request.Timeframe
                    : MarketDataKeyNormalizer.TimeframeFromSeconds(trade.TimeframeSec));
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                throw new InvalidOperationException("策略周期不能为空");
            }

            var timeframeMs = MarketDataConfig.TimeframeToMs(timeframe);
            var symbols = NormalizeSymbols(request.Symbols, trade.Symbol);
            if (symbols.Count == 0)
            {
                throw new InvalidOperationException("回测标的不能为空");
            }

            var (startTime, endTime) = ParseRange(request.StartTime, request.EndTime);
            var useRange = startTime.HasValue || endTime.HasValue;
            if (useRange && (!startTime.HasValue || !endTime.HasValue))
            {
                throw new InvalidOperationException("起止时间需同时提供，或改用 BarCount");
            }

            var barCount = ResolveBarCount(useRange, request.BarCount);

            var output = request.Output ?? new BacktestOutputOptions();
            var (equityCurveGranularity, equityCurveGranularityMs) = ResolveEquityCurveGranularity(output.EquityCurveGranularity);
            var runtimeConfig = request.Runtime ?? config.Runtime;
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "start",
                    "回测开始",
                    "请求参数解析完成，开始执行回测",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    false,
                    ct)
                .ConfigureAwait(false);

            LogSystemInfo(
                "回测开始: exchange={Exchange} timeframe={Timeframe} symbols={Symbols} range={Range} bars={Bars}",
                exchange,
                timeframe,
                string.Join(",", symbols),
                useRange ? $"{startTime:yyyy-MM-dd HH:mm:ss}~{endTime:yyyy-MM-dd HH:mm:ss}" : "BarCount",
                barCount);
            LogSystemInfo(
                "参数解析完成: useRange={UseRange} outputTrades={IncludeTrades} outputEquity={IncludeEquity} outputEvents={IncludeEvents} useRuntimeGate={UseRuntimeGate} equityGranularity={Granularity}",
                useRange,
                output.IncludeTrades,
                output.IncludeEquityCurve,
                output.IncludeEvents,
                request.UseStrategyRuntime,
                equityCurveGranularity);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "parse_request",
                    "参数解析",
                    "参数解析完成",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var buildRuntimeSw = System.Diagnostics.Stopwatch.StartNew();
            var symbolRuntimes = BuildSymbolRuntimes(
                request,
                config,
                exchange,
                timeframe,
                timeframeMs,
                runtimeConfig,
                symbols);
            buildRuntimeSw.Stop();
            if (symbolRuntimes.Count == 0)
            {
                throw new InvalidOperationException("未能构建回测策略实例");
            }
            var totalIndicatorRequests = symbolRuntimes.Values.Sum(r => r.IndicatorRequests.Count);
            LogSystemInfo(
                "策略实例构建完成: symbols={Symbols} indicatorRequests={IndicatorRequests} 耗时={Elapsed}ms",
                symbolRuntimes.Count,
                totalIndicatorRequests,
                buildRuntimeSw.ElapsedMilliseconds);
            foreach (var runtime in symbolRuntimes.Values)
            {
                LogSystemInfo(
                    "运行时间配置: symbol={Symbol} type={Type} policy={Policy} summary={Summary}",
                    runtime.Symbol,
                    runtime.RuntimeSchedule.ScheduleType,
                    runtime.RuntimeSchedule.Policy,
                    runtime.RuntimeSchedule.Summary);
                LogSystemInfo(
                    "策略结构: symbol={Symbol} {Summary}",
                    runtime.Symbol,
                    BuildLogicSummary(runtime.Strategy.StrategyConfig));
            }
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "build_runtime",
                    "构建策略实例",
                    "策略实例构建完成",
                    null,
                    null,
                    buildRuntimeSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var sampleRequests = symbolRuntimes.Values.First().IndicatorRequests;
            var warmupByTimeframe = BuildWarmupMap(sampleRequests);
            var requiredTimeframes = BuildRequiredTimeframes(sampleRequests, timeframe);

            var series = new Dictionary<string, List<OHLCV>>();
            var drivingTimestampsBySymbol = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

            foreach (var runtime in symbolRuntimes.Values)
            {
                var warmupBars = warmupByTimeframe.TryGetValue(timeframe, out var warmup) ? warmup : 0;
                var loadSw = System.Diagnostics.Stopwatch.StartNew();
                var bars = await LoadPrimaryBarsAsync(
                    exchange,
                    runtime.Symbol,
                    timeframe,
                    startTime,
                    endTime,
                    barCount,
                    warmupBars,
                    ct);
                loadSw.Stop();
                LogSystemInfo(
                    "加载主周期K线: symbol={Symbol} timeframe={Timeframe} bars={Bars} warmup={Warmup} 耗时={Elapsed}ms",
                    runtime.Symbol,
                    timeframe,
                    bars.Count,
                    warmupBars,
                    loadSw.ElapsedMilliseconds);

                if (bars.Count == 0)
                {
                    LogSystemWarning("回测标的无主周期K线: {Symbol}", runtime.Symbol);
                }

                series[BacktestMarketDataProvider.BuildKey(exchange, runtime.Symbol, timeframe)] = bars;
                var drivingBars = SelectDrivingBars(bars, startTime, endTime, barCount);
                drivingTimestampsBySymbol[runtime.Symbol] = drivingBars
                    .Select(b => (long)(b.timestamp ?? 0))
                    .Where(ts => ts > 0)
                    .ToList();
            }
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "load_primary",
                    "加载主周期K线",
                    "主周期K线加载完成",
                    symbolRuntimes.Count,
                    symbolRuntimes.Count,
                    stopwatch.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var intersectionSw = System.Diagnostics.Stopwatch.StartNew();
            var drivingTimestamps = BuildIntersection(drivingTimestampsBySymbol.Values);
            intersectionSw.Stop();
            LogSystemInfo(
                "交集时间轴构建完成: bars={Bars} symbols={Symbols} 耗时={Elapsed}ms",
                drivingTimestamps.Count,
                symbols.Count,
                intersectionSw.ElapsedMilliseconds);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "build_intersection",
                    "构建交集时间轴",
                    $"交集时间轴构建完成，bars={drivingTimestamps.Count}",
                    drivingTimestamps.Count,
                    drivingTimestamps.Count,
                    intersectionSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);
            if (drivingTimestamps.Count == 0)
            {
                LogSystemWarning("回测时间轴为空，直接返回空结果");
                stopwatch.Stop();
                await _progressPushService
                    .PublishStageAsync(
                        progressContext,
                        "completed",
                        "回测完成",
                        "交集时间轴为空，返回空结果",
                        0,
                        0,
                        stopwatch.ElapsedMilliseconds,
                        true,
                        ct)
                    .ConfigureAwait(false);
                return new BacktestRunResult
                {
                    Exchange = exchange,
                    Timeframe = timeframe,
                    EquityCurveGranularity = equityCurveGranularity,
                    StartTimestamp = 0,
                    EndTimestamp = 0,
                    TotalBars = 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    TotalStats = new BacktestStats(),
                    Symbols = symbolRuntimes.Values.Select(r => r.Result).ToList()
                };
            }

            var drivingStart = drivingTimestamps[0];
            var drivingEnd = drivingTimestamps[^1];
            var equityCurveCollectors = output.IncludeEquityCurve
                ? symbolRuntimes.Values.ToDictionary(
                    r => r.Symbol,
                    _ => new EquityCurveAggregator(equityCurveGranularityMs, drivingTimestamps.Count),
                    StringComparer.OrdinalIgnoreCase)
                : null;

            var supplementSw = System.Diagnostics.Stopwatch.StartNew();
            await LoadSupplementaryTimeframesAsync(
                exchange,
                requiredTimeframes,
                symbols,
                drivingStart,
                drivingEnd,
                warmupByTimeframe,
                series,
                ct);
            supplementSw.Stop();
            LogSystemInfo(
                "补充周期加载完成: timeframes={Timeframes} series={SeriesCount} 耗时={Elapsed}ms",
                requiredTimeframes.Count == 0 ? "-" : string.Join(",", requiredTimeframes),
                series.Count,
                supplementSw.ElapsedMilliseconds);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "load_supplementary",
                    "加载补充周期",
                    "补充周期加载完成",
                    null,
                    null,
                    supplementSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var provider = new BacktestMarketDataProvider(series);
            var indicatorEngine = new IndicatorEngine(
                provider,
                _loggerFactory.CreateLogger<IndicatorEngine>(),
                new OptionsWrapper<RuntimeQueueOptions>(_queueOptions));
            var valueResolver = new IndicatorValueResolver(
                provider,
                indicatorEngine,
                _loggerFactory.CreateLogger<IndicatorValueResolver>());
            var conditionCache = new ConditionCacheService();
            var conditionEvaluator = new ConditionEvaluator(conditionCache);

            var actionExecutor = new BacktestActionExecutor(
                symbolRuntimes.ToDictionary(k => k.Key, v => v.Value.State, StringComparer.OrdinalIgnoreCase));

            var loopSw = System.Diagnostics.Stopwatch.StartNew();
            var logicTiming = new LogicTiming();
            var loopTotalTicks = 0L;
            var resolveTicks = 0L;
            var updateTicks = 0L;
            var riskTicks = 0L;
            var contextTicks = 0L;
            var runtimeTicks = 0L;
            var logicTicks = 0L;
            var equityTicks = 0L;
            var loopBars = 0L;
            var loopSymbols = 0L;
            var nextLoopProgressTick = System.Diagnostics.Stopwatch.GetTimestamp() + MainLoopProgressIntervalTicks;
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "main_loop",
                    "执行主循环",
                    "主循环开始",
                    0,
                    drivingTimestamps.Count,
                    0,
                    false,
                    ct)
                .ConfigureAwait(false);
            foreach (var timestamp in drivingTimestamps)
            {
                var loopStart = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!TryResolveBars(
                        provider,
                        exchange,
                        timeframe,
                        symbols,
                        timestamp,
                        out var mainBars,
                        out var priceBars))
                {
                    continue;
                }
                resolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - loopStart;

                foreach (var symbol in symbols)
                {
                    var symbolStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var runtime = symbolRuntimes[symbol];
                    var bar = mainBars[symbol];
                    var priceBar = priceBars[symbol];

                    actionExecutor.UpdateCurrentBar(symbol, priceBar);
                    updateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - symbolStart;

                    if (runtime.State.Position != null)
                    {
                        var riskStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        actionExecutor.TryProcessRisk(runtime.State, bar, timestamp);
                        riskTicks += System.Diagnostics.Stopwatch.GetTimestamp() - riskStart;
                    }

                    if (runtime.IndicatorRequests.Count > 0)
                    {
                        var indicatorTask = new IndicatorTask(
                            new MarketDataTask(exchange, symbol, timeframe, timestamp, true),
                            runtime.IndicatorRequests);
                        indicatorEngine.ProcessTaskNow(indicatorTask);
                    }

                    var contextStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var context = new StrategyExecutionContext(
                        runtime.Strategy,
                        new MarketDataTask(exchange, symbol, timeframe, timestamp, true),
                        valueResolver,
                        actionExecutor);
                    contextTicks += System.Diagnostics.Stopwatch.GetTimestamp() - contextStart;

                    var runtimeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var runtimeGate = ResolveRuntimeGate(runtime, context.CurrentTime, output.IncludeEvents);
                    runtimeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - runtimeStart;
                    var logicStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    ExecuteLogic(context, runtimeGate, conditionEvaluator, logicTiming);
                    logicTicks += System.Diagnostics.Stopwatch.GetTimestamp() - logicStart;

                    if (output.IncludeEquityCurve)
                    {
                        var equityStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        var equity = BuildEquityPoint(runtime, priceBar, request.InitialCapital, timestamp);
                        equityCurveCollectors![runtime.Symbol].Add(equity);
                        equityTicks += System.Diagnostics.Stopwatch.GetTimestamp() - equityStart;
                    }

                    loopSymbols++;
                }

                loopTotalTicks += System.Diagnostics.Stopwatch.GetTimestamp() - loopStart;
                loopBars++;
                if (ShouldPublishProgress(ref nextLoopProgressTick, MainLoopProgressIntervalTicks))
                {
                    await _progressPushService
                        .PublishStageAsync(
                            progressContext,
                            "main_loop",
                            "执行主循环",
                            "主循环执行中",
                            (int)loopBars,
                            drivingTimestamps.Count,
                            loopSw.ElapsedMilliseconds,
                            false,
                            ct)
                        .ConfigureAwait(false);
                }
            }
            loopSw.Stop();
            LogSystemInfo(
                "主循环拆分: bars={Bars} symbols={Symbols} totalMs={Total} resolveMs={Resolve} updateMs={Update} riskMs={Risk} contextMs={Context} runtimeMs={Runtime} logicMs={Logic} equityMs={Equity}",
                loopBars,
                loopSymbols,
                ToMs(loopTotalTicks),
                ToMs(resolveTicks),
                ToMs(updateTicks),
                ToMs(riskTicks),
                ToMs(contextTicks),
                ToMs(runtimeTicks),
                ToMs(logicTicks),
                ToMs(equityTicks));
            LogSystemInfo(
                "逻辑拆分: entryMs={Entry} exitMs={Exit} entryFilterMs={EntryFilter} entryCheckMs={EntryCheck} entryActionMs={EntryAction} exitCheckMs={ExitCheck} exitActionMs={ExitAction}",
                ToMs(logicTiming.EntryTicks),
                ToMs(logicTiming.ExitTicks),
                ToMs(logicTiming.EntryFilterTicks),
                ToMs(logicTiming.EntryCheckTicks),
                ToMs(logicTiming.EntryActionTicks),
                ToMs(logicTiming.ExitCheckTicks),
                ToMs(logicTiming.ExitActionTicks));
            LogSystemInfo(
                "主循环完成: bars={Bars} symbols={Symbols} 耗时={Elapsed}ms",
                drivingTimestamps.Count,
                symbols.Count,
                loopSw.ElapsedMilliseconds);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "main_loop",
                    "执行主循环",
                    "主循环完成",
                    drivingTimestamps.Count,
                    drivingTimestamps.Count,
                    loopSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);
            // 回测结束处理：按最后一根K线收盘价强制平仓
            var finalizeSw = System.Diagnostics.Stopwatch.StartNew();
            var lastTimestamp = drivingTimestamps[^1];
            foreach (var runtime in symbolRuntimes.Values)
            {
                if (runtime.State.Position == null)
                {
                    continue;
                }

                if (!TryResolveBar(provider, exchange, "1m", runtime.Symbol, lastTimestamp, out var lastPriceBar) &&
                    !TryResolveBar(provider, exchange, timeframe, runtime.Symbol, lastTimestamp, out lastPriceBar))
                {
                    continue;
                }

                var closePrice = Convert.ToDecimal(lastPriceBar.close ?? lastPriceBar.open ?? 0d);
                if (closePrice <= 0)
                {
                    continue;
                }

                var orderSide = runtime.State.Position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? "sell"
                    : "buy";
                var execPrice = ApplySlippage(closePrice, orderSide, runtime.State.SlippageBps);
                actionExecutor.ClosePosition(runtime.Symbol, execPrice, lastTimestamp, "End");

                if (output.IncludeEquityCurve)
                {
                    var equity = BuildEquityPoint(runtime, lastPriceBar, request.InitialCapital, lastTimestamp);
                    equityCurveCollectors![runtime.Symbol].Add(equity);
                }
            }

            if (output.IncludeEquityCurve && equityCurveCollectors != null)
            {
                foreach (var runtime in symbolRuntimes.Values)
                {
                    runtime.Result.EquityCurve = equityCurveCollectors[runtime.Symbol].Build();
                }
            }

            var totalPositions = symbolRuntimes.Values.Sum(r => r.State.Trades.Count);
            var foundPositions = 0;
            var collectPositionsSw = System.Diagnostics.Stopwatch.StartNew();
            var nextPositionProgressTick = System.Diagnostics.Stopwatch.GetTimestamp() + PositionProgressIntervalTicks;
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "collect_positions",
                    "汇总仓位",
                    "开始汇总仓位数据",
                    0,
                    totalPositions,
                    0,
                    false,
                    ct)
                .ConfigureAwait(false);

            foreach (var runtime in symbolRuntimes.Values)
            {
                runtime.Result.Bars = drivingTimestamps.Count;
                runtime.Result.InitialCapital = request.InitialCapital;

                var symbolTrades = runtime.State.Trades;
                if (output.IncludeTrades)
                {
                    runtime.Result.Trades = new List<BacktestTrade>(symbolTrades.Count);
                }

                var tradeChunk = new List<BacktestTrade>(128);
                for (var tradeIndex = 0; tradeIndex < symbolTrades.Count; tradeIndex++)
                {
                    var closedTrade = symbolTrades[tradeIndex];
                    foundPositions++;

                    if (output.IncludeTrades)
                    {
                        runtime.Result.Trades.Add(closedTrade);
                        tradeChunk.Add(closedTrade);
                    }

                    // 仓位汇总阶段按 0.1 秒推送进度，前端可先展示部分数据。
                    if (ShouldPublishProgress(ref nextPositionProgressTick, PositionProgressIntervalTicks))
                    {
                        await _progressPushService
                            .PublishPositionsAsync(
                                progressContext,
                                "collect_positions",
                                "汇总仓位",
                                foundPositions,
                                totalPositions,
                                runtime.Symbol,
                                tradeChunk.Count > 0 ? tradeChunk : null,
                                false,
                                collectPositionsSw.ElapsedMilliseconds,
                                ct)
                            .ConfigureAwait(false);
                        tradeChunk.Clear();
                    }
                }

                if (tradeChunk.Count > 0)
                {
                    await _progressPushService
                        .PublishPositionsAsync(
                            progressContext,
                            "collect_positions",
                            "汇总仓位",
                            foundPositions,
                            totalPositions,
                            runtime.Symbol,
                            tradeChunk,
                            false,
                            collectPositionsSw.ElapsedMilliseconds,
                            ct)
                        .ConfigureAwait(false);
                }

                if (output.IncludeEvents)
                {
                    runtime.Result.Events = runtime.State.Events.ToList();
                }

                runtime.Result.Stats = BuildStats(
                    symbolTrades,
                    output.IncludeEquityCurve ? runtime.Result.EquityCurve : null,
                    request.InitialCapital);
            }

            await _progressPushService
                .PublishPositionsAsync(
                    progressContext,
                    "collect_positions",
                    "汇总仓位",
                    foundPositions,
                    totalPositions,
                    null,
                    null,
                    true,
                    collectPositionsSw.ElapsedMilliseconds,
                    ct)
                .ConfigureAwait(false);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "collect_positions",
                    "汇总仓位",
                    "仓位汇总完成",
                    foundPositions,
                    totalPositions,
                    collectPositionsSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var totalInitial = request.InitialCapital * symbolRuntimes.Count;
            var allTrades = symbolRuntimes.Values.SelectMany(r => r.State.Trades).ToList();
            var totalEquity = output.IncludeEquityCurve
                ? BuildTotalEquityCurve(symbolRuntimes.Values.Select(r => r.Result.EquityCurve).ToList())
                : new List<BacktestEquityPoint>();

            stopwatch.Stop();
            finalizeSw.Stop();
            var result = new BacktestRunResult
            {
                Exchange = exchange,
                Timeframe = timeframe,
                EquityCurveGranularity = equityCurveGranularity,
                StartTimestamp = drivingStart,
                EndTimestamp = drivingEnd,
                TotalBars = drivingTimestamps.Count,
                DurationMs = stopwatch.ElapsedMilliseconds,
                TotalStats = BuildStats(allTrades, totalEquity, totalInitial),
                Symbols = symbolRuntimes.Values.Select(r => r.Result).ToList()
            };

            LogSystemInfo(
                "收尾统计完成: trades={Trades} 耗时={Elapsed}ms",
                allTrades.Count,
                finalizeSw.ElapsedMilliseconds);
            LogSystemInfo(
                "回测完成: exchange={Exchange} timeframe={Timeframe} bars={Bars} trades={Trades}",
                exchange,
                timeframe,
                result.TotalBars,
                allTrades.Count);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "completed",
                    "回测完成",
                    "回测执行完成，已返回完整结果",
                    result.TotalBars,
                    result.TotalBars,
                    result.DurationMs,
                    true,
                    ct)
                .ConfigureAwait(false);

            return result;
        }
        private sealed class SymbolRuntime
        {
            public SymbolRuntime(
                string symbol,
                StrategyModel strategy,
                StrategyRuntimeSchedule runtimeSchedule,
                BacktestActionExecutor.SymbolState state,
                List<IndicatorRequest> indicatorRequests,
                BacktestSymbolResult result,
                bool useRuntimeGate)
            {
                Symbol = symbol;
                Strategy = strategy;
                RuntimeSchedule = runtimeSchedule;
                State = state;
                IndicatorRequests = indicatorRequests;
                Result = result;
                UseRuntimeGate = useRuntimeGate;
            }

            public string Symbol { get; }
            public StrategyModel Strategy { get; }
            public StrategyRuntimeSchedule RuntimeSchedule { get; }
            public BacktestActionExecutor.SymbolState State { get; }
            public List<IndicatorRequest> IndicatorRequests { get; }
            public BacktestSymbolResult Result { get; }
            public bool UseRuntimeGate { get; }
        }

        private Dictionary<string, SymbolRuntime> BuildSymbolRuntimes(
            BacktestRunRequest request,
            StrategyConfig templateConfig,
            string exchange,
            string timeframe,
            long timeframeMs,
            StrategyRuntimeConfig? runtimeConfig,
            IReadOnlyList<string> symbols)
        {
            var runtimes = new Dictionary<string, SymbolRuntime>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols)
            {
                var strategy = BuildStrategyForSymbol(request, templateConfig, exchange, timeframe, timeframeMs, symbol);
                var indicatorRequests = BuildIndicatorRequests(strategy);
                var schedule = new StrategyRuntimeSchedule(runtimeConfig, _templateProvider);
                if (!string.IsNullOrWhiteSpace(schedule.Error))
                {
                    LogSystemWarning(
                        "策略运行时间配置异常: symbol={Symbol} error={Error}",
                        symbol,
                        schedule.Error);
                }

                var contract = TryResolveContract(exchange, symbol);
                var trade = strategy.StrategyConfig.Trade ?? new TradeConfig();
                var orderQty = request.OrderQtyOverride ?? trade.Sizing.OrderQty;
                var leverage = request.LeverageOverride ?? trade.Sizing.Leverage;
                var stopLoss = request.StopLossPctOverride ?? trade.Risk.StopLossPct;
                var takeProfit = request.TakeProfitPctOverride ?? trade.Risk.TakeProfitPct;

                var state = new BacktestActionExecutor.SymbolState
                {
                    Symbol = symbol,
                    OrderQty = orderQty,
                    Leverage = Math.Max(1, leverage),
                    StopLossPct = stopLoss > 0 ? stopLoss : null,
                    TakeProfitPct = takeProfit > 0 ? takeProfit : null,
                    FeeRate = request.FeeRate,
                    SlippageBps = request.SlippageBps,
                    AutoReverse = request.AutoReverse,
                    ContractSize = contract?.ContractSize ?? 1m,
                    Contract = contract
                };

                var result = new BacktestSymbolResult
                {
                    Symbol = symbol
                };

                runtimes[symbol] = new SymbolRuntime(
                    symbol,
                    strategy,
                    schedule,
                    state,
                    indicatorRequests,
                    result,
                    request.UseStrategyRuntime);
            }

            return runtimes;
        }

        private static StrategyModel BuildStrategyForSymbol(
            BacktestRunRequest request,
            StrategyConfig templateConfig,
            string exchange,
            string timeframe,
            long timeframeMs,
            string symbol)
        {
            var cloned = CloneConfig(templateConfig);
            var trade = cloned.Trade ?? new TradeConfig();

            trade.Exchange = exchange;
            trade.Symbol = symbol;
            trade.TimeframeSec = (int)(timeframeMs / 1000);

            if (request.OrderQtyOverride.HasValue)
            {
                trade.Sizing.OrderQty = request.OrderQtyOverride.Value;
            }

            if (request.LeverageOverride.HasValue)
            {
                trade.Sizing.Leverage = request.LeverageOverride.Value;
            }

            if (request.TakeProfitPctOverride.HasValue)
            {
                trade.Risk.TakeProfitPct = request.TakeProfitPctOverride.Value;
            }

            if (request.StopLossPctOverride.HasValue)
            {
                trade.Risk.StopLossPct = request.StopLossPctOverride.Value;
            }

            cloned.Trade = trade;
            if (request.Runtime != null)
            {
                cloned.Runtime = request.Runtime;
            }

            return new StrategyModel
            {
                Id = request.UsId ?? 0,
                UidCode = $"backtest:{symbol}:{timeframe}",
                Name = $"Backtest-{symbol}",
                Description = "Backtest",
                State = StrategyState.Testing,
                CreatorUserId = 0,
                ExchangeApiKeyId = null,
                Version = 1,
                StrategyConfig = cloned
            };
        }

        private static StrategyConfig CloneConfig(StrategyConfig config)
        {
            var json = JsonConvert.SerializeObject(config);
            return JsonConvert.DeserializeObject<StrategyConfig>(json) ?? new StrategyConfig();
        }

        private ContractDetails? TryResolveContract(string exchange, string symbol)
        {
            if (!TryParseExchange(exchange, out var exchangeEnum))
            {
                return null;
            }

            var normalized = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var candidates = new List<string> { normalized };

            if (!normalized.Contains(':', StringComparison.Ordinal))
            {
                candidates.Add($"{normalized}:USDT");
            }

            candidates.Add(normalized.Replace("/", string.Empty, StringComparison.Ordinal));

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var contract = _contractCache.GetContract(exchangeEnum, candidate);
                if (contract != null)
                {
                    return contract;
                }
            }

            return null;
        }

        private static bool TryParseExchange(string exchange, out MarketDataConfig.ExchangeEnum exchangeEnum)
        {
            exchangeEnum = default;
            var normalized = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            foreach (var item in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                if (MarketDataConfig.ExchangeToString(item).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    exchangeEnum = item;
                    return true;
                }
            }

            return false;
        }

        private static List<string> NormalizeSymbols(IReadOnlyList<string>? symbols, string fallback)
        {
            var list = new List<string>();
            if (symbols != null && symbols.Count > 0)
            {
                list.AddRange(symbols);
            }
            else if (!string.IsNullOrWhiteSpace(fallback))
            {
                list.Add(fallback);
            }

            return list
                .Select(MarketDataKeyNormalizer.NormalizeSymbol)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static (DateTimeOffset? Start, DateTimeOffset? End) ParseRange(string? startRaw, string? endRaw)
        {
            var start = ParseDateTime(startRaw, "开始时间");
            var end = ParseDateTime(endRaw, "结束时间");

            if (start.HasValue && end.HasValue && start.Value > end.Value)
            {
                throw new InvalidOperationException("开始时间不能晚于结束时间");
            }

            return (start, end);
        }

        private static DateTimeOffset? ParseDateTime(string? raw, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (!DateTime.TryParse(raw, out var parsed))
            {
                throw new InvalidOperationException($"{fieldName}格式错误，请使用 yyyy-MM-dd HH:mm:ss");
            }

            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            return new DateTimeOffset(parsed);
        }

        private int ResolveBarCount(bool useRange, int? barCount)
        {
            if (useRange)
            {
                return Math.Max(1, barCount ?? 0);
            }

            var max = _historyOptions.MaxQueryBars > 0 ? _historyOptions.MaxQueryBars : DefaultBarCount;
            var fallback = Math.Min(DefaultBarCount, max);
            var count = barCount.HasValue && barCount.Value > 0 ? barCount.Value : fallback;
            return Math.Max(1, count);
        }
        private async Task<List<OHLCV>> LoadPrimaryBarsAsync(
            string exchange,
            string symbol,
            string timeframe,
            DateTimeOffset? startTime,
            DateTimeOffset? endTime,
            int barCount,
            int warmupBars,
            CancellationToken ct)
        {
            if (startTime.HasValue && endTime.HasValue)
            {
                var startMs = startTime.Value.ToUnixTimeMilliseconds();
                var endMs = endTime.Value.ToUnixTimeMilliseconds();
                var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                var warmupStart = Math.Max(0, startMs - warmupBars * tfMs);
                return await LoadBarsByRangeAsync(exchange, symbol, timeframe, warmupStart, endMs, ct);
            }

            var count = Math.Max(1, barCount + Math.Max(0, warmupBars));
            return await LoadBarsByCountAsync(exchange, symbol, timeframe, null, count, ct);
        }

        private async Task LoadSupplementaryTimeframesAsync(
            string exchange,
            IReadOnlyCollection<string> timeframes,
            IReadOnlyList<string> symbols,
            long drivingStart,
            long drivingEnd,
            IReadOnlyDictionary<string, int> warmupByTimeframe,
            Dictionary<string, List<OHLCV>> series,
            CancellationToken ct)
        {
            foreach (var timeframe in timeframes)
            {
                foreach (var symbol in symbols)
                {
                    var key = BacktestMarketDataProvider.BuildKey(exchange, symbol, timeframe);
                    if (series.ContainsKey(key))
                    {
                        continue;
                    }

                    var tfMs = MarketDataConfig.TimeframeToMs(timeframe);
                    var warmupBars = warmupByTimeframe.TryGetValue(timeframe, out var warmup) ? warmup : 0;
                    var startMs = Math.Max(0, drivingStart - warmupBars * tfMs);
                    var endMs = drivingEnd;

                    var bars = await LoadBarsByRangeAsync(exchange, symbol, timeframe, startMs, endMs, ct);
                    series[key] = bars;
                }
            }
        }

        private async Task<List<OHLCV>> LoadBarsByRangeAsync(
            string exchange,
            string symbol,
            string timeframe,
            long startMs,
            long endMs,
            CancellationToken ct)
        {
            // 统一规范化 key，确保缓存键与表名一致。
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);

            if (_historicalCache.TryGetHistoryFromCache(
                    exchangeKey,
                    timeframeKey,
                    symbolKey,
                    startMs,
                    endMs,
                    null,
                    out var cachedBars,
                    out var cacheMissReason))
            {
                LogSystemInfo(
                    "历史行情缓存命中(区间): exchange={Exchange} symbol={Symbol} timeframe={Timeframe} startMs={Start} endMs={End} bars={Bars}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    startMs,
                    endMs,
                    cachedBars.Count);
                return cachedBars;
            }

            LogSystemInfo(
                "历史行情缓存未命中(区间)，回退历史行情表: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} startMs={Start} endMs={End} reason={Reason}",
                exchangeKey,
                symbolKey,
                timeframeKey,
                startMs,
                endMs,
                cacheMissReason);

            var tableName = BuildTableName(exchangeKey, symbolKey, timeframeKey);
            var pageSize = ResolvePageSize();
            var result = new List<OHLCV>();
            var cursor = startMs;
            var pageCount = 0;
            var rowCount = 0;
            var loadSw = System.Diagnostics.Stopwatch.StartNew();

            while (cursor <= endMs)
            {
                var rows = await _repository.QueryRangeAsync(tableName, cursor, endMs, pageSize, ct)
                    .ConfigureAwait(false);
                if (rows.Count == 0)
                {
                    break;
                }

                result.AddRange(rows.Select(ToOhlcv));
                pageCount++;
                rowCount += rows.Count;

                var lastTs = rows[^1].OpenTime;
                if (lastTs >= endMs || rows.Count < pageSize)
                {
                    break;
                }

                cursor = lastTs + 1;
            }

            loadSw.Stop();
            LogSystemInfo(
                "区间查询完成: table={Table} startMs={Start} endMs={End} rows={Rows} pages={Pages} 耗时={Elapsed}ms",
                tableName,
                startMs,
                endMs,
                rowCount,
                pageCount,
                loadSw.ElapsedMilliseconds);
            return result;
        }

        private async Task<List<OHLCV>> LoadBarsByCountAsync(
            string exchange,
            string symbol,
            string timeframe,
            long? endMs,
            int count,
            CancellationToken ct)
        {
            // 统一规范化 key，避免大小写/格式差异
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var safeCount = Math.Max(1, count);
            var timeframeMs = MarketDataConfig.TimeframeToMs(timeframeKey);
            var requestEndMs = endMs;
            var requestStartMs = requestEndMs.HasValue
                ? requestEndMs.Value - Math.Max(0L, (long)(safeCount - 1) * timeframeMs)
                : (long?)null;

            // 1. 优先从历史行情缓存中截取，命中后不读表。
            if (_historicalCache.TryGetHistoryFromCache(
                    exchangeKey,
                    timeframeKey,
                    symbolKey,
                    null,
                    endMs,
                    safeCount,
                    out var cachedBars,
                    out var cacheMissReason))
            {
                if (cachedBars.Count >= safeCount)
                {
                    LogSystemInfo(
                        "历史行情缓存命中(数量): exchange={Exchange} symbol={Symbol} timeframe={Timeframe} endMs={End} requested={Requested} actual={Actual}",
                        exchangeKey,
                        symbolKey,
                        timeframeKey,
                        endMs ?? 0,
                        safeCount,
                        cachedBars.Count);
                    return cachedBars;
                }

                LogSystemInfo(
                    "历史行情缓存数据不足(数量)，回退历史行情系统/历史行情表: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} endMs={End} requested={Requested} actual={Actual}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    endMs ?? 0,
                    safeCount,
                    cachedBars.Count);
            }
            else
            {
                LogSystemInfo(
                    "历史行情缓存未命中(数量): exchange={Exchange} symbol={Symbol} timeframe={Timeframe} endMs={End} requested={Requested} reason={Reason}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    endMs ?? 0,
                    safeCount,
                    cacheMissReason);
            }

            // 2. 历史行情缓存未命中时，尝试“历史行情系统”（IMarketDataProvider，例如 MarketDataEngine 缓存）
            //    - 仅当返回数量满足请求时才视为命中，避免因为缓存长度不足导致回测截断。
            List<OHLCV>? providerBars = null;
            try
            {
                providerBars = _marketDataProvider.GetHistoryKlines(
                    exchangeKey,
                    timeframeKey,
                    symbolKey,
                    endMs,
                    safeCount);
            }
            catch (Exception ex)
            {
                // 历史行情系统异常时，记录警告日志，继续回退到读表逻辑。
                LogSystemWarning(
                    "历史行情系统读取异常，回退到历史行情表: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} error={Error}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    ex.Message);
            }

            if (providerBars != null && providerBars.Count >= safeCount)
            {
                LogSystemInfo(
                    "历史行情系统命中: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} bars={Bars} source={Source} range={Range}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    providerBars.Count,
                    "MarketDataEngine",
                    BuildRangeText(ResolveTimestampMs(providerBars[0]), ResolveTimestampMs(providerBars[^1])));
                // IMarketDataProvider 约定按时间升序返回，无需反转。
                return providerBars;
            }

            if (providerBars != null && providerBars.Count > 0 && providerBars.Count < safeCount)
            {
                // 有部分数据但不足以支撑回测完整区间，记录详细缺口后回退到历史行情表。
                var providerStartMs = ResolveTimestampMs(providerBars[0]);
                var providerEndMs = ResolveTimestampMs(providerBars[^1]);
                var expectedEndMs = requestEndMs ?? providerEndMs;
                var expectedStartMs = expectedEndMs - Math.Max(0L, (long)(safeCount - 1) * timeframeMs);
                var missingCount = safeCount - providerBars.Count;

                var leftGapBars = providerStartMs > expectedStartMs
                    ? EstimateGapBars(providerStartMs - expectedStartMs, timeframeMs)
                    : 0;
                var rightGapBars = providerEndMs < expectedEndMs
                    ? EstimateGapBars(expectedEndMs - providerEndMs, timeframeMs)
                    : 0;
                var internalGapBars = Math.Max(0, missingCount - leftGapBars - rightGapBars);

                var missingParts = new List<string>(3);
                if (leftGapBars > 0)
                {
                    missingParts.Add($"左侧缺失{leftGapBars}根");
                }

                if (rightGapBars > 0)
                {
                    missingParts.Add($"右侧缺失{rightGapBars}根");
                }

                if (internalGapBars > 0)
                {
                    missingParts.Add($"区间内部缺失{internalGapBars}根");
                }

                var missingDetail = missingParts.Count == 0
                    ? "无法从首尾推断缺口位置"
                    : string.Join("，", missingParts);
                var reasonHint = BuildProviderInsufficientReason(leftGapBars, rightGapBars, internalGapBars, requestEndMs.HasValue);

                LogSystemInfo(
                    "历史行情系统数据不足，回退到历史行情表: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} requested={Requested} actual={Actual} missing={Missing} missingDetail={MissingDetail} requestRange={RequestRange} providerRange={ProviderRange} reasonHint={ReasonHint}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    safeCount,
                    providerBars.Count,
                    missingCount,
                    missingDetail,
                    BuildRangeText(expectedStartMs, expectedEndMs),
                    BuildRangeText(providerStartMs, providerEndMs),
                    reasonHint);
            }
            else if (providerBars != null && providerBars.Count == 0)
            {
                LogSystemInfo(
                    "历史行情系统返回空结果，回退到历史行情表: exchange={Exchange} symbol={Symbol} timeframe={Timeframe} requested={Requested} requestRange={RequestRange} reasonHint={ReasonHint}",
                    exchangeKey,
                    symbolKey,
                    timeframeKey,
                    safeCount,
                    BuildRangeText(requestStartMs, requestEndMs),
                    requestEndMs.HasValue
                        ? "该时间窗在历史行情系统中不可用或尚未预热"
                        : "按数量拉取末尾数据时历史行情系统无可用数据");
            }

            // 3. 历史行情系统没有命中或数据不足时，再从历史行情表读取。
            var tableName = BuildTableName(exchangeKey, symbolKey, timeframeKey);
            var loadSw = System.Diagnostics.Stopwatch.StartNew();
            var rows = await _repository.QueryRangeAsync(tableName, null, endMs, count, ct)
                .ConfigureAwait(false);
            var list = rows.Select(ToOhlcv).ToList();
            list.Reverse();
            loadSw.Stop();
            LogSystemInfo(
                "数量查询完成: table={Table} endMs={End} rows={Rows} 耗时={Elapsed}ms",
                tableName,
                endMs ?? 0,
                rows.Count,
                loadSw.ElapsedMilliseconds);

            if ((providerBars == null || providerBars.Count == 0) && list.Count == 0)
            {
                // 历史行情系统与历史行情表均无数据，按需求返回错误而非静默空结果。
                throw new InvalidOperationException(
                    $"历史行情不存在: exchange={exchangeKey} symbol={symbolKey} timeframe={timeframeKey}");
            }

            return list;
        }

        private int ResolvePageSize()
        {
            var max = _historyOptions.MaxQueryBars > 0 ? _historyOptions.MaxQueryBars : 2000;
            return Math.Max(500, max);
        }

        private static long ResolveTimestampMs(OHLCV bar)
        {
            return (long)(bar.timestamp ?? 0);
        }

        private static int EstimateGapBars(long gapMs, long timeframeMs)
        {
            if (gapMs <= 0 || timeframeMs <= 0)
            {
                return 0;
            }

            return (int)((gapMs + timeframeMs - 1) / timeframeMs);
        }

        private static string BuildProviderInsufficientReason(
            int leftGapBars,
            int rightGapBars,
            int internalGapBars,
            bool hasExplicitEnd)
        {
            if (internalGapBars > 0)
            {
                return "时间窗内存在断档，历史行情系统返回序列不连续或被过滤";
            }

            if (leftGapBars > 0 && rightGapBars > 0)
            {
                return "首尾两侧均未覆盖，历史行情系统可用时间窗小于请求时间窗";
            }

            if (rightGapBars > 0)
            {
                return "右侧缺失，历史行情系统最新时间落后于请求结束时间";
            }

            if (leftGapBars > 0)
            {
                return hasExplicitEnd
                    ? "左侧缺失，历史行情系统回溯深度不足"
                    : "按数量拉取时历史行情系统历史窗口深度不足";
            }

            return "历史行情系统返回条数不足";
        }

        private static string BuildRangeText(long? startMs, long? endMs)
        {
            return $"{FormatUnixMs(startMs)}({startMs?.ToString() ?? "null"}) ~ {FormatUnixMs(endMs)}({endMs?.ToString() ?? "null"})";
        }

        private static string FormatUnixMs(long? value)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                return "null";
            }

            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return value.Value.ToString();
            }
        }

        private static List<OHLCV> SelectDrivingBars(
            List<OHLCV> bars,
            DateTimeOffset? startTime,
            DateTimeOffset? endTime,
            int barCount)
        {
            if (bars.Count == 0)
            {
                return new List<OHLCV>();
            }

            if (startTime.HasValue && endTime.HasValue)
            {
                var startMs = startTime.Value.ToUnixTimeMilliseconds();
                var endMs = endTime.Value.ToUnixTimeMilliseconds();
                return bars
                    .Where(b => (b.timestamp ?? 0) >= startMs && (b.timestamp ?? 0) <= endMs)
                    .ToList();
            }

            if (barCount <= 0 || bars.Count <= barCount)
            {
                return new List<OHLCV>(bars);
            }

            return bars.GetRange(bars.Count - barCount, barCount);
        }

        private static List<long> BuildIntersection(IEnumerable<List<long>> sources)
        {
            HashSet<long>? result = null;
            foreach (var source in sources)
            {
                var set = new HashSet<long>(source.Where(ts => ts > 0));
                if (result == null)
                {
                    result = set;
                }
                else
                {
                    result.IntersectWith(set);
                }
            }

            if (result == null || result.Count == 0)
            {
                return new List<long>();
            }

            var list = result.ToList();
            list.Sort();
            return list;
        }

        private static bool TryResolveBars(
            BacktestMarketDataProvider provider,
            string exchange,
            string timeframe,
            IReadOnlyList<string> symbols,
            long timestamp,
            out Dictionary<string, OHLCV> mainBars,
            out Dictionary<string, OHLCV> priceBars)
        {
            mainBars = new Dictionary<string, OHLCV>(StringComparer.OrdinalIgnoreCase);
            priceBars = new Dictionary<string, OHLCV>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols)
            {
                if (!provider.TryGetBar(exchange, timeframe, symbol, timestamp, out var bar))
                {
                    return false;
                }

                mainBars[symbol] = bar;

                var priceBar = bar;
                if (!string.Equals(timeframe, "1m", StringComparison.OrdinalIgnoreCase))
                {
                    if (!provider.TryGetBar(exchange, "1m", symbol, timestamp, out priceBar))
                    {
                        return false;
                    }
                }

                priceBars[symbol] = priceBar;
            }

            return true;
        }

        private static bool TryResolveBar(
            BacktestMarketDataProvider provider,
            string exchange,
            string timeframe,
            string symbol,
            long timestamp,
            out OHLCV bar)
        {
            return provider.TryGetBar(exchange, timeframe, symbol, timestamp, out bar);
        }
        private static BacktestEquityPoint BuildEquityPoint(
            SymbolRuntime runtime,
            OHLCV priceBar,
            decimal initialCapital,
            long timestamp)
        {
            var realized = runtime.State.RealizedPnl;
            var unrealized = 0m;

            if (runtime.State.Position != null)
            {
                var close = Convert.ToDecimal(priceBar.close ?? priceBar.open ?? 0d);
                if (close > 0)
                {
                    var position = runtime.State.Position;
                    unrealized = position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                        ? (close - position.EntryPrice) * position.Qty * position.ContractSize
                        : (position.EntryPrice - close) * position.Qty * position.ContractSize;
                }
            }

            return new BacktestEquityPoint
            {
                Timestamp = timestamp,
                Equity = initialCapital + realized + unrealized,
                RealizedPnl = realized,
                UnrealizedPnl = unrealized
            };
        }

        private static BacktestStats BuildStats(
            IReadOnlyList<BacktestTrade> trades,
            IReadOnlyList<BacktestEquityPoint>? equityCurve,
            decimal initialCapital)
        {
            var totalProfit = trades.Sum(t => t.PnL);
            var tradeCount = trades.Count;
            var winTrades = trades.Where(t => t.PnL > 0).ToList();
            var lossTrades = trades.Where(t => t.PnL < 0).ToList();
            var winCount = winTrades.Count;
            var lossCount = lossTrades.Count;
            var winSum = winTrades.Sum(t => t.PnL);
            var lossSum = lossTrades.Sum(t => t.PnL);

            var winRate = tradeCount > 0 ? winCount / (decimal)tradeCount : 0m;
            var avgProfit = tradeCount > 0 ? totalProfit / tradeCount : 0m;
            var avgWin = winCount > 0 ? winSum / winCount : 0m;
            var avgLoss = lossCount > 0 ? lossSum / lossCount : 0m;
            var profitFactor = lossSum < 0 ? winSum / Math.Abs(lossSum) : 0m;
            var totalReturn = initialCapital > 0 ? totalProfit / initialCapital : 0m;
            var maxDrawdown = equityCurve == null || equityCurve.Count == 0
                ? 0m
                : ComputeMaxDrawdown(equityCurve);

            return new BacktestStats
            {
                TotalProfit = totalProfit,
                TotalReturn = totalReturn,
                MaxDrawdown = maxDrawdown,
                WinRate = winRate,
                TradeCount = tradeCount,
                AvgProfit = avgProfit,
                ProfitFactor = profitFactor,
                AvgWin = avgWin,
                AvgLoss = avgLoss
            };
        }

        private static decimal ComputeMaxDrawdown(IReadOnlyList<BacktestEquityPoint> curve)
        {
            var peak = 0m;
            var maxDrawdown = 0m;

            foreach (var point in curve)
            {
                if (point.Equity > peak)
                {
                    peak = point.Equity;
                }

                if (peak <= 0)
                {
                    continue;
                }

                var drawdown = (peak - point.Equity) / peak;
                if (drawdown > maxDrawdown)
                {
                    maxDrawdown = drawdown;
                }
            }

            return maxDrawdown;
        }

        private static List<BacktestEquityPoint> BuildTotalEquityCurve(List<List<BacktestEquityPoint>> curves)
        {
            if (curves.Count == 0)
            {
                return new List<BacktestEquityPoint>();
            }

            var minCount = curves.Min(c => c.Count);
            if (minCount <= 0)
            {
                return new List<BacktestEquityPoint>();
            }

            var result = new List<BacktestEquityPoint>(minCount);
            for (var i = 0; i < minCount; i++)
            {
                var timestamp = curves[0][i].Timestamp;
                var equity = 0m;
                var realized = 0m;
                var unrealized = 0m;
                var periodRealized = 0m;
                var periodUnrealized = 0m;

                foreach (var curve in curves)
                {
                    equity += curve[i].Equity;
                    realized += curve[i].RealizedPnl;
                    unrealized += curve[i].UnrealizedPnl;
                    periodRealized += curve[i].PeriodRealizedPnl;
                    periodUnrealized += curve[i].PeriodUnrealizedPnl;
                }

                result.Add(new BacktestEquityPoint
                {
                    Timestamp = timestamp,
                    Equity = equity,
                    RealizedPnl = realized,
                    UnrealizedPnl = unrealized,
                    PeriodRealizedPnl = periodRealized,
                    PeriodUnrealizedPnl = periodUnrealized
                });
            }

            return result;
        }

        private static long AlignBucketStart(long timestamp, long intervalMs)
        {
            if (intervalMs <= 0)
            {
                return timestamp;
            }

            var mod = timestamp % intervalMs;
            if (mod < 0)
            {
                return timestamp - mod - intervalMs;
            }

            return timestamp - mod;
        }

        private static (string Granularity, long IntervalMs) ResolveEquityCurveGranularity(string? rawGranularity)
        {
            var normalized = string.IsNullOrWhiteSpace(rawGranularity)
                ? "1m"
                : rawGranularity.Trim().ToLowerInvariant();

            if (EquityGranularityToMs.TryGetValue(normalized, out var intervalMs))
            {
                return (normalized, intervalMs);
            }

            throw new InvalidOperationException(
                $"资金曲线颗粒度不支持: {rawGranularity}，仅支持 {string.Join("/", SupportedEquityGranularities)}");
        }

        private StrategyRuntimeGate ResolveRuntimeGate(SymbolRuntime runtime, DateTimeOffset currentTime, bool recordEvent)
        {
            if (!runtime.UseRuntimeGate)
            {
                return StrategyRuntimeGate.AllowAll;
            }

            var evaluation = runtime.RuntimeSchedule.Evaluate(currentTime);
            if (evaluation.Changed && recordEvent)
            {
                runtime.State.Events.Add(new BacktestEvent
                {
                    Timestamp = currentTime.ToUnixTimeMilliseconds(),
                    Type = "RuntimeGate",
                    Message = $"运行时间切换: 允许={evaluation.Allowed} 模式={runtime.RuntimeSchedule.Summary}"
                });
            }

            if (evaluation.Allowed)
            {
                return StrategyRuntimeGate.AllowAll;
            }

            return runtime.RuntimeSchedule.Policy == StrategyRuntimeOutOfSessionPolicy.BlockAll
                ? StrategyRuntimeGate.BlockAll
                : StrategyRuntimeGate.BlockEntryAllowExit;
        }
        private void ExecuteLogic(
            StrategyExecutionContext context,
            StrategyRuntimeGate runtimeGate,
            ConditionEvaluator evaluator,
            LogicTiming? timing)
        {
            var logic = context.StrategyConfig.Logic;
            if (logic == null)
            {
                return;
            }

            if (runtimeGate.AllowExit)
            {
                var exitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                ExecuteBranch(context, logic.Exit.Long, "Exit.Long", evaluator, LogicBranchKind.Exit, timing);
                timing?.AddBranch(LogicBranchKind.Exit, System.Diagnostics.Stopwatch.GetTimestamp() - exitStart);
            }

            if (context.Strategy.State == StrategyState.PausedOpenPosition)
            {
                return;
            }

            if (!runtimeGate.AllowEntry)
            {
                return;
            }

            var filterStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!EvaluateEntryFilters(context, logic.Entry.Long, "Entry.Long", evaluator))
            {
                if (timing != null)
                {
                    timing.EntryFilterTicks += System.Diagnostics.Stopwatch.GetTimestamp() - filterStart;
                }
                return;
            }
            if (timing != null)
            {
                timing.EntryFilterTicks += System.Diagnostics.Stopwatch.GetTimestamp() - filterStart;
            }

            var entryStart = System.Diagnostics.Stopwatch.GetTimestamp();
            ExecuteBranch(context, logic.Entry.Long, "Entry.Long", evaluator, LogicBranchKind.Entry, timing);
            timing?.AddBranch(LogicBranchKind.Entry, System.Diagnostics.Stopwatch.GetTimestamp() - entryStart);
        }

        private bool EvaluateEntryFilters(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage,
            ConditionEvaluator evaluator)
        {
            if (branch == null || !branch.Enabled)
            {
                return true;
            }

            var filters = branch.Filters;
            if (filters == null || !filters.Enabled)
            {
                return true;
            }

            if (filters.Groups == null || filters.Groups.Count == 0)
            {
                return true;
            }

            PrecomputeRequiredConditions(context, filters, evaluator);
            var results = new List<ConditionEvaluationResult>();
            var stageLabel = $"{stage}.Filter";
            var pass = EvaluateChecks(context, filters, results, stageLabel, evaluator);
            // 性能优先：回测高频链路关闭逐K线调试日志

            return pass;
        }

        private void ExecuteBranch(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage,
            ConditionEvaluator evaluator,
            LogicBranchKind branchKind,
            LogicTiming? timing)
        {
            if (branch == null || !branch.Enabled)
            {
                return;
            }

            var containers = branch.Containers ?? new List<ConditionContainer>();
            var passCount = 0;
            var aggregatedResults = new List<ConditionEvaluationResult>();

            PrecomputeRequiredConditions(context, containers, evaluator);

            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (container == null)
                {
                    continue;
                }

                var checkResults = new List<ConditionEvaluationResult>();
                var stageLabel = $"{stage}[{i}]";

                var checkStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var checkPass = EvaluateChecks(context, container.Checks, checkResults, stageLabel, evaluator);
                timing?.AddCheck(branchKind, System.Diagnostics.Stopwatch.GetTimestamp() - checkStart);
                if (!checkPass)
                {
                    continue;
                }

                passCount++;
                aggregatedResults.AddRange(checkResults);
            }

            if (passCount < branch.MinPassConditionContainer)
            {
                return;
            }

            var actionStart = System.Diagnostics.Stopwatch.GetTimestamp();
            ExecuteActions(context, branch.OnPass, aggregatedResults, stage);
            timing?.AddAction(branchKind, System.Diagnostics.Stopwatch.GetTimestamp() - actionStart);
        }

        private static void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            IReadOnlyList<ConditionContainer> containers,
            ConditionEvaluator evaluator)
        {
            if (containers == null || containers.Count == 0)
            {
                return;
            }

            foreach (var container in containers)
            {
                if (container?.Checks == null)
                {
                    continue;
                }

                if (container.Checks.Groups == null)
                {
                    continue;
                }

                foreach (var group in container.Checks.Groups)
                {
                    if (group?.Conditions == null || !group.Enabled)
                    {
                        continue;
                    }

                    foreach (var condition in group.Conditions)
                    {
                        if (condition == null || !condition.Enabled || !condition.Required)
                        {
                            continue;
                        }

                        evaluator.Evaluate(context, condition);
                    }
                }
            }
        }

        private static void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            ConditionGroupSet? checks,
            ConditionEvaluator evaluator)
        {
            if (checks == null || !checks.Enabled || checks.Groups == null || checks.Groups.Count == 0)
            {
                return;
            }

            foreach (var group in checks.Groups)
            {
                if (group?.Conditions == null || !group.Enabled)
                {
                    continue;
                }

                foreach (var condition in group.Conditions)
                {
                    if (condition == null || !condition.Enabled || !condition.Required)
                    {
                        continue;
                    }

                    evaluator.Evaluate(context, condition);
                }
            }
        }

        private bool EvaluateChecks(
            StrategyExecutionContext context,
            ConditionGroupSet checks,
            List<ConditionEvaluationResult> results,
            string stage,
            ConditionEvaluator evaluator)
        {
            if (checks == null || !checks.Enabled)
            {
                return false;
            }

            var groups = checks.Groups ?? new List<ConditionGroup>();
            var passGroups = 0;

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null || !group.Enabled)
                {
                    continue;
                }

                var crossRequired = new List<StrategyMethod>();
                var crossOptional = new List<StrategyMethod>();
                var required = new List<StrategyMethod>();
                var optional = new List<StrategyMethod>();

                // 重要：优先级拆分只调整评估顺序，不改变 Required/Optional 语义。
                ConditionPriorityHelper.SplitByPriority(
                    group.Conditions ?? new List<StrategyMethod>(),
                    crossRequired,
                    crossOptional,
                    required,
                    optional);

                var hasEnabled = crossRequired.Count + crossOptional.Count + required.Count + optional.Count > 0;
                if (!hasEnabled)
                {
                    if (group.MinPassConditions <= 0)
                    {
                        passGroups++;
                    }
                    continue;
                }

                var requiredFailed = false;
                var optionalPassCount = 0;

                bool EvaluateCondition(StrategyMethod condition, bool isRequired)
                {
                    var result = evaluator.Evaluate(context, condition);
                    results.Add(result);

                    if (isRequired && !result.Success)
                    {
                        requiredFailed = true;
                        return false;
                    }

                    if (!isRequired && result.Success)
                    {
                        optionalPassCount++;
                    }

                    return result.Success;
                }

                foreach (var condition in crossRequired)
                {
                    EvaluateCondition(condition, true);
                    if (requiredFailed)
                    {
                        break;
                    }
                }

                if (requiredFailed)
                {
                    continue;
                }

                foreach (var condition in crossOptional)
                {
                    EvaluateCondition(condition, false);
                }

                foreach (var condition in required)
                {
                    EvaluateCondition(condition, true);
                    if (requiredFailed)
                    {
                        break;
                    }
                }

                if (requiredFailed)
                {
                    continue;
                }

                if (optionalPassCount < group.MinPassConditions)
                {
                    foreach (var condition in optional)
                    {
                        EvaluateCondition(condition, false);
                        if (optionalPassCount >= group.MinPassConditions)
                        {
                            break;
                        }
                    }
                }

                var pass = optionalPassCount >= group.MinPassConditions;
                if (!pass)
                {
                    // 性能优先：回测高频链路关闭逐条件调试日志
                }
                else
                {
                    passGroups++;
                }
            }

            return passGroups >= checks.MinPassGroups;
        }

        private void ExecuteActions(
            StrategyExecutionContext context,
            ActionSet actions,
            IReadOnlyList<ConditionEvaluationResult> triggerResults,
            string stage)
        {
            if (actions == null || !actions.Enabled)
            {
                return;
            }

            var optionalSuccessCount = 0;
            var hasEnabled = false;

            foreach (var action in actions.Conditions ?? new List<StrategyMethod>())
            {
                if (action == null || !action.Enabled)
                {
                    continue;
                }

                hasEnabled = true;
                var result = ActionMethodRegistry.Run(context, action, triggerResults);

                if (action.Required && !result.Success)
                {
                    return;
                }

                if (!action.Required && result.Success)
                {
                    optionalSuccessCount++;
                }
            }

            if (!hasEnabled)
            {
                return;
            }

            if (optionalSuccessCount < actions.MinPassConditions)
            {
                // 性能优先：回测高频链路关闭逐动作调试日志
            }
        }
        private static List<IndicatorRequest> BuildIndicatorRequests(StrategyModel strategy)
        {
            var trade = strategy.StrategyConfig?.Trade;
            var logic = strategy.StrategyConfig?.Logic;
            if (trade == null || logic == null)
            {
                return new List<IndicatorRequest>();
            }

            var requests = new Dictionary<IndicatorKey, IndicatorRequest>();
            foreach (var method in EnumerateMethods(logic))
            {
                if (method.Args == null)
                {
                    continue;
                }

                foreach (var reference in method.Args)
                {
                    if (!string.Equals(reference.RefType, "Indicator", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var request = IndicatorKeyFactory.BuildRequest(trade, reference);
                    if (request == null)
                    {
                        continue;
                    }

                    if (requests.TryGetValue(request.Key, out var existing))
                    {
                        requests[request.Key] = existing.WithMaxOffset(Math.Max(existing.MaxOffset, request.MaxOffset));
                    }
                    else
                    {
                        requests[request.Key] = request;
                    }
                }
            }

            return requests.Values.ToList();
        }

        private static IEnumerable<StrategyMethod> EnumerateMethods(StrategyLogic logic)
        {
            foreach (var method in EnumerateBranch(logic.Entry.Long))
            {
                yield return method;
            }

            foreach (var method in EnumerateBranch(logic.Entry.Short))
            {
                yield return method;
            }

            foreach (var method in EnumerateBranch(logic.Exit.Long))
            {
                yield return method;
            }

            foreach (var method in EnumerateBranch(logic.Exit.Short))
            {
                yield return method;
            }
        }

        private static IEnumerable<StrategyMethod> EnumerateBranch(StrategyLogicBranch branch)
        {
            if (branch == null)
            {
                yield break;
            }

            if (branch.Filters?.Groups != null)
            {
                foreach (var group in branch.Filters.Groups)
                {
                    if (group?.Conditions == null)
                    {
                        continue;
                    }

                    foreach (var condition in group.Conditions)
                    {
                        if (condition != null)
                        {
                            yield return condition;
                        }
                    }
                }
            }

            if (branch.Containers != null)
            {
                foreach (var container in branch.Containers)
                {
                    if (container == null)
                    {
                        continue;
                    }

                    if (container.Checks?.Groups != null)
                    {
                        foreach (var group in container.Checks.Groups)
                        {
                            if (group?.Conditions == null)
                            {
                                continue;
                            }

                            foreach (var condition in group.Conditions)
                            {
                                if (condition != null)
                                {
                                    yield return condition;
                                }
                            }
                        }
                    }
                }
            }

            if (branch.OnPass?.Conditions != null)
            {
                foreach (var action in branch.OnPass.Conditions)
                {
                    if (action != null)
                    {
                        yield return action;
                    }
                }
            }
        }

        private static Dictionary<string, int> BuildWarmupMap(List<IndicatorRequest> requests)
        {
            var warmup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var calculator = new TalibIndicatorCalculator(null);

            foreach (var request in requests)
            {
                if (request == null)
                {
                    continue;
                }

                var func = calculator.ResolveFunction(request.Key.Indicator);
                if (func == null)
                {
                    continue;
                }

                var lookback = calculator.GetLookback(func, request.Parameters ?? Array.Empty<double>());
                var requiredBars = Math.Max(5, lookback + request.MaxOffset + 2);

                if (!warmup.TryGetValue(request.Key.Timeframe, out var existing) || requiredBars > existing)
                {
                    warmup[request.Key.Timeframe] = requiredBars;
                }
            }

            return warmup;
        }

        private static HashSet<string> BuildRequiredTimeframes(List<IndicatorRequest> requests, string primaryTimeframe)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                primaryTimeframe
            };

            foreach (var request in requests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Key.Timeframe))
                {
                    continue;
                }

                set.Add(request.Key.Timeframe);
            }

            if (!string.Equals(primaryTimeframe, "1m", StringComparison.OrdinalIgnoreCase))
            {
                set.Add("1m");
            }

            return set;
        }

        private static OHLCV ToOhlcv(HistoricalMarketDataKlineRow row)
        {
            return new OHLCV
            {
                timestamp = row.OpenTime,
                open = row.Open.HasValue ? (double)row.Open.Value : null,
                high = row.High.HasValue ? (double)row.High.Value : null,
                low = row.Low.HasValue ? (double)row.Low.Value : null,
                close = row.Close.HasValue ? (double)row.Close.Value : null,
                volume = row.Volume.HasValue ? (double)row.Volume.Value : null
            };
        }

        private static string BuildTableName(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);

            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!SafeIdentifier.IsMatch(exchangeKey) || !SafeIdentifier.IsMatch(symbolPart) || !SafeIdentifier.IsMatch(timeframeKey))
            {
                throw new InvalidOperationException("历史行情表名不合法");
            }

            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }

        private static decimal ApplySlippage(decimal price, string orderSide, int slippageBps)
        {
            if (slippageBps == 0)
            {
                return price;
            }

            var ratio = slippageBps / 10000m;
            return orderSide.Equals("buy", StringComparison.OrdinalIgnoreCase)
                ? price * (1 + ratio)
                : price * (1 - ratio);
        }

        private readonly struct StrategyRuntimeGate
        {
            private StrategyRuntimeGate(bool allowEntry, bool allowExit)
            {
                AllowEntry = allowEntry;
                AllowExit = allowExit;
            }

            public bool AllowEntry { get; }
            public bool AllowExit { get; }

            public static StrategyRuntimeGate AllowAll => new(true, true);
            public static StrategyRuntimeGate BlockEntryAllowExit => new(false, true);
            public static StrategyRuntimeGate BlockAll => new(false, false);
        }
    }
}
