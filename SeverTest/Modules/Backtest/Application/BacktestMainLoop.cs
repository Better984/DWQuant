using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Config;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyEngine.Domain;
using ServerTest.Options;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测主循环执行器：
    /// 负责指标计算、条件评估、动作执行、风控处理与收尾强平。
    /// </summary>
    public sealed class BacktestMainLoop
    {
        private static readonly long MainLoopProgressIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 2;
        internal static readonly long PositionProgressIntervalTicks = System.Diagnostics.Stopwatch.Frequency / 10;
        private const int DefaultInnerParallelism = 4;
        private const int DefaultBatchCloseParallelism = 0;
        private const int DefaultBatchCloseProgressChunkSize = 512;
        private const int DefaultBatchCloseCandidateSliceSize = 100;
        private const int BatchPreviewTradeLimit = 100;
        private static readonly IReadOnlyList<ConditionContainer> EmptyConditionContainers = Array.Empty<ConditionContainer>();
        private static readonly IReadOnlyList<ConditionGroup> EmptyConditionGroups = Array.Empty<ConditionGroup>();
        private static readonly IReadOnlyList<StrategyMethod> EmptyStrategyMethods = Array.Empty<StrategyMethod>();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessorNumber();

        private readonly RuntimeQueueOptions _queueOptions;
        private readonly ServerConfigStore _configStore;
        private readonly BacktestObjectPoolManager _objectPoolManager;
        private readonly BacktestProgressPushService _progressPushService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<BacktestMainLoop> _logger;

        public BacktestMainLoop(
            IOptions<RuntimeQueueOptions> queueOptions,
            ServerConfigStore configStore,
            BacktestObjectPoolManager objectPoolManager,
            BacktestProgressPushService progressPushService,
            ILoggerFactory loggerFactory,
            ILogger<BacktestMainLoop> logger)
        {
            _queueOptions = queueOptions?.Value ?? new RuntimeQueueOptions();
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _objectPoolManager = objectPoolManager ?? throw new ArgumentNullException(nameof(objectPoolManager));
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal async Task<BacktestMainLoopResult> ExecuteAsync(
            string exchange,
            string timeframe,
            long timeframeMs,
            IReadOnlyList<string> symbols,
            IReadOnlyList<long> drivingTimestamps,
            Dictionary<string, BacktestSymbolRuntime> symbolRuntimes,
            BacktestMarketDataProvider provider,
            BacktestOutputOptions output,
            decimal initialCapital,
            long equityCurveGranularityMs,
            BacktestProgressContext? progressContext,
            CancellationToken ct)
        {
            if (symbols == null) throw new ArgumentNullException(nameof(symbols));
            if (drivingTimestamps == null) throw new ArgumentNullException(nameof(drivingTimestamps));
            if (symbolRuntimes == null) throw new ArgumentNullException(nameof(symbolRuntimes));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (output == null) throw new ArgumentNullException(nameof(output));

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

            Dictionary<string, EquityCurveAggregator>? equityCurveCollectors = null;
            if (output.IncludeEquityCurve)
            {
                equityCurveCollectors = symbolRuntimes.Values.ToDictionary(
                    r => r.Symbol,
                    r => new EquityCurveAggregator(equityCurveGranularityMs, drivingTimestamps.Count),
                    StringComparer.OrdinalIgnoreCase);
            }

            var loopSw = System.Diagnostics.Stopwatch.StartNew();
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
            var entryTicks = 0L;
            var exitTicks = 0L;
            var entryFilterTicks = 0L;
            var exitCheckTicks = 0L;
            var entryCheckTicks = 0L;
            var exitActionTicks = 0L;
            var entryActionTicks = 0L;
            var nextLoopProgressTick = System.Diagnostics.Stopwatch.GetTimestamp() + MainLoopProgressIntervalTicks;
            var innerParallelism = ResolveInnerParallelism(symbols.Count);
            var runParallelPerTimestamp = innerParallelism > 1 && symbols.Count > 1;
            var timelineParallelTracker = runParallelPerTimestamp
                ? new ParallelExecutionParticipationTracker()
                : null;
            var normalizedExchange = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var normalizedTimeframe = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var normalizedOneMinuteTimeframe = MarketDataKeyNormalizer.NormalizeTimeframe("1m");
            var needOneMinutePrice = !string.Equals(normalizedTimeframe, normalizedOneMinuteTimeframe, StringComparison.Ordinal);
            var mainSeriesKeys = BuildSeriesKeys(normalizedExchange, normalizedTimeframe, symbols);
            var priceSeriesKeys = needOneMinutePrice
                ? BuildSeriesKeys(normalizedExchange, normalizedOneMinuteTimeframe, symbols)
                : mainSeriesKeys;

            LogSystemInfo(
                "主循环并行配置：标的数={SymbolCount} 并行度={InnerParallelism} 启用并行={ParallelEnabled}",
                symbols.Count,
                innerParallelism,
                runParallelPerTimestamp);

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

            SymbolExecutionMetrics ExecuteSymbolAtTimestamp(
                string symbol,
                long timestamp,
                Dictionary<string, OHLCV> mainBars,
                Dictionary<string, OHLCV> priceBars)
            {
                ct.ThrowIfCancellationRequested();

                var runtime = symbolRuntimes[symbol];
                var bar = mainBars[symbol];
                var priceBar = priceBars[symbol];
                var riskElapsed = 0L;
                var contextElapsed = 0L;
                var runtimeElapsed = 0L;
                var logicElapsed = 0L;
                var equityElapsed = 0L;

                if (runtime.State.Position != null)
                {
                    var riskStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    actionExecutor.ApplyFundingRate(runtime.State, bar, timestamp, timeframeMs);
                    actionExecutor.TryProcessRisk(runtime.State, bar, timestamp);
                    riskElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - riskStart;
                }

                var marketTask = new MarketDataTask(exchange, symbol, timeframe, timestamp, true);
                if (runtime.IndicatorRequests.Count > 0)
                {
                    var indicatorTask = _objectPoolManager.RentIndicatorTask(marketTask, runtime.IndicatorRequests);
                    try
                    {
                        indicatorEngine.ProcessTaskNow(indicatorTask);
                    }
                    finally
                    {
                        _objectPoolManager.ReturnIndicatorTask(indicatorTask);
                    }
                }

                var contextStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var context = _objectPoolManager.RentStrategyExecutionContext(
                    runtime.Strategy,
                    marketTask,
                    valueResolver,
                    actionExecutor);
                try
                {
                    contextElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - contextStart;

                    var runtimeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var runtimeGate = ResolveRuntimeGate(runtime, context.CurrentTime, output.IncludeEvents);
                    runtimeElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - runtimeStart;

                    var symbolLogicTiming = new LogicTiming();
                    var logicStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    ExecuteLogic(context, runtimeGate, conditionEvaluator, symbolLogicTiming);
                    logicElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - logicStart;

                    if (output.IncludeEquityCurve && equityCurveCollectors != null)
                    {
                        var equityStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        var equity = BuildEquityPoint(runtime, priceBar, initialCapital, timestamp);
                        equityCurveCollectors[runtime.Symbol].Add(equity);
                        equityElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - equityStart;
                    }

                    return new SymbolExecutionMetrics(
                        riskElapsed,
                        contextElapsed,
                        runtimeElapsed,
                        logicElapsed,
                        equityElapsed,
                        symbolLogicTiming.EntryTicks,
                        symbolLogicTiming.ExitTicks,
                        symbolLogicTiming.EntryFilterTicks,
                        symbolLogicTiming.ExitCheckTicks,
                        symbolLogicTiming.EntryCheckTicks,
                        symbolLogicTiming.ExitActionTicks,
                        symbolLogicTiming.EntryActionTicks);
                }
                finally
                {
                    _objectPoolManager.ReturnStrategyExecutionContext(context);
                }
            }

            var mainBars = _objectPoolManager.RentBarDictionary();
            var priceBars = _objectPoolManager.RentBarDictionary();
            ParallelOptions? timelineParallelOptions = null;
            if (runParallelPerTimestamp)
            {
                timelineParallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = innerParallelism,
                    CancellationToken = ct
                };
            }

            try
            {
                foreach (var timestamp in drivingTimestamps)
                {
                    ct.ThrowIfCancellationRequested();
                    var loopStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (!TryResolveBars(
                            provider,
                            symbols,
                            mainSeriesKeys,
                            priceSeriesKeys,
                            needOneMinutePrice,
                            timestamp,
                            mainBars,
                            priceBars))
                    {
                        continue;
                    }
                    resolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - loopStart;

                    // 时间轴保持串行，确保同一 symbol 的状态按时间顺序推进；
                    // 每个时间点内部按 symbol 并行，提升单任务吞吐。
                    var updateStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    actionExecutor.UpdateCurrentBars(priceBars);
                    updateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - updateStart;

                    if (runParallelPerTimestamp)
                    {
                        Parallel.ForEach(symbols, timelineParallelOptions!, symbol =>
                        {
                            timelineParallelTracker?.RecordCurrentThread();
                            var metrics = ExecuteSymbolAtTimestamp(symbol, timestamp, mainBars, priceBars);
                            Interlocked.Add(ref riskTicks, metrics.RiskTicks);
                            Interlocked.Add(ref contextTicks, metrics.ContextTicks);
                            Interlocked.Add(ref runtimeTicks, metrics.RuntimeTicks);
                            Interlocked.Add(ref logicTicks, metrics.LogicTicks);
                            Interlocked.Add(ref equityTicks, metrics.EquityTicks);
                            Interlocked.Add(ref entryTicks, metrics.EntryTicks);
                            Interlocked.Add(ref exitTicks, metrics.ExitTicks);
                            Interlocked.Add(ref entryFilterTicks, metrics.EntryFilterTicks);
                            Interlocked.Add(ref exitCheckTicks, metrics.ExitCheckTicks);
                            Interlocked.Add(ref entryCheckTicks, metrics.EntryCheckTicks);
                            Interlocked.Add(ref exitActionTicks, metrics.ExitActionTicks);
                            Interlocked.Add(ref entryActionTicks, metrics.EntryActionTicks);
                            Interlocked.Increment(ref loopSymbols);
                        });
                    }
                    else
                    {
                        foreach (var symbol in symbols)
                        {
                            var metrics = ExecuteSymbolAtTimestamp(symbol, timestamp, mainBars, priceBars);
                            riskTicks += metrics.RiskTicks;
                            contextTicks += metrics.ContextTicks;
                            runtimeTicks += metrics.RuntimeTicks;
                            logicTicks += metrics.LogicTicks;
                            equityTicks += metrics.EquityTicks;
                            entryTicks += metrics.EntryTicks;
                            exitTicks += metrics.ExitTicks;
                            entryFilterTicks += metrics.EntryFilterTicks;
                            exitCheckTicks += metrics.ExitCheckTicks;
                            entryCheckTicks += metrics.EntryCheckTicks;
                            exitActionTicks += metrics.ExitActionTicks;
                            entryActionTicks += metrics.EntryActionTicks;
                            loopSymbols++;
                        }
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
            }
            finally
            {
                _objectPoolManager.ReturnBarDictionary(mainBars);
                _objectPoolManager.ReturnBarDictionary(priceBars);
            }

            loopSw.Stop();
            var loopBarsPerSecond = loopSw.Elapsed.TotalSeconds > 0
                ? loopBars / loopSw.Elapsed.TotalSeconds
                : loopBars;
            LogSystemInfo(
                "主循环耗时拆分：K线数={Bars} 标的执行次数={Symbols} 总耗时={Total}ms 取数={Resolve}ms 更新价={Update}ms 风控={Risk}ms 上下文={Context}ms 运行时间门禁={Runtime}ms 逻辑执行={Logic}ms 权益计算={Equity}ms",
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
                "逻辑耗时拆分：开仓总计={Entry}ms 平仓总计={Exit}ms 开仓过滤={EntryFilter}ms 开仓检查={EntryCheck}ms 开仓动作={EntryAction}ms 平仓检查={ExitCheck}ms 平仓动作={ExitAction}ms",
                ToMs(entryTicks),
                ToMs(exitTicks),
                ToMs(entryFilterTicks),
                ToMs(entryCheckTicks),
                ToMs(entryActionTicks),
                ToMs(exitCheckTicks),
                ToMs(exitActionTicks));
            if (timelineParallelTracker != null && timelineParallelTracker.HasData)
            {
                LogSystemInfo(
                    "主循环并行线程参与：线程数={ThreadCount} 参与核心数={CoreCount} 核心列表={CoreList} 线程核心映射={ThreadCoreMap}",
                    timelineParallelTracker.ThreadCount,
                    timelineParallelTracker.CoreCount,
                    timelineParallelTracker.CoreList,
                    timelineParallelTracker.ThreadCoreMap);
            }
            LogSystemInfo(
                "主循环完成：驱动K线={Bars} 标的数={Symbols} 阶段耗时={Elapsed}ms 吞吐={BarsPerSecond}/秒",
                drivingTimestamps.Count,
                symbols.Count,
                loopSw.ElapsedMilliseconds,
                loopBarsPerSecond);
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

            var lastTimestamp = drivingTimestamps[^1];
            foreach (var runtime in symbolRuntimes.Values)
            {
                if (runtime.State.Position == null)
                {
                    continue;
                }

                var normalizedSymbol = MarketDataKeyNormalizer.NormalizeSymbol(runtime.Symbol);
                var oneMinuteKey = BacktestMarketDataProvider.BuildKeyFromNormalized(
                    normalizedExchange,
                    normalizedSymbol,
                    normalizedOneMinuteTimeframe);
                var mainKey = BacktestMarketDataProvider.BuildKeyFromNormalized(
                    normalizedExchange,
                    normalizedSymbol,
                    normalizedTimeframe);
                if (!provider.TryGetBarBySeriesKey(oneMinuteKey, lastTimestamp, out var lastPriceBar) &&
                    !provider.TryGetBarBySeriesKey(mainKey, lastTimestamp, out lastPriceBar))
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
                var execPrice = BacktestSlippageHelper.ApplySlippage(closePrice, orderSide, runtime.State.SlippageBps);
                actionExecutor.ClosePosition(runtime.Symbol, execPrice, lastTimestamp, "End");

                if (output.IncludeEquityCurve && equityCurveCollectors != null)
                {
                    var equity = BuildEquityPoint(runtime, lastPriceBar, initialCapital, lastTimestamp);
                    equityCurveCollectors[runtime.Symbol].Add(equity);
                }
            }

            Dictionary<string, List<BacktestEquityPoint>>? equityCurvesBySymbol = null;
            if (output.IncludeEquityCurve && equityCurveCollectors != null)
            {
                equityCurvesBySymbol = new Dictionary<string, List<BacktestEquityPoint>>(StringComparer.OrdinalIgnoreCase);
                foreach (var runtime in symbolRuntimes.Values)
                {
                    var curve = equityCurveCollectors[runtime.Symbol].Build();
                    equityCurvesBySymbol[runtime.Symbol] = curve;
                    runtime.Result.EquitySummary = BacktestStatisticsBuilder.BuildEquitySummary(curve);
                    runtime.Result.EquityCurveRaw = BacktestStatisticsBuilder.SerializeToRawList(curve);
                }
            }

            return new BacktestMainLoopResult
            {
                EquityCurvesBySymbol = equityCurvesBySymbol
            };
        }

        /// <summary>
        /// 高速模式：
        /// - Phase1：先批量检测开仓信号
        /// - Phase2：再统一并行平仓
        /// </summary>
        internal async Task<BacktestMainLoopResult> ExecuteBatchOpenCloseAsync(
            string exchange,
            string timeframe,
            long timeframeMs,
            IReadOnlyList<string> symbols,
            IReadOnlyList<long> drivingTimestamps,
            Dictionary<string, BacktestSymbolRuntime> symbolRuntimes,
            BacktestMarketDataProvider provider,
            BacktestOutputOptions output,
            decimal initialCapital,
            long equityCurveGranularityMs,
            BacktestProgressContext? progressContext,
            CancellationToken ct)
        {
            if (symbols == null) throw new ArgumentNullException(nameof(symbols));
            if (drivingTimestamps == null) throw new ArgumentNullException(nameof(drivingTimestamps));
            if (symbolRuntimes == null) throw new ArgumentNullException(nameof(symbolRuntimes));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var batchModeSw = System.Diagnostics.Stopwatch.StartNew();
            var openParallelism = ResolveInnerParallelism(symbols.Count);
            var closeParallelism = ResolveBatchCloseParallelism();
            var allowOverlappingPositions = _configStore.GetBool("Backtest:BatchAllowOverlappingPositions", true);
            var cpuCount = Math.Max(1, Environment.ProcessorCount);

            LogSystemInfo(
                "高速批量模式启动：标的数={SymbolCount} 开仓并行度={OpenParallelism} 平仓并行度={CloseParallelism} CPU核心数={CpuCount} 允许重叠仓位={AllowOverlap}",
                symbols.Count,
                openParallelism,
                closeParallelism,
                cpuCount,
                allowOverlappingPositions);

            var prepareSw = System.Diagnostics.Stopwatch.StartNew();
            var contexts = new Dictionary<string, BatchSymbolContext>(StringComparer.OrdinalIgnoreCase);
            var needOneMinutePrice = !string.Equals(timeframe, "1m", StringComparison.OrdinalIgnoreCase);
            foreach (var symbol in symbols)
            {
                ct.ThrowIfCancellationRequested();
                if (!symbolRuntimes.TryGetValue(symbol, out var runtime))
                {
                    continue;
                }

                if (!provider.TryGetSeries(exchange, timeframe, symbol, out var mainBars, out var mainIndex))
                {
                    continue;
                }

                var priceBars = mainBars;
                var priceIndex = mainIndex;
                if (needOneMinutePrice &&
                    provider.TryGetSeries(exchange, "1m", symbol, out var oneMinuteBars, out var oneMinuteIndex))
                {
                    priceBars = oneMinuteBars;
                    priceIndex = oneMinuteIndex;
                }

                contexts[symbol] = new BatchSymbolContext(
                    runtime,
                    mainBars,
                    mainIndex,
                    priceBars,
                    priceIndex);
            }
            prepareSw.Stop();

            if (contexts.Count == 0)
            {
                LogSystemWarning("高速批量模式未找到可执行标的，数据准备耗时={Elapsed}ms，返回空结果", prepareSw.ElapsedMilliseconds);
                return new BacktestMainLoopResult();
            }

            var totalMainBars = contexts.Values.Sum(item => item.MainBars.Count);
            var totalPriceBars = contexts.Values.Sum(item => item.PriceBars.Count);
            LogSystemInfo(
                "高速批量模式数据准备完成：可执行标的={SymbolCount} 驱动K线={DrivingBars} 主周期K线总数={MainBars} 价格K线总数={PriceBars} 阶段耗时={Elapsed}ms",
                contexts.Count,
                drivingTimestamps.Count,
                totalMainBars,
                totalPriceBars,
                prepareSw.ElapsedMilliseconds);

            var openPhaseSw = System.Diagnostics.Stopwatch.StartNew();
            var totalOpenChecks = Math.Max(1L, (long)contexts.Count * Math.Max(1, drivingTimestamps.Count));
            await _progressPushService
                .PublishMessageAsync(
                    progressContext,
                    new BacktestProgressMessage
                    {
                        EventKind = "stage",
                        Stage = "batch_open_phase",
                        StageName = "第一阶段：检测开仓",
                        Message = "第一阶段正在检测开仓，已获取开仓 0 个",
                        ProcessedBars = 0,
                        TotalBars = ToSafeInt(totalOpenChecks),
                        FoundPositions = 0,
                        Progress = 0m,
                        ElapsedMs = 0,
                        Completed = false
                    },
                    ct)
                .ConfigureAwait(false);

            long openProcessedChecks = 0;
            var openSignalCount = 0;
            var openParallelTracker = new ParallelExecutionParticipationTracker();
            var openParallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = openParallelism,
                CancellationToken = ct
            };
            using var openProgressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var openProgressTask = ReportBatchOpenProgressAsync(
                progressContext,
                totalOpenChecks,
                () => Volatile.Read(ref openProcessedChecks),
                () => Volatile.Read(ref openSignalCount),
                openPhaseSw,
                openProgressCts.Token);

            try
            {
                Parallel.ForEach(
                    contexts.Values,
                    openParallelOptions,
                    context =>
                    {
                        openParallelTracker.RecordCurrentThread();
                        var candidates = CollectOpenCandidatesForSymbol(
                            exchange,
                            timeframe,
                            output,
                            context,
                            drivingTimestamps,
                            (processedDelta, signalDelta) =>
                            {
                                if (processedDelta != 0)
                                {
                                    Interlocked.Add(ref openProcessedChecks, processedDelta);
                                }

                                if (signalDelta != 0)
                                {
                                    Interlocked.Add(ref openSignalCount, signalDelta);
                                }
                            },
                            ct);
                        context.Candidates = candidates;
                    });
            }
            finally
            {
                openProgressCts.Cancel();
                await AwaitBackgroundReportAsync(openProgressTask).ConfigureAwait(false);
            }

            openPhaseSw.Stop();
            var finalOpenProcessedChecks = Math.Max(0L, Volatile.Read(ref openProcessedChecks));
            var finalOpenSignals = Math.Max(0, Volatile.Read(ref openSignalCount));
            var totalCandidates = Math.Max(0, contexts.Values.Sum(item => item.Candidates.Count));
            var openHitRatePct = finalOpenProcessedChecks > 0
                ? totalCandidates * 100m / finalOpenProcessedChecks
                : 0m;
            var openThroughput = openPhaseSw.Elapsed.TotalSeconds > 0
                ? finalOpenProcessedChecks / openPhaseSw.Elapsed.TotalSeconds
                : finalOpenProcessedChecks;
            var openTopSymbols = BuildTopBatchSymbolSummary(contexts.Values, item => item.Candidates.Count, 5);
            LogSystemInfo(
                "批量开仓检测完成：标的数={Symbols} 已检测={ProcessedChecks}/{TotalChecks} 开仓信号={SignalCount} 候选仓位={Candidates} 命中率={HitRatePct}% 耗时={Elapsed}ms 吞吐={Throughput}/秒 候选Top5={TopSymbols} 并行线程数={ThreadCount} 参与核心数={CoreCount} 核心列表={CoreList}",
                contexts.Count,
                finalOpenProcessedChecks,
                totalOpenChecks,
                finalOpenSignals,
                totalCandidates,
                openHitRatePct,
                openPhaseSw.ElapsedMilliseconds,
                openThroughput,
                openTopSymbols,
                openParallelTracker.ThreadCount,
                openParallelTracker.CoreCount,
                openParallelTracker.CoreList);
            if (openParallelTracker.HasData)
            {
                LogSystemInfo("批量开仓线程核心映射：{ThreadCoreMap}", openParallelTracker.ThreadCoreMap);
            }

            await _progressPushService
                .PublishMessageAsync(
                    progressContext,
                    new BacktestProgressMessage
                    {
                        EventKind = "stage",
                        Stage = "batch_open_phase",
                        StageName = "第一阶段：检测开仓",
                        Message = $"开仓数量检测完毕，共 {totalCandidates} 个仓位",
                        ProcessedBars = ToSafeInt(totalOpenChecks),
                        TotalBars = ToSafeInt(totalOpenChecks),
                        FoundPositions = totalCandidates,
                        Progress = 1m,
                        ElapsedMs = openPhaseSw.ElapsedMilliseconds,
                        Completed = true
                    },
                    ct)
                .ConfigureAwait(false);

            var closePhaseSw = System.Diagnostics.Stopwatch.StartNew();
            var closeParallelTracker = new ParallelExecutionParticipationTracker();
            await _progressPushService
                .PublishMessageAsync(
                    progressContext,
                    new BacktestProgressMessage
                    {
                        EventKind = "stage",
                        Stage = "batch_close_phase",
                        StageName = "第二阶段：平仓检测",
                        Message = "开始平仓检测，优先检测最近仓位",
                        ProcessedBars = 0,
                        TotalBars = totalCandidates,
                        FoundPositions = 0,
                        TotalPositions = totalCandidates,
                        Progress = 0m,
                        WinCount = 0,
                        LossCount = 0,
                        WinRate = 0m,
                        ElapsedMs = 0,
                        Completed = false
                    },
                    ct)
                .ConfigureAwait(false);

            var closeOrderedContexts = contexts.Values
                .OrderByDescending(item => item.Candidates.Count > 0 ? item.Candidates.Max(candidate => candidate.EntryTime) : long.MinValue)
                .ToList();

            long closeProcessedCandidates = 0;
            var closedPositions = 0;
            var winCount = 0;
            var lossCount = 0;
            var closePreviewTrades = new List<BacktestTrade>(BatchPreviewTradeLimit);
            var closePreviewLock = new object();
            IReadOnlyList<BacktestTrade> GetClosePreviewSnapshot()
            {
                lock (closePreviewLock)
                {
                    return closePreviewTrades.Count == 0
                        ? Array.Empty<BacktestTrade>()
                        : new List<BacktestTrade>(closePreviewTrades);
                }
            }

            CancellationTokenSource? closeProgressCts = null;
            Task? closeProgressTask = null;
            if (totalCandidates > 0)
            {
                closeProgressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                closeProgressTask = ReportBatchCloseProgressAsync(
                    progressContext,
                    totalCandidates,
                    () => Volatile.Read(ref closeProcessedCandidates),
                    () => Volatile.Read(ref closedPositions),
                    () => Volatile.Read(ref winCount),
                    () => Volatile.Read(ref lossCount),
                    GetClosePreviewSnapshot,
                    closePhaseSw,
                    closeProgressCts.Token);
            }

            try
            {
                foreach (var context in closeOrderedContexts)
                {
                    ct.ThrowIfCancellationRequested();

                    context.ClosedPositions = CloseCandidatesForSymbol(
                        context.Candidates,
                        context.PriceBars,
                        context.PriceIndex,
                        timeframeMs,
                        closeParallelism,
                        allowOverlappingPositions,
                        chunkProgress =>
                        {
                            if (chunkProgress.ProcessedCandidates > 0)
                            {
                                Interlocked.Add(ref closeProcessedCandidates, chunkProgress.ProcessedCandidates);
                            }

                            if (chunkProgress.AcceptedPositions.Count > 0)
                            {
                                var chunkClosed = chunkProgress.AcceptedPositions.Count;
                                var chunkWins = chunkProgress.AcceptedPositions.Count(item => item.Trade.PnL > 0m);
                                var chunkLosses = chunkProgress.AcceptedPositions.Count(item => item.Trade.PnL < 0m);

                                Interlocked.Add(ref closedPositions, chunkClosed);
                                Interlocked.Add(ref winCount, chunkWins);
                                Interlocked.Add(ref lossCount, chunkLosses);

                                lock (closePreviewLock)
                                {
                                    MergeRecentPreviewTrades(closePreviewTrades, chunkProgress.AcceptedPositions, BatchPreviewTradeLimit);
                                }
                            }
                        },
                        closeParallelTracker,
                        ct);

                    ApplyClosedPositions(context.Runtime, context.ClosedPositions);
                }
            }
            finally
            {
                if (closeProgressCts != null)
                {
                    closeProgressCts.Cancel();
                    if (closeProgressTask != null)
                    {
                        await AwaitBackgroundReportAsync(closeProgressTask).ConfigureAwait(false);
                    }
                }
            }

            var finalProcessedCandidates = totalCandidates > 0
                ? Math.Min(totalCandidates, ToSafeInt(Volatile.Read(ref closeProcessedCandidates)))
                : 0;
            var finalClosedPositions = Math.Max(0, Volatile.Read(ref closedPositions));
            var finalWins = Math.Max(0, Volatile.Read(ref winCount));
            var finalLosses = Math.Max(0, Volatile.Read(ref lossCount));
            var finalWinRate = finalClosedPositions > 0 ? finalWins / (decimal)finalClosedPositions : 0m;
            var finalPreviewSnapshot = GetClosePreviewSnapshot();
            closePhaseSw.Stop();
            var filteredCandidates = Math.Max(0, finalProcessedCandidates - finalClosedPositions);
            var closeThroughput = closePhaseSw.Elapsed.TotalSeconds > 0
                ? finalProcessedCandidates / closePhaseSw.Elapsed.TotalSeconds
                : finalProcessedCandidates;
            var closeTopSymbols = BuildTopBatchSymbolSummary(contexts.Values, item => item.ClosedPositions.Count, 5);

            await _progressPushService
                .PublishMessageAsync(
                    progressContext,
                    new BacktestProgressMessage
                    {
                        EventKind = "positions",
                        Stage = "batch_close_phase",
                        StageName = "第二阶段：平仓检测",
                        Message = $"平仓检测完成：{finalClosedPositions}/{totalCandidates}，胜率 {(finalWinRate * 100m):F2}%",
                        ProcessedBars = finalProcessedCandidates,
                        TotalBars = totalCandidates,
                        FoundPositions = finalClosedPositions,
                        TotalPositions = totalCandidates,
                        ChunkCount = finalPreviewSnapshot.Count,
                        WinCount = finalWins,
                        LossCount = finalLosses,
                        WinRate = finalWinRate,
                        Progress = totalCandidates > 0 ? finalProcessedCandidates / (decimal)totalCandidates : 1m,
                        ElapsedMs = closePhaseSw.ElapsedMilliseconds,
                        Positions = finalPreviewSnapshot.Count > 0 ? new List<BacktestTrade>(finalPreviewSnapshot) : null,
                        ReplacePositions = true,
                        Completed = true
                    },
                    ct)
                .ConfigureAwait(false);

            LogSystemInfo(
                "并行统一平仓完成：候选数={Candidates} 已处理={Processed} 成功平仓={Closed} 过滤淘汰={Filtered} 盈利={Win} 亏损={Loss} 胜率={WinRatePct}% 耗时={Elapsed}ms 吞吐={Throughput}/秒 平仓Top5={TopSymbols} 并行线程数={ThreadCount} 参与核心数={CoreCount} CPU核心数={CpuCount} 核心列表={CoreList}",
                totalCandidates,
                finalProcessedCandidates,
                finalClosedPositions,
                filteredCandidates,
                finalWins,
                finalLosses,
                finalWinRate * 100m,
                closePhaseSw.ElapsedMilliseconds,
                closeThroughput,
                closeTopSymbols,
                closeParallelTracker.ThreadCount,
                closeParallelTracker.CoreCount,
                cpuCount,
                closeParallelTracker.CoreList);
            if (closeParallelTracker.HasData)
            {
                LogSystemInfo("批量平仓线程核心映射：{ThreadCoreMap}", closeParallelTracker.ThreadCoreMap);
            }

            await _progressPushService
                .PublishMessageAsync(
                    progressContext,
                    new BacktestProgressMessage
                    {
                        EventKind = "stage",
                        Stage = "batch_close_phase",
                        StageName = "第二阶段：平仓检测",
                        Message = $"平仓检测完成，当前胜率 {(finalWinRate * 100m):F2}%",
                        ProcessedBars = finalProcessedCandidates,
                        TotalBars = totalCandidates,
                        FoundPositions = finalClosedPositions,
                        TotalPositions = totalCandidates,
                        WinCount = finalWins,
                        LossCount = finalLosses,
                        WinRate = finalWinRate,
                        Progress = totalCandidates > 0 ? finalProcessedCandidates / (decimal)totalCandidates : 1m,
                        ElapsedMs = closePhaseSw.ElapsedMilliseconds,
                        Completed = true
                    },
                    ct)
                .ConfigureAwait(false);

            var equityBuildSw = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<string, List<BacktestEquityPoint>>? equityCurvesBySymbol = null;
            if (output.IncludeEquityCurve)
            {
                equityCurvesBySymbol = new Dictionary<string, List<BacktestEquityPoint>>(StringComparer.OrdinalIgnoreCase);
                foreach (var context in contexts.Values)
                {
                    var curve = BuildBatchEquityCurve(
                        context,
                        drivingTimestamps,
                        initialCapital,
                        equityCurveGranularityMs);
                    equityCurvesBySymbol[context.Runtime.Symbol] = curve;
                    context.Runtime.Result.EquitySummary = BacktestStatisticsBuilder.BuildEquitySummary(curve);
                    context.Runtime.Result.EquityCurveRaw = BacktestStatisticsBuilder.SerializeToRawList(curve);
                }
            }
            equityBuildSw.Stop();
            batchModeSw.Stop();
            LogSystemInfo(
                "高速批量模式完成：标的数={Symbols} 候选仓位={Candidates} 平仓笔数={Closed} 总耗时={TotalElapsed}ms（数据准备={PrepareElapsed}ms, 开仓检测={OpenElapsed}ms, 平仓检测={CloseElapsed}ms, 权益构建={EquityElapsed}ms）",
                contexts.Count,
                totalCandidates,
                finalClosedPositions,
                batchModeSw.ElapsedMilliseconds,
                prepareSw.ElapsedMilliseconds,
                openPhaseSw.ElapsedMilliseconds,
                closePhaseSw.ElapsedMilliseconds,
                equityBuildSw.ElapsedMilliseconds);

            return new BacktestMainLoopResult
            {
                EquityCurvesBySymbol = equityCurvesBySymbol
            };
        }

        private void LogSystemInfo(string message, params object[] args)
        {
            _logger.LogInformation($"回测系统Log：{message}", args);
        }

        private void LogSystemWarning(string message, params object[] args)
        {
            _logger.LogWarning($"回测系统Log：{message}", args);
        }

        private int ResolveBatchCloseParallelism()
        {
            var configured = _configStore.GetInt("Backtest:BatchCloseParallelism", DefaultBatchCloseParallelism);
            var cpuCount = Math.Max(1, Environment.ProcessorCount);
            if (configured <= 0)
            {
                configured = cpuCount;
            }

            return Math.Max(1, Math.Min(configured, cpuCount));
        }

        private List<BatchOpenCandidate> CollectOpenCandidatesForSymbol(
            string exchange,
            string timeframe,
            BacktestOutputOptions output,
            BatchSymbolContext context,
            IReadOnlyList<long> drivingTimestamps,
            Action<int, int>? progressReporter,
            CancellationToken ct)
        {
            var runtime = context.Runtime;
            var indicatorProvider = new BatchIndicatorProvider(
                exchange,
                runtime.Symbol,
                timeframe,
                context.MainBars,
                context.MainIndex);
            var indicatorEngine = new IndicatorEngine(
                indicatorProvider,
                _loggerFactory.CreateLogger<IndicatorEngine>(),
                new OptionsWrapper<RuntimeQueueOptions>(_queueOptions));
            var valueResolver = new IndicatorValueResolver(
                indicatorProvider,
                indicatorEngine,
                _loggerFactory.CreateLogger<IndicatorValueResolver>());
            var evaluator = new ConditionEvaluator(new ConditionCacheService());
            var signalCollector = new BatchSignalCollectorExecutor(runtime.State);

            var logic = runtime.Strategy.StrategyConfig.Logic;
            if (logic?.Entry?.Long == null)
            {
                return new List<BatchOpenCandidate>();
            }

            var localProcessed = 0;
            var localSignals = 0;
            const int flushInterval = 256;

            foreach (var timestamp in drivingTimestamps)
            {
                ct.ThrowIfCancellationRequested();
                localProcessed++;

                if (!context.MainIndex.TryGetValue(timestamp, out _))
                {
                    TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
                    continue;
                }

                if (!TryGetPriceBar(context.PriceBars, context.PriceIndex, timestamp, out var priceBar))
                {
                    TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
                    continue;
                }

                signalCollector.SetCurrentBar(priceBar, timestamp);
                var marketTask = new MarketDataTask(exchange, runtime.Symbol, timeframe, timestamp, true);

                if (runtime.IndicatorRequests.Count > 0)
                {
                    var indicatorTask = _objectPoolManager.RentIndicatorTask(marketTask, runtime.IndicatorRequests);
                    try
                    {
                        indicatorEngine.ProcessTaskNow(indicatorTask);
                    }
                    finally
                    {
                        _objectPoolManager.ReturnIndicatorTask(indicatorTask);
                    }
                }

                var strategyContext = _objectPoolManager.RentStrategyExecutionContext(
                    runtime.Strategy,
                    marketTask,
                    valueResolver,
                    signalCollector);
                try
                {
                    var runtimeGate = ResolveRuntimeGate(runtime, strategyContext.CurrentTime, output.IncludeEvents);
                    if (!runtimeGate.AllowEntry)
                    {
                        TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
                        continue;
                    }

                    if (strategyContext.Strategy.State == StrategyState.PausedOpenPosition)
                    {
                        TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
                        continue;
                    }

                    if (!EvaluateEntryFilters(strategyContext, logic.Entry.Long, "Entry.Long", evaluator))
                    {
                        TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
                        continue;
                    }

                    var beforeCount = signalCollector.Candidates.Count;
                    ExecuteBranch(strategyContext, logic.Entry.Long, "Entry.Long", evaluator, LogicBranchKind.Entry, null);
                    var afterCount = signalCollector.Candidates.Count;
                    if (afterCount > beforeCount)
                    {
                        localSignals += afterCount - beforeCount;
                    }
                }
                finally
                {
                    _objectPoolManager.ReturnStrategyExecutionContext(strategyContext);
                }

                TryFlushOpenProgress(progressReporter, ref localProcessed, ref localSignals, flushInterval);
            }

            if (localProcessed > 0 || localSignals > 0)
            {
                progressReporter?.Invoke(localProcessed, localSignals);
            }

            return signalCollector.Candidates;
        }

        private static bool TryGetPriceBar(
            IReadOnlyList<OHLCV> priceBars,
            IReadOnlyDictionary<long, int> priceIndex,
            long timestamp,
            out OHLCV bar)
        {
            bar = default;
            if (!priceIndex.TryGetValue(timestamp, out var index))
            {
                return false;
            }

            if (index < 0 || index >= priceBars.Count)
            {
                return false;
            }

            bar = priceBars[index];
            return true;
        }

        private List<BatchClosedPosition> CloseCandidatesForSymbol(
            IReadOnlyList<BatchOpenCandidate> candidates,
            IReadOnlyList<OHLCV> priceBars,
            IReadOnlyDictionary<long, int> priceIndex,
            long timeframeMs,
            int parallelism,
            bool allowOverlappingPositions,
            Action<BatchCloseChunkProgress>? chunkProgressReporter,
            ParallelExecutionParticipationTracker? parallelTracker,
            CancellationToken ct)
        {
            if (candidates.Count == 0 || priceBars.Count == 0)
            {
                return new List<BatchClosedPosition>();
            }

            // 非重叠模式必须按开仓时间正序处理，否则会错误过滤大量历史仓位。
            // 重叠模式可按最近优先，便于更快出现“最近100条”预览。
            var orderedCandidates = allowOverlappingPositions
                ? candidates.OrderByDescending(item => item.EntryTime).ToList()
                : candidates.OrderBy(item => item.EntryTime).ToList();

            var accepted = new List<BatchClosedPosition>(orderedCandidates.Count);
            var chunkSize = ResolveBatchCloseProgressChunkSize();
            var closeParallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = ct
            };
            var lastExitTime = long.MinValue;
            for (var offset = 0; offset < orderedCandidates.Count; offset += chunkSize)
            {
                ct.ThrowIfCancellationRequested();

                var currentChunk = orderedCandidates
                    .Skip(offset)
                    .Take(chunkSize)
                    .ToArray();
                if (currentChunk.Length == 0)
                {
                    continue;
                }

                // 将候选按固定切片大小分配到并行任务，避免“每个仓位一个任务”带来的调度开销。
                var sliceSize = ResolveBatchCloseCandidateSliceSize();
                var partitions = Partitioner.Create(0, currentChunk.Length, sliceSize);
                var chunkClosedRaw = new List<BatchClosedPosition>(currentChunk.Length);
                var chunkClosedLock = new object();
                Parallel.ForEach(
                    partitions,
                    closeParallelOptions,
                    range =>
                    {
                        parallelTracker?.RecordCurrentThread();
                        var local = new List<BatchClosedPosition>(Math.Max(1, range.Item2 - range.Item1));
                        for (var index = range.Item1; index < range.Item2; index++)
                        {
                            var candidate = currentChunk[index];
                            var closed = SimulateBatchClose(candidate, priceBars, priceIndex, timeframeMs);
                            if (closed != null)
                            {
                                local.Add(closed);
                            }
                        }

                        if (local.Count == 0)
                        {
                            return;
                        }

                        lock (chunkClosedLock)
                        {
                            chunkClosedRaw.AddRange(local);
                        }
                    });

                var chunkClosed = chunkClosedRaw
                    .OrderBy(item => item.Candidate.EntryTime)
                    .ThenBy(item => item.Trade.ExitTime)
                    .ToList();
                var chunkAccepted = new List<BatchClosedPosition>(chunkClosed.Count);

                if (allowOverlappingPositions)
                {
                    chunkAccepted.AddRange(chunkClosed);
                }
                else
                {
                    foreach (var item in chunkClosed)
                    {
                        if (item.Candidate.EntryTime < lastExitTime)
                        {
                            continue;
                        }

                        chunkAccepted.Add(item);
                        lastExitTime = item.Trade.ExitTime;
                    }
                }

                if (chunkAccepted.Count > 0)
                {
                    accepted.AddRange(chunkAccepted);
                }

                chunkProgressReporter?.Invoke(new BatchCloseChunkProgress(currentChunk.Length, chunkAccepted));
            }

            accepted.Sort((left, right) =>
            {
                var compare = left.Trade.EntryTime.CompareTo(right.Trade.EntryTime);
                return compare != 0 ? compare : left.Trade.ExitTime.CompareTo(right.Trade.ExitTime);
            });
            return accepted;
        }

        private static BatchClosedPosition? SimulateBatchClose(
            BatchOpenCandidate candidate,
            IReadOnlyList<OHLCV> priceBars,
            IReadOnlyDictionary<long, int> priceIndex,
            long timeframeMs)
        {
            if (!priceIndex.TryGetValue(candidate.EntryTime, out var entryIndex))
            {
                return null;
            }

            if (entryIndex < 0 || entryIndex >= priceBars.Count)
            {
                return null;
            }

            var fundingRealizedDelta = 0m;
            var fundingAccumulatedDelta = 0m;
            var fundingRatio = timeframeMs > 0
                ? (decimal)timeframeMs / (8m * 60m * 60m * 1000m)
                : 0m;

            var exitReason = "End";
            var exitTime = candidate.EntryTime;
            var exitPrice = candidate.EntryPrice;
            var matched = false;

            for (var index = entryIndex + 1; index < priceBars.Count; index++)
            {
                var bar = priceBars[index];
                var timestamp = (long)(bar.timestamp ?? 0);
                if (timestamp <= 0)
                {
                    continue;
                }

                var close = Convert.ToDecimal(bar.close ?? bar.open ?? 0d);
                if (close <= 0)
                {
                    continue;
                }

                if (candidate.FundingRate != 0m && fundingRatio > 0m)
                {
                    var notional = close * candidate.Qty * candidate.ContractSize;
                    var funding = notional * candidate.FundingRate * fundingRatio;
                    if (candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase))
                    {
                        fundingRealizedDelta -= funding;
                        fundingAccumulatedDelta += funding;
                    }
                    else
                    {
                        fundingRealizedDelta += funding;
                        fundingAccumulatedDelta -= funding;
                    }
                }

                var high = Convert.ToDecimal(bar.high ?? bar.close ?? bar.open ?? 0d);
                var low = Convert.ToDecimal(bar.low ?? bar.close ?? bar.open ?? 0d);
                var takeProfitHit = CheckBatchTakeProfit(candidate, high, low);
                var stopLossHit = CheckBatchStopLoss(candidate, high, low);
                if (!takeProfitHit && !stopLossHit)
                {
                    continue;
                }

                matched = true;
                exitReason = takeProfitHit ? "TakeProfit" : "StopLoss";
                exitTime = timestamp;
                var closeSide = candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
                exitPrice = BacktestSlippageHelper.ApplySlippage(close, closeSide, candidate.SlippageBps);
                break;
            }

            if (!matched)
            {
                var lastBar = priceBars[^1];
                var close = Convert.ToDecimal(lastBar.close ?? lastBar.open ?? (double)candidate.EntryPrice);
                if (close <= 0)
                {
                    close = candidate.EntryPrice;
                }

                exitTime = (long)(lastBar.timestamp ?? candidate.EntryTime);
                if (exitTime <= 0)
                {
                    exitTime = candidate.EntryTime;
                }

                var closeSide = candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
                exitPrice = BacktestSlippageHelper.ApplySlippage(close, closeSide, candidate.SlippageBps);
            }

            var exitFee = CalculateBatchFee(exitPrice, candidate.Qty, candidate.ContractSize, candidate.FeeRate);
            var grossPnl = candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? (exitPrice - candidate.EntryPrice) * candidate.Qty * candidate.ContractSize
                : (candidate.EntryPrice - exitPrice) * candidate.Qty * candidate.ContractSize;
            var tradePnl = grossPnl - candidate.EntryFee - exitFee;

            var trade = new BacktestTrade
            {
                Symbol = candidate.Symbol,
                Side = candidate.Side,
                EntryTime = candidate.EntryTime,
                ExitTime = exitTime,
                EntryPrice = candidate.EntryPrice,
                ExitPrice = exitPrice,
                StopLossPrice = candidate.StopLossPrice,
                TakeProfitPrice = candidate.TakeProfitPrice,
                Qty = candidate.Qty,
                ContractSize = candidate.ContractSize,
                Fee = candidate.EntryFee + exitFee,
                PnL = tradePnl,
                ExitReason = exitReason,
                SlippageBps = candidate.SlippageBps
            };

            return new BatchClosedPosition(candidate, trade, fundingRealizedDelta, fundingAccumulatedDelta);
        }

        private static void ApplyClosedPositions(BacktestSymbolRuntime runtime, IReadOnlyList<BatchClosedPosition> closed)
        {
            runtime.State.Trades.Clear();
            runtime.State.Position = null;
            runtime.State.RealizedPnl = 0m;
            runtime.State.AccumulatedFunding = 0m;

            foreach (var item in closed)
            {
                runtime.State.Trades.Add(item.Trade);
                runtime.State.RealizedPnl += item.Trade.PnL + item.FundingRealizedDelta;
                runtime.State.AccumulatedFunding += item.FundingAccumulatedDelta;
                runtime.State.Events.Add(new BacktestEvent
                {
                    Timestamp = item.Candidate.EntryTime,
                    Type = "Open",
                    Message = $"开仓 {item.Candidate.Side} 价格={item.Candidate.EntryPrice} 数量={item.Candidate.Qty}"
                });
                runtime.State.Events.Add(new BacktestEvent
                {
                    Timestamp = item.Trade.ExitTime,
                    Type = "Close",
                    Message = $"平仓 {item.Candidate.Side} 价格={item.Trade.ExitPrice} 原因={item.Trade.ExitReason} 盈亏={item.Trade.PnL:F4}"
                });
            }
        }

        private static List<BacktestEquityPoint> BuildBatchEquityCurve(
            BatchSymbolContext context,
            IReadOnlyList<long> drivingTimestamps,
            decimal initialCapital,
            long equityCurveGranularityMs)
        {
            var byEntry = context.ClosedPositions
                .OrderBy(item => item.Candidate.EntryTime)
                .ThenBy(item => item.Trade.ExitTime)
                .ToList();
            var byExit = context.ClosedPositions
                .OrderBy(item => item.Trade.ExitTime)
                .ThenBy(item => item.Candidate.EntryTime)
                .ToList();

            var active = new List<BatchClosedPosition>();
            var openCursor = 0;
            var closeCursor = 0;
            var realized = 0m;
            var lastClose = 0m;
            var aggregator = new EquityCurveAggregator(equityCurveGranularityMs, drivingTimestamps.Count);

            foreach (var timestamp in drivingTimestamps)
            {
                while (openCursor < byEntry.Count && byEntry[openCursor].Candidate.EntryTime <= timestamp)
                {
                    active.Add(byEntry[openCursor]);
                    openCursor++;
                }

                while (closeCursor < byExit.Count && byExit[closeCursor].Trade.ExitTime <= timestamp)
                {
                    var item = byExit[closeCursor];
                    realized += item.Trade.PnL + item.FundingRealizedDelta;
                    active.Remove(item);
                    closeCursor++;
                }

                if (context.PriceIndex.TryGetValue(timestamp, out var barIndex) &&
                    barIndex >= 0 &&
                    barIndex < context.PriceBars.Count)
                {
                    var close = Convert.ToDecimal(context.PriceBars[barIndex].close ?? context.PriceBars[barIndex].open ?? 0d);
                    if (close > 0)
                    {
                        lastClose = close;
                    }
                }

                var unrealized = 0m;
                if (lastClose > 0)
                {
                    foreach (var item in active)
                    {
                        if (item.Trade.ExitTime <= timestamp)
                        {
                            continue;
                        }

                        unrealized += item.Candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                            ? (lastClose - item.Candidate.EntryPrice) * item.Candidate.Qty * item.Candidate.ContractSize
                            : (item.Candidate.EntryPrice - lastClose) * item.Candidate.Qty * item.Candidate.ContractSize;
                    }
                }

                aggregator.Add(new BacktestEquityPoint
                {
                    Timestamp = timestamp,
                    Equity = initialCapital + realized + unrealized,
                    RealizedPnl = realized,
                    UnrealizedPnl = unrealized
                });
            }

            return aggregator.Build();
        }

        private int ResolveBatchCloseProgressChunkSize()
        {
            var configured = _configStore.GetInt("Backtest:BatchCloseProgressChunkSize", DefaultBatchCloseProgressChunkSize);
            if (configured <= 0)
            {
                configured = DefaultBatchCloseProgressChunkSize;
            }

            return Math.Max(128, configured);
        }

        private int ResolveBatchCloseCandidateSliceSize()
        {
            var configured = _configStore.GetInt("Backtest:BatchCloseCandidateSliceSize", DefaultBatchCloseCandidateSliceSize);
            if (configured <= 0)
            {
                configured = DefaultBatchCloseCandidateSliceSize;
            }

            return Math.Max(10, configured);
        }

        private static void TryFlushOpenProgress(
            Action<int, int>? progressReporter,
            ref int localProcessed,
            ref int localSignals,
            int flushInterval)
        {
            if (progressReporter == null)
            {
                return;
            }

            if (localProcessed < flushInterval && localSignals <= 0)
            {
                return;
            }

            progressReporter(localProcessed, localSignals);
            localProcessed = 0;
            localSignals = 0;
        }

        private async Task ReportBatchOpenProgressAsync(
            BacktestProgressContext? progressContext,
            long totalChecks,
            Func<long> processedAccessor,
            Func<int> signalCountAccessor,
            System.Diagnostics.Stopwatch stopwatch,
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var completed = Math.Min(Math.Max(processedAccessor(), 0L), Math.Max(totalChecks, 0L));
                var signalCount = Math.Max(0, signalCountAccessor());
                var progress = totalChecks > 0 ? completed / (decimal)totalChecks : 1m;

                await _progressPushService
                    .PublishMessageAsync(
                        progressContext,
                        new BacktestProgressMessage
                        {
                            EventKind = "stage",
                            Stage = "batch_open_phase",
                            StageName = "第一阶段：检测开仓",
                            Message = $"第一阶段正在检测开仓：{completed}/{totalChecks}，已获取开仓 {signalCount} 个",
                            ProcessedBars = ToSafeInt(completed),
                            TotalBars = ToSafeInt(totalChecks),
                            FoundPositions = signalCount,
                            Progress = progress,
                            ElapsedMs = stopwatch.ElapsedMilliseconds,
                            Completed = false
                        },
                        ct)
                    .ConfigureAwait(false);

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        private async Task ReportBatchCloseProgressAsync(
            BacktestProgressContext? progressContext,
            int totalCandidates,
            Func<long> processedAccessor,
            Func<int> closedAccessor,
            Func<int> winAccessor,
            Func<int> lossAccessor,
            Func<IReadOnlyList<BacktestTrade>> previewAccessor,
            System.Diagnostics.Stopwatch stopwatch,
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var processed = Math.Min(Math.Max(processedAccessor(), 0L), Math.Max(totalCandidates, 0));
                var closed = Math.Max(0, closedAccessor());
                var wins = Math.Max(0, winAccessor());
                var losses = Math.Max(0, lossAccessor());
                var winRate = closed > 0 ? wins / (decimal)closed : 0m;
                var preview = previewAccessor();
                var previewList = preview ?? Array.Empty<BacktestTrade>();
                var previewCount = previewList.Count;

                await _progressPushService
                    .PublishMessageAsync(
                        progressContext,
                        new BacktestProgressMessage
                        {
                            EventKind = "positions",
                            Stage = "batch_close_phase",
                            StageName = "第二阶段：平仓检测",
                            Message = $"平仓检测中：已处理 {processed}/{totalCandidates}，当前平仓 {closed}，胜率 {(winRate * 100m):F2}%",
                            ProcessedBars = ToSafeInt(processed),
                            TotalBars = totalCandidates,
                            FoundPositions = closed,
                            TotalPositions = totalCandidates,
                            ChunkCount = previewCount,
                            WinCount = wins,
                            LossCount = losses,
                            WinRate = winRate,
                            Progress = totalCandidates > 0 ? processed / (decimal)totalCandidates : 1m,
                            ElapsedMs = stopwatch.ElapsedMilliseconds,
                            Positions = previewCount > 0 ? new List<BacktestTrade>(previewList) : null,
                            ReplacePositions = true,
                            Completed = false
                        },
                        ct)
                    .ConfigureAwait(false);

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        private static async Task AwaitBackgroundReportAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 进度协程结束由取消触发，属于预期行为。
            }
        }

        private static string BuildTopBatchSymbolSummary(
            IEnumerable<BatchSymbolContext> contexts,
            Func<BatchSymbolContext, int> selector,
            int limit)
        {
            if (contexts == null || selector == null || limit <= 0)
            {
                return "-";
            }

            var topItems = contexts
                .Select(item => new
                {
                    Symbol = item.Runtime.Symbol,
                    Count = Math.Max(0, selector(item))
                })
                .Where(item => item.Count > 0)
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(item => $"{item.Symbol}:{item.Count}")
                .ToList();

            return topItems.Count == 0 ? "-" : string.Join(", ", topItems);
        }

        private sealed class ParallelExecutionParticipationTracker
        {
            // 记录“线程 -> 首次观测核心”，用于输出并行执行的核心参与情况。
            private readonly ConcurrentDictionary<int, uint> _threadCoreMap = new();

            public void RecordCurrentThread()
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;
                if (_threadCoreMap.ContainsKey(threadId))
                {
                    return;
                }

                var coreId = GetCurrentProcessorNumber();
                _threadCoreMap.TryAdd(threadId, coreId);
            }

            public bool HasData => !_threadCoreMap.IsEmpty;

            public int ThreadCount => _threadCoreMap.Count;

            public int CoreCount => _threadCoreMap
                .Values
                .Distinct()
                .Count();

            public string CoreList => HasData
                ? string.Join(",", _threadCoreMap.Values.Distinct().OrderBy(item => item))
                : "-";

            public string ThreadCoreMap => HasData
                ? string.Join(
                    ", ",
                    _threadCoreMap
                        .OrderBy(item => item.Key)
                        .Select(item => $"线程{item.Key}->核心{item.Value}"))
                : "-";
        }

        private static void MergeRecentPreviewTrades(
            List<BacktestTrade> window,
            IReadOnlyList<BatchClosedPosition> positions,
            int limit)
        {
            if (window == null || positions == null || positions.Count == 0 || limit <= 0)
            {
                return;
            }

            var merged = new List<BacktestTrade>(window.Count + Math.Min(limit, positions.Count));
            merged.AddRange(window);
            merged.AddRange(
                positions
                    .OrderByDescending(item => item.Trade.ExitTime)
                    .ThenByDescending(item => item.Candidate.EntryTime)
                    .Take(limit)
                    .Select(item => item.Trade));

            var recent = merged
                .OrderByDescending(item => item.ExitTime)
                .ThenByDescending(item => item.EntryTime)
                .Take(limit)
                .ToList();

            window.Clear();
            window.AddRange(recent);
        }

        private static long ToMs(long ticks)
        {
            if (ticks <= 0)
            {
                return 0;
            }

            return (long)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        private static int ToSafeInt(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)value;
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

        private int ResolveInnerParallelism(int symbolCount)
        {
            if (symbolCount <= 1)
            {
                return 1;
            }

            var cpuCount = Math.Max(1, Environment.ProcessorCount);
            var configured = _configStore.GetInt("Backtest:InnerParallelism", DefaultInnerParallelism);
            if (configured <= 0)
            {
                configured = cpuCount;
            }

            configured = Math.Min(configured, cpuCount);
            return Math.Max(1, Math.Min(configured, symbolCount));
        }

        private enum LogicBranchKind
        {
            Entry,
            Exit
        }

        private readonly struct SymbolExecutionMetrics
        {
            public SymbolExecutionMetrics(
                long riskTicks,
                long contextTicks,
                long runtimeTicks,
                long logicTicks,
                long equityTicks,
                long entryTicks,
                long exitTicks,
                long entryFilterTicks,
                long exitCheckTicks,
                long entryCheckTicks,
                long exitActionTicks,
                long entryActionTicks)
            {
                RiskTicks = riskTicks;
                ContextTicks = contextTicks;
                RuntimeTicks = runtimeTicks;
                LogicTicks = logicTicks;
                EquityTicks = equityTicks;
                EntryTicks = entryTicks;
                ExitTicks = exitTicks;
                EntryFilterTicks = entryFilterTicks;
                ExitCheckTicks = exitCheckTicks;
                EntryCheckTicks = entryCheckTicks;
                ExitActionTicks = exitActionTicks;
                EntryActionTicks = entryActionTicks;
            }

            public long RiskTicks { get; }
            public long ContextTicks { get; }
            public long RuntimeTicks { get; }
            public long LogicTicks { get; }
            public long EquityTicks { get; }
            public long EntryTicks { get; }
            public long ExitTicks { get; }
            public long EntryFilterTicks { get; }
            public long ExitCheckTicks { get; }
            public long EntryCheckTicks { get; }
            public long ExitActionTicks { get; }
            public long EntryActionTicks { get; }
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

        private sealed class BatchSymbolContext
        {
            public BatchSymbolContext(
                BacktestSymbolRuntime runtime,
                IReadOnlyList<OHLCV> mainBars,
                IReadOnlyDictionary<long, int> mainIndex,
                IReadOnlyList<OHLCV> priceBars,
                IReadOnlyDictionary<long, int> priceIndex)
            {
                Runtime = runtime;
                MainBars = mainBars;
                MainIndex = mainIndex;
                PriceBars = priceBars;
                PriceIndex = priceIndex;
            }

            public BacktestSymbolRuntime Runtime { get; }
            public IReadOnlyList<OHLCV> MainBars { get; }
            public IReadOnlyDictionary<long, int> MainIndex { get; }
            public IReadOnlyList<OHLCV> PriceBars { get; }
            public IReadOnlyDictionary<long, int> PriceIndex { get; }
            public List<BatchOpenCandidate> Candidates { get; set; } = new();
            public List<BatchClosedPosition> ClosedPositions { get; set; } = new();
        }

        private sealed class BatchOpenCandidate
        {
            public string Symbol { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public long EntryTime { get; set; }
            public decimal EntryPrice { get; set; }
            public decimal Qty { get; set; }
            public decimal ContractSize { get; set; }
            public decimal EntryFee { get; set; }
            public decimal? StopLossPrice { get; set; }
            public decimal? TakeProfitPrice { get; set; }
            public decimal FeeRate { get; set; }
            public decimal FundingRate { get; set; }
            public int SlippageBps { get; set; }
        }

        private sealed class BatchClosedPosition
        {
            public BatchClosedPosition(
                BatchOpenCandidate candidate,
                BacktestTrade trade,
                decimal fundingRealizedDelta,
                decimal fundingAccumulatedDelta)
            {
                Candidate = candidate;
                Trade = trade;
                FundingRealizedDelta = fundingRealizedDelta;
                FundingAccumulatedDelta = fundingAccumulatedDelta;
            }

            public BatchOpenCandidate Candidate { get; }
            public BacktestTrade Trade { get; }
            public decimal FundingRealizedDelta { get; }
            public decimal FundingAccumulatedDelta { get; }
        }

        private sealed class BatchCloseChunkProgress
        {
            public BatchCloseChunkProgress(int processedCandidates, IReadOnlyList<BatchClosedPosition> acceptedPositions)
            {
                ProcessedCandidates = processedCandidates;
                AcceptedPositions = acceptedPositions ?? Array.Empty<BatchClosedPosition>();
            }

            public int ProcessedCandidates { get; }
            public IReadOnlyList<BatchClosedPosition> AcceptedPositions { get; }
        }

        private sealed class BatchSignalCollectorExecutor : IStrategyActionExecutor
        {
            private readonly BacktestActionExecutor.SymbolState _state;
            private OHLCV _currentBar;
            private long _currentTimestamp;

            public BatchSignalCollectorExecutor(BacktestActionExecutor.SymbolState state)
            {
                _state = state ?? throw new ArgumentNullException(nameof(state));
            }

            public List<BatchOpenCandidate> Candidates { get; } = new();

            public void SetCurrentBar(OHLCV bar, long timestamp)
            {
                _currentBar = bar;
                _currentTimestamp = timestamp;
            }

            public (bool Success, StringBuilder Message) Execute(
                StrategyExecutionContext context,
                StrategyMethod method,
                IReadOnlyList<ConditionEvaluationResult> triggerResults)
            {
                var action = method?.Param != null && method.Param.Length > 0 ? method.Param[0] : string.Empty;
                if (!TryMapBatchAction(action, out var positionSide, out var orderSide, out var isClose))
                {
                    return BuildResult(method?.Method ?? "Unknown", false, "不支持的动作类型");
                }

                // 批量阶段仅记录开仓信号。
                if (isClose)
                {
                    return BuildResult(method?.Method ?? "Unknown", true, "批量模式忽略平仓动作");
                }

                var close = Convert.ToDecimal(_currentBar.close ?? _currentBar.open ?? 0d);
                if (close <= 0)
                {
                    return BuildResult(method?.Method ?? "Unknown", false, "K线收盘价无效");
                }

                var qty = NormalizeBatchOrderQty(_state.OrderQty, _state.Contract);
                if (qty <= 0)
                {
                    return BuildResult(method?.Method ?? "Unknown", false, "下单数量无效");
                }

                var entryPrice = BacktestSlippageHelper.ApplySlippage(close, orderSide, _state.SlippageBps);
                var entryFee = CalculateBatchFee(entryPrice, qty, _state.ContractSize, _state.FeeRate);

                Candidates.Add(new BatchOpenCandidate
                {
                    Symbol = _state.Symbol,
                    Side = positionSide,
                    EntryTime = _currentTimestamp,
                    EntryPrice = entryPrice,
                    Qty = qty,
                    ContractSize = _state.ContractSize,
                    EntryFee = entryFee,
                    StopLossPrice = BuildBatchStopLossPrice(entryPrice, _state.StopLossPct, _state.Leverage, positionSide),
                    TakeProfitPrice = BuildBatchTakeProfitPrice(entryPrice, _state.TakeProfitPct, _state.Leverage, positionSide),
                    FeeRate = _state.FeeRate,
                    FundingRate = _state.FundingRate,
                    SlippageBps = _state.SlippageBps
                });

                return BuildResult(method?.Method ?? "Unknown", true, "已记录开仓信号");
            }

            private static (bool Success, StringBuilder Message) BuildResult(string method, bool success, string message)
            {
                var builder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(method))
                {
                    builder.Append(method).Append(": ");
                }

                builder.Append(message);
                return (success, builder);
            }
        }

        private sealed class BatchIndicatorProvider : IMarketDataProvider
        {
            private readonly string _exchange;
            private readonly string _symbol;
            private readonly string _timeframe;
            private readonly IReadOnlyList<OHLCV> _bars;
            private readonly IReadOnlyDictionary<long, int> _index;

            public BatchIndicatorProvider(
                string exchange,
                string symbol,
                string timeframe,
                IReadOnlyList<OHLCV> bars,
                IReadOnlyDictionary<long, int> index)
            {
                _exchange = MarketDataKeyNormalizer.NormalizeExchange(exchange);
                _symbol = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
                _timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
                _bars = bars;
                _index = index;
            }

            public List<OHLCV> GetHistoryKlines(
                string exchangeId,
                string timeframe,
                string symbol,
                long? endTimestamp,
                int count)
            {
                if (count <= 0 || _bars.Count == 0)
                {
                    return new List<OHLCV>();
                }

                if (!IsSameExchange(exchangeId))
                {
                    return new List<OHLCV>();
                }

                if (!IsSameTimeframe(timeframe))
                {
                    return new List<OHLCV>();
                }

                if (!IsSameSymbol(symbol))
                {
                    return new List<OHLCV>();
                }

                var endIndex = _bars.Count - 1;
                if (endTimestamp.HasValue)
                {
                    if (!_index.TryGetValue(endTimestamp.Value, out endIndex))
                    {
                        return new List<OHLCV>();
                    }
                }

                var startIndex = Math.Max(0, endIndex - count + 1);
                var result = new List<OHLCV>(endIndex - startIndex + 1);
                for (var i = startIndex; i <= endIndex; i++)
                {
                    result.Add(_bars[i]);
                }

                return result;
            }

            private bool IsSameExchange(string exchangeId)
            {
                return IsSameValue(exchangeId, _exchange, MarketDataKeyNormalizer.NormalizeExchange);
            }

            private bool IsSameTimeframe(string timeframe)
            {
                return IsSameValue(timeframe, _timeframe, MarketDataKeyNormalizer.NormalizeTimeframe);
            }

            private bool IsSameSymbol(string symbol)
            {
                return IsSameValue(symbol, _symbol, MarketDataKeyNormalizer.NormalizeSymbol);
            }

            private static bool IsSameValue(string input, string normalizedValue, Func<string, string> normalize)
            {
                if (string.Equals(input, normalizedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 热路径先做直接比较，仅在失配时再做归一化回退兼容。
                return string.Equals(normalize(input), normalizedValue, StringComparison.Ordinal);
            }
        }

        private static bool TryResolveBars(
            BacktestMarketDataProvider provider,
            IReadOnlyList<string> symbols,
            IReadOnlyList<string> mainSeriesKeys,
            IReadOnlyList<string> priceSeriesKeys,
            bool needOneMinutePrice,
            long timestamp,
            Dictionary<string, OHLCV> mainBars,
            Dictionary<string, OHLCV> priceBars)
        {
            mainBars.Clear();
            priceBars.Clear();

            if (symbols.Count != mainSeriesKeys.Count || symbols.Count != priceSeriesKeys.Count)
            {
                return false;
            }

            for (var i = 0; i < symbols.Count; i++)
            {
                var symbol = symbols[i];
                if (!provider.TryGetBarBySeriesKey(mainSeriesKeys[i], timestamp, out var bar))
                {
                    mainBars.Clear();
                    priceBars.Clear();
                    return false;
                }

                mainBars[symbol] = bar;

                var priceBar = bar;
                if (needOneMinutePrice)
                {
                    if (!provider.TryGetBarBySeriesKey(priceSeriesKeys[i], timestamp, out priceBar))
                    {
                        mainBars.Clear();
                        priceBars.Clear();
                        return false;
                    }
                }

                priceBars[symbol] = priceBar;
            }

            return true;
        }

        private static string[] BuildSeriesKeys(string normalizedExchange, string normalizedTimeframe, IReadOnlyList<string> symbols)
        {
            var keys = new string[symbols.Count];
            for (var i = 0; i < symbols.Count; i++)
            {
                var symbol = MarketDataKeyNormalizer.NormalizeSymbol(symbols[i]);
                keys[i] = BacktestMarketDataProvider.BuildKeyFromNormalized(
                    normalizedExchange,
                    symbol,
                    normalizedTimeframe);
            }

            return keys;
        }

        private static BacktestEquityPoint BuildEquityPoint(
            BacktestSymbolRuntime runtime,
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

        private static decimal CalculateBatchFee(decimal price, decimal qty, decimal contractSize, decimal feeRate)
        {
            if (price <= 0 || qty <= 0 || contractSize <= 0 || feeRate <= 0)
            {
                return 0m;
            }

            return price * qty * contractSize * feeRate;
        }

        private static decimal NormalizeBatchOrderQty(decimal qty, ContractDetails? contract)
        {
            if (qty <= 0)
            {
                return 0m;
            }

            if (contract?.AmountPrecision != null)
            {
                var digits = Math.Max(0, contract.AmountPrecision.Value);
                var factor = (decimal)Math.Pow(10, digits);
                qty = Math.Floor(qty * factor) / factor;
            }

            if (contract?.MinOrderAmount != null && qty < contract.MinOrderAmount.Value)
            {
                return 0m;
            }

            if (contract?.MaxOrderAmount != null && qty > contract.MaxOrderAmount.Value)
            {
                qty = contract.MaxOrderAmount.Value;
            }

            return qty;
        }

        private static decimal? BuildBatchStopLossPrice(decimal entryPrice, decimal? stopLossPct, int leverage, string side)
        {
            if (!stopLossPct.HasValue || stopLossPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            var effectiveLeverage = Math.Max(1, leverage);
            var movePct = stopLossPct.Value / effectiveLeverage;
            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 - movePct)
                : entryPrice * (1 + movePct);
        }

        private static decimal? BuildBatchTakeProfitPrice(decimal entryPrice, decimal? takeProfitPct, int leverage, string side)
        {
            if (!takeProfitPct.HasValue || takeProfitPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            var effectiveLeverage = Math.Max(1, leverage);
            var movePct = takeProfitPct.Value / effectiveLeverage;
            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + movePct)
                : entryPrice * (1 - movePct);
        }

        private static bool CheckBatchStopLoss(BatchOpenCandidate candidate, decimal high, decimal low)
        {
            if (!candidate.StopLossPrice.HasValue)
            {
                return false;
            }

            return candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? low <= candidate.StopLossPrice.Value
                : high >= candidate.StopLossPrice.Value;
        }

        private static bool CheckBatchTakeProfit(BatchOpenCandidate candidate, decimal high, decimal low)
        {
            if (!candidate.TakeProfitPrice.HasValue)
            {
                return false;
            }

            return candidate.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? high >= candidate.TakeProfitPrice.Value
                : low <= candidate.TakeProfitPrice.Value;
        }

        private static bool TryMapBatchAction(
            string action,
            out string positionSide,
            out string orderSide,
            out bool isClose)
        {
            positionSide = string.Empty;
            orderSide = string.Empty;
            isClose = false;

            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            switch (action.Trim().ToUpperInvariant())
            {
                case "LONG":
                    positionSide = "Long";
                    orderSide = "buy";
                    return true;
                case "SHORT":
                    positionSide = "Short";
                    orderSide = "sell";
                    return true;
                case "CLOSELONG":
                    positionSide = "Long";
                    orderSide = "sell";
                    isClose = true;
                    return true;
                case "CLOSESHORT":
                    positionSide = "Short";
                    orderSide = "buy";
                    isClose = true;
                    return true;
                default:
                    return false;
            }
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

        private StrategyRuntimeGate ResolveRuntimeGate(BacktestSymbolRuntime runtime, DateTimeOffset currentTime, bool recordEvent)
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
            var results = _objectPoolManager.RentConditionResultList();
            try
            {
                var stageLabel = $"{stage}.Filter";
                return EvaluateChecks(context, filters, results, stageLabel, evaluator);
            }
            finally
            {
                _objectPoolManager.ReturnConditionResultList(results);
            }
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

            var containers = (IReadOnlyList<ConditionContainer>)(branch.Containers ?? EmptyConditionContainers);
            var passCount = 0;
            var aggregatedResults = _objectPoolManager.RentConditionResultList();

            try
            {
                PrecomputeRequiredConditions(context, containers, evaluator);

                for (var i = 0; i < containers.Count; i++)
                {
                    var container = containers[i];
                    if (container == null)
                    {
                        continue;
                    }

                    var checkResults = _objectPoolManager.RentConditionResultList();
                    try
                    {
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
                    finally
                    {
                        _objectPoolManager.ReturnConditionResultList(checkResults);
                    }
                }

                if (passCount < branch.MinPassConditionContainer)
                {
                    return;
                }

                var actionStart = System.Diagnostics.Stopwatch.GetTimestamp();
                ExecuteActions(context, branch.OnPass, aggregatedResults, stage);
                timing?.AddAction(branchKind, System.Diagnostics.Stopwatch.GetTimestamp() - actionStart);
            }
            finally
            {
                _objectPoolManager.ReturnConditionResultList(aggregatedResults);
            }
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
                if (container?.Checks?.Groups == null)
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

            var groups = (IReadOnlyList<ConditionGroup>)(checks.Groups ?? EmptyConditionGroups);
            var passGroups = 0;

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null || !group.Enabled)
                {
                    continue;
                }

                var crossRequired = _objectPoolManager.RentStrategyMethodList();
                var crossOptional = _objectPoolManager.RentStrategyMethodList();
                var required = _objectPoolManager.RentStrategyMethodList();
                var optional = _objectPoolManager.RentStrategyMethodList();

                try
                {
                    ConditionPriorityHelper.SplitByPriority(
                        group.Conditions ?? EmptyStrategyMethods,
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

                    if (optionalPassCount >= group.MinPassConditions)
                    {
                        passGroups++;
                    }
                }
                finally
                {
                    _objectPoolManager.ReturnStrategyMethodList(crossRequired);
                    _objectPoolManager.ReturnStrategyMethodList(crossOptional);
                    _objectPoolManager.ReturnStrategyMethodList(required);
                    _objectPoolManager.ReturnStrategyMethodList(optional);
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

            foreach (var action in actions.Conditions ?? EmptyStrategyMethods)
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

    internal sealed class BacktestMainLoopResult
    {
        public Dictionary<string, List<BacktestEquityPoint>>? EquityCurvesBySymbol { get; set; }
    }
}
