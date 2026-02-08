using System;
using System.Collections.Generic;
using System.Linq;
using ccxt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using ServerTest.Infrastructure.Config;
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
        private readonly HistoricalMarketDataRepository _repository;
        private readonly HistoricalMarketDataCache _historicalCache;
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly ContractDetailsCacheService _contractCache;
        private readonly IStrategyRuntimeTemplateProvider _templateProvider;
        private readonly HistoricalMarketDataOptions _historyOptions;
        private readonly BacktestMainLoop _mainLoop;
        private readonly BacktestObjectPoolManager _objectPoolManager;
        private readonly ServerConfigStore _configStore;
        private readonly BacktestProgressPushService _progressPushService;
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
                return "逻辑配置=空";
            }

            var entry = BuildBranchSummary(logic.Entry?.Long);
            var exit = BuildBranchSummary(logic.Exit?.Long);
            return $"开仓={entry} 平仓={exit}";
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
            return $"容器数={containerCount} 分组数={groupCount} 条件数={conditionCount} 动作数={actionCount} 已启用={branch.Enabled}";
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

        public BacktestRunner(
            HistoricalMarketDataRepository repository,
            HistoricalMarketDataCache historicalCache,
            IMarketDataProvider marketDataProvider,
            ContractDetailsCacheService contractCache,
            IStrategyRuntimeTemplateProvider templateProvider,
            IOptions<HistoricalMarketDataOptions> historyOptions,
            BacktestMainLoop mainLoop,
            BacktestObjectPoolManager objectPoolManager,
            ServerConfigStore configStore,
            BacktestProgressPushService progressPushService,
            ILogger<BacktestRunner> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _historicalCache = historicalCache ?? throw new ArgumentNullException(nameof(historicalCache));
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _contractCache = contractCache ?? throw new ArgumentNullException(nameof(contractCache));
            _templateProvider = templateProvider;
            _historyOptions = historyOptions?.Value ?? new HistoricalMarketDataOptions();
            _mainLoop = mainLoop ?? throw new ArgumentNullException(nameof(mainLoop));
            _objectPoolManager = objectPoolManager ?? throw new ArgumentNullException(nameof(objectPoolManager));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
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

            var parseSw = System.Diagnostics.Stopwatch.StartNew();
            // 委托给 BacktestParameterParser 解析参数
            var maxQueryBars = _historyOptions.MaxQueryBars > 0 ? _historyOptions.MaxQueryBars : 1000;
            var ctx = BacktestParameterParser.Parse(request, config, maxQueryBars);
            var exchange = ctx.Exchange;
            var timeframe = ctx.Timeframe;
            var timeframeMs = ctx.TimeframeMs;
            var symbols = ctx.Symbols;
            var startTime = ctx.StartTime;
            var endTime = ctx.EndTime;
            var useRange = ctx.UseRange;
            var barCount = ctx.BarCount;
            var output = ctx.Output;
            var equityCurveGranularity = ctx.EquityCurveGranularity;
            var equityCurveGranularityMs = ctx.EquityCurveGranularityMs;
            var runtimeConfig = ctx.RuntimeConfig;
            parseSw.Stop();
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
                "回测开始：交易所={Exchange} 周期={Timeframe} 标的={Symbols} 回测范围={Range} K线数量={Bars}",
                exchange,
                timeframe,
                string.Join(",", symbols),
                useRange ? $"{startTime:yyyy-MM-dd HH:mm:ss}~{endTime:yyyy-MM-dd HH:mm:ss}" : $"最近{barCount}根K线",
                barCount);
            LogSystemInfo(
                "参数解析完成：范围模式={UseRange} 输出交易明细={IncludeTrades} 输出资金曲线={IncludeEquity} 输出事件={IncludeEvents} 启用运行时间门禁={UseRuntimeGate} 资金曲线粒度={Granularity} 参数阶段耗时={Elapsed}ms 累计耗时={TotalElapsed}ms",
                useRange,
                output.IncludeTrades,
                output.IncludeEquityCurve,
                output.IncludeEvents,
                request.UseStrategyRuntime,
                equityCurveGranularity,
                parseSw.ElapsedMilliseconds,
                stopwatch.ElapsedMilliseconds);
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
                "策略实例构建完成：标的数={Symbols} 指标请求数={IndicatorRequests} 阶段耗时={Elapsed}ms 平均每标的耗时={AvgElapsed}ms",
                symbolRuntimes.Count,
                totalIndicatorRequests,
                buildRuntimeSw.ElapsedMilliseconds,
                symbolRuntimes.Count > 0 ? buildRuntimeSw.ElapsedMilliseconds / (decimal)symbolRuntimes.Count : 0m);
            foreach (var runtime in symbolRuntimes.Values)
            {
                LogSystemInfo(
                    "运行时间配置：标的={Symbol} 调度类型={Type} 门禁策略={Policy} 配置摘要={Summary}",
                    runtime.Symbol,
                    runtime.RuntimeSchedule.ScheduleType,
                    runtime.RuntimeSchedule.Policy,
                    runtime.RuntimeSchedule.Summary);
                LogSystemInfo(
                    "策略结构：标的={Symbol} {Summary}",
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

            // 使用 BacktestDataLoader 加载 K 线数据
            var dataLoader = new BacktestDataLoader(
                _repository, _historicalCache, _marketDataProvider,
                _historyOptions.MaxQueryBars, _logger);

            var series = new Dictionary<string, List<OHLCV>>();
            var drivingTimestampsBySymbol = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
            var loadPrimaryStageSw = System.Diagnostics.Stopwatch.StartNew();
            var loadedPrimaryBars = 0L;
            var loadedDrivingBars = 0L;

            foreach (var runtime in symbolRuntimes.Values)
            {
                var warmupBars = warmupByTimeframe.TryGetValue(timeframe, out var warmup) ? warmup : 0;
                var loadSw = System.Diagnostics.Stopwatch.StartNew();
                var bars = await dataLoader.LoadPrimaryBarsAsync(
                    exchange, runtime.Symbol, timeframe,
                    startTime, endTime, barCount, warmupBars, ct);
                loadSw.Stop();
                loadedPrimaryBars += bars.Count;
                LogSystemInfo(
                    "加载主周期K线：标的={Symbol} 周期={Timeframe} 原始K线={Bars} 预热K线={Warmup} 阶段耗时={Elapsed}ms 累计已加载K线={LoadedBars}",
                    runtime.Symbol, timeframe, bars.Count, warmupBars, loadSw.ElapsedMilliseconds, loadedPrimaryBars);

                if (bars.Count == 0)
                    LogSystemWarning("回测标的无主周期K线: {Symbol}", runtime.Symbol);

                series[BacktestMarketDataProvider.BuildKey(exchange, runtime.Symbol, timeframe)] = bars;
                var drivingBars = BacktestDataLoader.SelectDrivingBars(bars, startTime, endTime, barCount);
                loadedDrivingBars += drivingBars.Count;
                var symbolDrivingTimestamps = new List<long>(drivingBars.Count);
                foreach (var bar in drivingBars)
                {
                    var timestamp = (long)(bar.timestamp ?? 0);
                    if (timestamp > 0)
                    {
                        symbolDrivingTimestamps.Add(timestamp);
                    }
                }

                drivingTimestampsBySymbol[runtime.Symbol] = symbolDrivingTimestamps;
            }
            loadPrimaryStageSw.Stop();
            LogSystemInfo(
                "主周期K线阶段完成：标的数={Symbols} 原始K线总数={PrimaryBars} 驱动K线总数={DrivingBars} 阶段耗时={Elapsed}ms 平均每标的耗时={AvgElapsed}ms",
                symbolRuntimes.Count,
                loadedPrimaryBars,
                loadedDrivingBars,
                loadPrimaryStageSw.ElapsedMilliseconds,
                symbolRuntimes.Count > 0 ? loadPrimaryStageSw.ElapsedMilliseconds / (decimal)symbolRuntimes.Count : 0m);
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
            var drivingTimestamps = BacktestDataLoader.BuildIntersection(drivingTimestampsBySymbol.Values, _objectPoolManager);
            intersectionSw.Stop();
            var intersectionStart = drivingTimestamps.Count > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(drivingTimestamps[0]).ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
            var intersectionEnd = drivingTimestamps.Count > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(drivingTimestamps[^1]).ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
            LogSystemInfo(
                "交集时间轴构建完成：交集K线数={Bars} 标的数={Symbols} 起始时间={Start} 结束时间={End} 阶段耗时={Elapsed}ms",
                drivingTimestamps.Count,
                symbols.Count,
                intersectionStart,
                intersectionEnd,
                intersectionSw.ElapsedMilliseconds);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "build_intersection",
                    "构建交集时间轴",
                    $"交集时间轴构建完成，交集K线数={drivingTimestamps.Count}",
                    drivingTimestamps.Count,
                    drivingTimestamps.Count,
                    intersectionSw.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);
            if (drivingTimestamps.Count == 0)
            {
                LogSystemWarning(
                    "交集时间轴为空，返回空结果：参数={ParseElapsed}ms 构建策略={BuildElapsed}ms 主周期加载={LoadPrimaryElapsed}ms 交集构建={IntersectionElapsed}ms 累计={TotalElapsed}ms",
                    parseSw.ElapsedMilliseconds,
                    buildRuntimeSw.ElapsedMilliseconds,
                    loadPrimaryStageSw.ElapsedMilliseconds,
                    intersectionSw.ElapsedMilliseconds,
                    stopwatch.ElapsedMilliseconds);
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

            var supplementarySeriesBefore = series.Count;
            var supplementaryBarsBefore = series.Values.Sum(item => item.Count);
            var supplementSw = System.Diagnostics.Stopwatch.StartNew();
            await dataLoader.LoadSupplementaryTimeframesAsync(
                exchange, requiredTimeframes, symbols,
                drivingStart, drivingEnd, warmupByTimeframe, series, ct);
            supplementSw.Stop();
            var supplementarySeriesAfter = series.Count;
            var supplementaryBarsAfter = series.Values.Sum(item => item.Count);
            LogSystemInfo(
                "补充周期加载完成：补充周期={Timeframes} 新增序列={AddedSeries} 总序列={SeriesCount} 新增K线={AddedBars} 总K线={TotalBars} 阶段耗时={Elapsed}ms",
                requiredTimeframes.Count == 0 ? "-" : string.Join(",", requiredTimeframes),
                supplementarySeriesAfter - supplementarySeriesBefore,
                supplementarySeriesAfter,
                supplementaryBarsAfter - supplementaryBarsBefore,
                supplementaryBarsAfter,
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
            var configuredExecutionMode = _configStore.GetString(
                "Backtest:ExecutionModeDefault",
                BacktestExecutionModes.BatchOpenClose);
            var executionMode = ResolveExecutionMode(request.ExecutionMode, configuredExecutionMode);
            LogSystemInfo(
                "执行模式确定：请求模式={RequestMode} 默认模式={ConfiguredMode} 最终模式={ExecutionMode}",
                string.IsNullOrWhiteSpace(request.ExecutionMode) ? "-" : request.ExecutionMode,
                configuredExecutionMode,
                executionMode);
            await _progressPushService
                .PublishStageAsync(
                    progressContext,
                    "execution_mode",
                    "执行模式",
                    $"当前执行模式：{executionMode}",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    true,
                    ct)
                .ConfigureAwait(false);

            var mainLoopSw = System.Diagnostics.Stopwatch.StartNew();
            var mainLoopResult = executionMode == BacktestExecutionModes.BatchOpenClose
                ? await _mainLoop
                    .ExecuteBatchOpenCloseAsync(
                        exchange,
                        timeframe,
                        timeframeMs,
                        symbols,
                        drivingTimestamps,
                        symbolRuntimes,
                        provider,
                        output,
                        request.InitialCapital,
                        equityCurveGranularityMs,
                        progressContext,
                        ct)
                    .ConfigureAwait(false)
                : await _mainLoop
                    .ExecuteAsync(
                        exchange,
                        timeframe,
                        timeframeMs,
                        symbols,
                        drivingTimestamps,
                        symbolRuntimes,
                        provider,
                        output,
                        request.InitialCapital,
                        equityCurveGranularityMs,
                        progressContext,
                        ct)
                    .ConfigureAwait(false);
            mainLoopSw.Stop();
            var equityCurvesBySymbol = mainLoopResult.EquityCurvesBySymbol;
            var totalTradesAfterMainLoop = symbolRuntimes.Values.Sum(r => r.State.Trades.Count);
            LogSystemInfo(
                "核心执行阶段完成：执行模式={ExecutionMode} 交易笔数={Trades} 阶段耗时={Elapsed}ms 累计耗时={TotalElapsed}ms",
                executionMode,
                totalTradesAfterMainLoop,
                mainLoopSw.ElapsedMilliseconds,
                stopwatch.ElapsedMilliseconds);

            var finalizeSw = System.Diagnostics.Stopwatch.StartNew();
            var totalPositions = symbolRuntimes.Values.Sum(r => r.State.Trades.Count);
            var foundPositions = 0;
            var collectPositionsSw = System.Diagnostics.Stopwatch.StartNew();
            var nextPositionProgressTick = System.Diagnostics.Stopwatch.GetTimestamp() + BacktestMainLoop.PositionProgressIntervalTicks;
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
                runtime.Result.TradeSummary = BacktestStatisticsBuilder.BuildTradeSummary(symbolTrades);
                if (output.IncludeTrades)
                {
                    runtime.Result.TradesRaw = new List<string>(symbolTrades.Count);
                }

                var tradeChunk = new List<BacktestTrade>(128);
                for (var tradeIndex = 0; tradeIndex < symbolTrades.Count; tradeIndex++)
                {
                    var closedTrade = symbolTrades[tradeIndex];
                    foundPositions++;

                    if (output.IncludeTrades)
                    {
                        runtime.Result.TradesRaw.Add(JsonConvert.SerializeObject(closedTrade));
                        tradeChunk.Add(closedTrade);
                    }

                    // 仓位汇总阶段按 0.1 秒推送进度，前端可先展示部分数据。
                    if (ShouldPublishProgress(ref nextPositionProgressTick, BacktestMainLoop.PositionProgressIntervalTicks))
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
                    runtime.Result.EventsRaw = BacktestStatisticsBuilder.SerializeToRawList(runtime.State.Events);
                }
                runtime.Result.EventSummary = BacktestStatisticsBuilder.BuildEventSummary(runtime.State.Events);

                runtime.Result.Stats = BacktestStatisticsBuilder.BuildStats(
                    symbolTrades,
                    output.IncludeEquityCurve && equityCurvesBySymbol != null && equityCurvesBySymbol.TryGetValue(runtime.Symbol, out var symbolCurve)
                        ? symbolCurve
                        : null,
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
            collectPositionsSw.Stop();

            var totalInitial = request.InitialCapital * symbolRuntimes.Count;
            var allTrades = symbolRuntimes.Values.SelectMany(r => r.State.Trades).ToList();
            var totalEquity = output.IncludeEquityCurve && equityCurvesBySymbol != null
                ? BacktestStatisticsBuilder.BuildTotalEquityCurve(equityCurvesBySymbol.Values.ToList())
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
                TotalStats = BacktestStatisticsBuilder.BuildStats(allTrades, totalEquity, totalInitial),
                Symbols = symbolRuntimes.Values.Select(r => r.Result).ToList()
            };

            LogSystemInfo(
                "收尾统计完成：交易笔数={Trades} 标的数={Symbols} 仓位汇总耗时={CollectElapsed}ms 收尾阶段耗时={Elapsed}ms",
                allTrades.Count,
                symbolRuntimes.Count,
                collectPositionsSw.ElapsedMilliseconds,
                finalizeSw.ElapsedMilliseconds);
            LogSystemInfo(
                "回测完成：交易所={Exchange} 周期={Timeframe} 交集K线={Bars} 交易笔数={Trades} 总耗时={Elapsed}ms（参数解析={ParseElapsed}ms, 策略构建={BuildElapsed}ms, 主周期加载={LoadPrimaryElapsed}ms, 交集构建={IntersectionElapsed}ms, 补充周期={SupplementElapsed}ms, 核心执行={MainLoopElapsed}ms, 收尾阶段={FinalizeElapsed}ms）",
                exchange,
                timeframe,
                result.TotalBars,
                allTrades.Count,
                result.DurationMs,
                parseSw.ElapsedMilliseconds,
                buildRuntimeSw.ElapsedMilliseconds,
                loadPrimaryStageSw.ElapsedMilliseconds,
                intersectionSw.ElapsedMilliseconds,
                supplementSw.ElapsedMilliseconds,
                mainLoopSw.ElapsedMilliseconds,
                finalizeSw.ElapsedMilliseconds);
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
        private Dictionary<string, BacktestSymbolRuntime> BuildSymbolRuntimes(
            BacktestRunRequest request,
            StrategyConfig templateConfig,
            string exchange,
            string timeframe,
            long timeframeMs,
            StrategyRuntimeConfig? runtimeConfig,
            IReadOnlyList<string> symbols)
        {
            var runtimes = new Dictionary<string, BacktestSymbolRuntime>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols)
            {
                var strategy = BuildStrategyForSymbol(request, templateConfig, exchange, timeframe, timeframeMs, symbol);
                var indicatorRequests = BuildIndicatorRequests(strategy);
                var schedule = new StrategyRuntimeSchedule(runtimeConfig, _templateProvider);
                if (!string.IsNullOrWhiteSpace(schedule.Error))
                {
                    LogSystemWarning(
                        "策略运行时间配置异常：标的={Symbol} 错误={Error}",
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
                    FundingRate = request.FundingRate,
                    SlippageBps = request.SlippageBps,
                    AutoReverse = request.AutoReverse,
                    ContractSize = contract?.ContractSize ?? 1m,
                    Contract = contract
                };

                var result = new BacktestSymbolResult
                {
                    Symbol = symbol
                };

                runtimes[symbol] = new BacktestSymbolRuntime(
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

        private static string ResolveExecutionMode(string? requestMode, string? configuredMode)
        {
            var normalizedRequest = NormalizeExecutionMode(requestMode);
            if (!string.IsNullOrWhiteSpace(normalizedRequest))
            {
                return normalizedRequest;
            }

            var normalizedConfigured = NormalizeExecutionMode(configuredMode);
            if (!string.IsNullOrWhiteSpace(normalizedConfigured))
            {
                return normalizedConfigured;
            }

            return BacktestExecutionModes.BatchOpenClose;
        }

        private static string NormalizeExecutionMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized switch
            {
                "batch_open_close" => BacktestExecutionModes.BatchOpenClose,
                "batchopenclose" => BacktestExecutionModes.BatchOpenClose,
                "batch" => BacktestExecutionModes.BatchOpenClose,
                "fast" => BacktestExecutionModes.BatchOpenClose,
                "timeline" => BacktestExecutionModes.Timeline,
                "time_serial" => BacktestExecutionModes.Timeline,
                "time-serial" => BacktestExecutionModes.Timeline,
                _ => string.Empty
            };
        }

        // ---- 参数解析 → BacktestParameterParser ----
        // ---- 数据加载 → BacktestDataLoader ----
        // ---- 主循环执行/强平收尾 → BacktestMainLoop ----
        // ---- 统计/序列化 → BacktestStatisticsBuilder ----
        // ---- 滑点计算 → BacktestSlippageHelper ----


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

    }
}
