using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StrategyModel = ServerTest.Models.Strategy.Strategy;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.Shared.Application.Diagnostics;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Options;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class RealTimeStrategyEngine
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly MarketDataTaskSubscription _marketTaskSubscription;
        private readonly ILogger<RealTimeStrategyEngine> _logger;
        private readonly IStrategyValueResolver _valueResolver;
        private readonly IStrategyActionExecutor? _actionExecutor;
        private readonly StrategyEngineRunLogQueue? _runLogQueue;
        private readonly int _maxParallelism;
        private readonly IndicatorEngine? _indicatorEngine;
        private readonly ConditionEvaluator _conditionEvaluator;
        private readonly ConditionUsageTracker _conditionUsageTracker;
        private readonly IStrategyRuntimeTemplateProvider? _templateProvider;
        private readonly StrategyTaskTraceLogQueue? _taskTraceLogQueue;
        private readonly string _engineInstanceId;
        private readonly StrategyDiagnosticsOptions _diagnostics;
        private readonly ParallelOptions _parallelOptions;
        private readonly LiveTradingObjectPoolManager? _objectPool;
        private const int ParallelExecutionThreshold = 8;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StrategyModel>> _strategiesByKey = new();
        private readonly ConcurrentDictionary<string, string> _strategyKeyByUid = new();
        private readonly ConcurrentDictionary<string, List<IndicatorRequest>> _indicatorRequestsByStrategy = new();
        private readonly ConcurrentDictionary<string, StrategyRuntimeSchedule> _runtimeSchedules = new();

        public RealTimeStrategyEngine(
            MarketDataEngine marketDataEngine,
            ILogger<RealTimeStrategyEngine> logger,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null,
            StrategyEngineRunLogQueue? runLogQueue = null,
            IndicatorEngine? indicatorEngine = null,
            ConditionEvaluator? conditionEvaluator = null,
            ConditionUsageTracker? conditionUsageTracker = null,
            IStrategyRuntimeTemplateProvider? templateProvider = null,
            StrategyTaskTraceLogQueue? taskTraceLogQueue = null,
            int? maxParallelism = null,
            IOptions<StrategyDiagnosticsOptions>? diagnosticsOptions = null,
            LiveTradingObjectPoolManager? objectPool = null)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _marketTaskSubscription = _marketDataEngine.SubscribeMarketTasks("StrategyEngine");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _valueResolver = valueResolver ?? NoopStrategyValueResolver.Instance;
            _actionExecutor = actionExecutor;
            _runLogQueue = runLogQueue;
            _indicatorEngine = indicatorEngine;
            _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
            _conditionUsageTracker = conditionUsageTracker ?? throw new ArgumentNullException(nameof(conditionUsageTracker));
            _templateProvider = templateProvider;
            _taskTraceLogQueue = taskTraceLogQueue;
            _maxParallelism = Math.Max(1, maxParallelism ?? Environment.ProcessorCount);
            _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism };
            _engineInstanceId = ProcessInstanceIdProvider.InstanceId;
            _diagnostics = diagnosticsOptions?.Value ?? new StrategyDiagnosticsOptions();
            _objectPool = objectPool;
        }

        public void UpsertStrategy(StrategyModel strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }

            var trade = strategy.StrategyConfig?.Trade;
            if (trade == null)
            {
                _logger.LogWarning("策略 {Uid} 缺少交易配置", strategy.UidCode);
                return;
            }

            _conditionEvaluator.InvalidateStrategy(strategy.UidCode);
            var key = BuildIndexKey(trade.Exchange, trade.Symbol, trade.TimeframeSec);
            var bucket = _strategiesByKey.GetOrAdd(
                key,
                _ => new ConcurrentDictionary<string, StrategyModel>(StringComparer.Ordinal));
            var indicatorRequests = BuildIndicatorRequests(strategy);
            _indicatorEngine?.RegisterRequestsForStrategy(indicatorRequests);
            bucket[strategy.UidCode] = strategy;
            _strategyKeyByUid[strategy.UidCode] = key;
            _indicatorRequestsByStrategy[strategy.UidCode] = indicatorRequests;
            _conditionUsageTracker.UpsertStrategy(strategy);
            var runtimeSchedule = new StrategyRuntimeSchedule(strategy.StrategyConfig?.Runtime, _templateProvider);
            _runtimeSchedules[strategy.UidCode] = runtimeSchedule;
            if (!string.IsNullOrWhiteSpace(runtimeSchedule.Error))
            {
                _logger.LogWarning(
                    "策略运行时间配置异常: {Uid} 错误={Error}",
                    strategy.UidCode,
                    runtimeSchedule.Error);
            }

            if (indicatorRequests.Count > 0)
            {
                //var indicators = string.Join("\n", indicatorRequests.Select(request => request.Key.ToString()));
                //_logger.LogInformation(
                //    "策略已注册: {Uid} 交易所={Exchange} 交易对={Symbol} 周期={TimeframeSec}秒 指标数={Count}\n{Indicators}",
                //    strategy.UidCode,
                //    trade.Exchange,
                //    trade.Symbol,
                //    trade.TimeframeSec,
                //    indicatorRequests.Count,
                //    indicators);
            }
            else
            {
                //_logger.LogInformation(
                //    "策略已注册: {Uid} 交易所={Exchange} 交易对={Symbol} 周期={TimeframeSec}秒 无指标",
                //    strategy.UidCode,
                //    trade.Exchange,
                //    trade.Symbol,
                //    trade.TimeframeSec);
            }
        }

        public bool RemoveStrategy(string uidCode)
        {
            if (string.IsNullOrWhiteSpace(uidCode))
            {
                return false;
            }

            if (!_strategyKeyByUid.TryRemove(uidCode, out var key))
            {
                return false;
            }

            if (_strategiesByKey.TryGetValue(key, out var bucket))
            {
                bucket.TryRemove(uidCode, out _);
                if (bucket.IsEmpty)
                {
                    _strategiesByKey.TryRemove(key, out _);
                }
            }

            _indicatorRequestsByStrategy.TryRemove(uidCode, out _);
            _runtimeSchedules.TryRemove(uidCode, out _);
            _conditionUsageTracker.RemoveStrategy(uidCode);
            _conditionEvaluator.InvalidateStrategy(uidCode);
            return true;
        }

        /// <summary>
        /// 判断指定策略是否仍在当前节点运行时中注册。
        /// </summary>
        public bool HasStrategy(string uidCode)
        {
            if (string.IsNullOrWhiteSpace(uidCode))
            {
                return false;
            }

            return _strategyKeyByUid.ContainsKey(uidCode);
        }

        public int GetRegisteredStrategyCount()
        {
            var total = 0;
            foreach (var bucket in _strategiesByKey.Values)
            {
                total += bucket.Count;
            }

            return total;
        }

        public void GetStateCounts(out int running, out int pausedOpenPosition, out int testing, out int total)
        {
            running = 0;
            pausedOpenPosition = 0;
            testing = 0;
            total = 0;

            foreach (var bucket in _strategiesByKey.Values)
            {
                foreach (var strategy in bucket.Values)
                {
                    total++;
                    switch (strategy.State)
                    {
                        case StrategyState.Running:
                            running++;
                            break;
                        case StrategyState.PausedOpenPosition:
                            pausedOpenPosition++;
                            break;
                        case StrategyState.Testing:
                            testing++;
                            break;
                    }
                }
            }
        }

        public int GetRunnableStrategyCountForTimeframes(IReadOnlyCollection<int> timeframeSecs)
        {
            if (timeframeSecs == null || timeframeSecs.Count == 0)
            {
                return 0;
            }

            var total = 0;
            foreach (var bucket in _strategiesByKey.Values)
            {
                foreach (var strategy in bucket.Values)
                {
                    var trade = strategy.StrategyConfig?.Trade;
                    if (trade == null || !timeframeSecs.Contains(trade.TimeframeSec))
                    {
                        continue;
                    }

                    if (IsRunnableState(strategy.State))
                    {
                        total++;
                    }
                }
            }

            return total;
        }

        public bool TryProcessNextTask()
        {
            if (!_marketTaskSubscription.TryRead(out var task))
            {
                return false;
            }

            HandleTask(task);
            return true;
        }

        internal MarketDataTaskSubscription MarketTaskSubscription => _marketTaskSubscription;

        internal void ProcessTask(MarketDataTask task) => HandleTask(task);

        internal void AdjustInnerParallelism(int shardCount)
        {
            if (shardCount > 1)
            {
                _parallelOptions.MaxDegreeOfParallelism = Math.Max(2, _maxParallelism / shardCount);
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                MarketDataTask task;
                try
                {
                    task = await _marketTaskSubscription.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                HandleTask(task);
            }
        }

        public Task RunWorkersAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            var workers = new Task[workerCount];
            for (var i = 0; i < workerCount; i++)
            {
                workers[i] = RunAsync(cancellationToken);
            }

            return Task.WhenAll(workers);
        }

        private void HandleTask(MarketDataTask task)
        {
            var t0 = Stopwatch.GetTimestamp();
            var trace = new StrategyRunTrace();
            var matchedCount = 0;

            TraceTaskClaim(task);

            try
            {
                var key = BuildIndexKey(task.Exchange, task.Symbol, task.TimeframeSec);
                _strategiesByKey.TryGetValue(key, out var strategies);
                matchedCount = strategies?.Count ?? 0;
                var t1 = Stopwatch.GetTimestamp();
                trace.LookupMs = TicksToMs(t0, t1);

                if (strategies == null || strategies.IsEmpty)
                {
                    LogStrategyRun(task, matchedCount, TicksToMs(t0, t1), trace);
                    return;
                }

                var indicatorSnapshot = UpdateIndicatorsBeforeExecute(strategies.Values, task);
                var t2 = Stopwatch.GetTimestamp();
                trace.IndicatorMs = TicksToMs(t1, t2);
                trace.IndicatorRequestCount = indicatorSnapshot.RequestCount;
                trace.IndicatorSuccessCount = indicatorSnapshot.SuccessCount;
                trace.IndicatorTotalCount = indicatorSnapshot.TotalCount;

                if (_maxParallelism <= 1 || matchedCount < ParallelExecutionThreshold)
                {
                    foreach (var strategy in strategies.Values)
                    {
                        ExecuteStrategyTask(strategy, task, trace);
                    }
                }
                else
                {
                    Parallel.ForEach(strategies.Values, _parallelOptions, strategy =>
                    {
                        ExecuteStrategyTask(strategy, task, trace);
                    });
                }
                var t3 = Stopwatch.GetTimestamp();
                trace.ExecuteMs = TicksToMs(t2, t3);

                LogStrategyRun(task, matchedCount, TicksToMs(t0, t3), trace);
            }
            catch (Exception ex)
            {
                var now = Stopwatch.GetTimestamp();
                LogStrategyRun(task, matchedCount, TicksToMs(t0, now), trace, ex);
                throw;
            }
        }

        private static int TicksToMs(long start, long end)
        {
            return (int)((end - start) * 1000 / Stopwatch.Frequency);
        }

        private void ExecuteStrategyTask(
            StrategyModel strategy,
            MarketDataTask task,
            StrategyRunTrace? trace)
        {
            if (!IsRunnableState(strategy.State))
            {
                trace?.IncrementSkippedByState();
                trace?.IncrementSkipped();
                return;
            }

            trace?.IncrementRunnableStrategy();
            var startTicks = Stopwatch.GetTimestamp();

            StrategyExecutionContext? context = null;
            try
            {
                context = _objectPool != null
                    ? _objectPool.RentContext(strategy, task, _valueResolver, _actionExecutor)
                    : new StrategyExecutionContext(strategy, task, _valueResolver, _actionExecutor);

                var runtimeGate = ResolveRuntimeGate(strategy, context.CurrentTime);
                if (!runtimeGate.AllowEntry && !runtimeGate.AllowExit)
                {
                    trace?.IncrementSkippedByRuntimeGate();
                    trace?.IncrementSkipped();
                    return;
                }

                ExecuteLogic(context, runtimeGate, trace);
                trace?.AddExecutedStrategy(strategy.UidCode);
                trace?.IncrementExecuted();
            }
            finally
            {
                if (trace != null)
                {
                    var elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency;
                    trace.RecordStrategyExec(elapsedMs);
                }
                if (_objectPool != null && context != null)
                {
                    _objectPool.ReturnContext(context);
                }
            }
        }

        private void ExecuteLogic(
            StrategyExecutionContext context,
            StrategyRuntimeGate runtimeGate,
            StrategyRunTrace? trace)
        {
            var logic = context.StrategyConfig.Logic;
            if (logic == null)
            {
                return;
            }

            if (runtimeGate.AllowExit)
            {
                ExecuteBranch(context, logic.Exit.Long, "Exit.Long", trace);
            }
            //ExecuteBranch(context, logic.Exit.Short, "Exit.Short");

            if (context.Strategy.State == StrategyState.PausedOpenPosition)
            {
                return;
            }

            if (!runtimeGate.AllowEntry)
            {
                return;
            }

            if (!EvaluateEntryFilters(context, logic.Entry.Long, "Entry.Long", trace))
            {
                return;
            }

            ExecuteBranch(context, logic.Entry.Long, "Entry.Long", trace);
            //ExecuteBranch(context, logic.Entry.Short, "Entry.Short");
        }

        private bool EvaluateEntryFilters(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage,
            StrategyRunTrace? trace)
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

            PrecomputeRequiredConditions(context, filters, trace);
            var results = _objectPool != null ? _objectPool.RentResultList() : new List<ConditionEvaluationResult>();
            var stageLabel = stage;
            var pass = EvaluateChecks(context, filters, results, stageLabel, trace);
            if (!pass && trace != null)
            {
                trace.IncrementSkipped();
            }

            if (_objectPool != null) _objectPool.ReturnResultList(results);
            return pass;
        }

        private StrategyRuntimeGate ResolveRuntimeGate(StrategyModel strategy, DateTimeOffset currentTime)
        {
            if (!_runtimeSchedules.TryGetValue(strategy.UidCode, out var schedule))
            {
                schedule = new StrategyRuntimeSchedule(strategy.StrategyConfig?.Runtime, _templateProvider);
                _runtimeSchedules[strategy.UidCode] = schedule;
            }

            var evaluation = schedule.Evaluate(currentTime);
            if (evaluation.Changed)
            {
                _logger.LogInformation(
                    "策略运行时间切换: {Uid} 允许={Allowed} 模式={Summary} 时间={Time}",
                    strategy.UidCode,
                    evaluation.Allowed,
                    schedule.Summary,
                    FormatTimestampIso(currentTime.ToUnixTimeMilliseconds()));
            }

            if (evaluation.Allowed)
            {
                return StrategyRuntimeGate.AllowAll;
            }

            return schedule.Policy == StrategyRuntimeOutOfSessionPolicy.BlockAll
                ? StrategyRuntimeGate.BlockAll
                : StrategyRuntimeGate.BlockEntryAllowExit;
        }

        private void ExecuteBranch(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage,
            StrategyRunTrace? trace)
        {
            if (branch == null || !branch.Enabled)
            {
                return;
            }

            var containers = branch.Containers;
            if (containers == null || containers.Count == 0) return;
            var passCount = 0;
            var debugBranch = _diagnostics.LogEveryRunTask && _logger.IsEnabled(LogLevel.Debug);
            List<ConditionEvaluationResult>? aggregatedResults = null;
            try
            {
            aggregatedResults = _objectPool != null ? _objectPool.RentResultList() : new List<ConditionEvaluationResult>();

            PrecomputeRequiredConditions(context, containers, trace);

            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (container == null)
                {
                    continue;
                }

                var checkResults = _objectPool != null ? _objectPool.RentResultList() : new List<ConditionEvaluationResult>();
                // stageLabel only built when needed for debug logging
                //_logger.LogDebug(
                //    "策略检查开始: {Uid} {Stage} 时间={Time}",
                //    context.Strategy.UidCode,
                //    stageLabel,
                //    FormatTimestamp(context.Task.CandleTimestamp));

                if (!EvaluateChecks(context, container.Checks, checkResults, stage, trace))
                {
                    if (_objectPool != null) _objectPool.ReturnResultList(checkResults);
                    continue;
                }

                passCount++;
                aggregatedResults.AddRange(checkResults);
                if (_objectPool != null) _objectPool.ReturnResultList(checkResults);
            }

            if (passCount < branch.MinPassConditionContainer)
            {
                //_logger.LogDebug(
                //    "策略容器数量不足: {Uid} {Stage} 需要={Need} 通过={Pass}",
                //    context.Strategy.UidCode,
                //    stage,
                //    branch.MinPassConditionContainer,
                //    passCount);
                return;
            }

            ExecuteActions(context, branch.OnPass, aggregatedResults, stage, trace);
            }
            finally
            {
                if (_objectPool != null) _objectPool.ReturnResultList(aggregatedResults);
            }
        }

        private void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            IReadOnlyList<ConditionContainer> containers,
            StrategyRunTrace? trace)
        {
            if (containers == null || containers.Count == 0)
            {
                return;
            }

            foreach (var container in containers)
            {
                if (container?.Checks?.Groups == null || !container.Checks.Enabled)
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

                        _conditionEvaluator.Evaluate(context, condition);
                        trace?.IncrementConditionEval();
                    }
                }
            }
        }

        private void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            ConditionGroupSet? checks,
            StrategyRunTrace? trace)
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

                    _conditionEvaluator.Evaluate(context, condition);
                    trace?.IncrementConditionEval();
                }
            }
        }

        private bool EvaluateChecks(
            StrategyExecutionContext context,
            ConditionGroupSet checks,
            List<ConditionEvaluationResult> results,
            string stage,
            StrategyRunTrace? trace)
        {
            if (checks == null || !checks.Enabled)
            {
                return false;
            }

            var groups = checks.Groups;
            if (groups == null || groups.Count == 0) return false;
            var passGroups = 0;
            var debugChecks = _diagnostics.LogEveryRunTask && _logger.IsEnabled(LogLevel.Debug);

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                if (group == null || !group.Enabled)
                {
                    continue;
                }

                var conditions = group.Conditions;
                if (conditions == null || conditions.Count == 0)
                {
                    if (group.MinPassConditions <= 0) passGroups++;
                    continue;
                }

                List<StrategyMethod>? crossRequired = null, crossOptional = null, requiredL = null, optionalL = null;
                try
                {
                    if (_objectPool != null)
                    {
                        crossRequired = _objectPool.RentMethodList();
                        crossOptional = _objectPool.RentMethodList();
                        requiredL = _objectPool.RentMethodList();
                        optionalL = _objectPool.RentMethodList();
                    }
                    else
                    {
                        crossRequired = new List<StrategyMethod>();
                        crossOptional = new List<StrategyMethod>();
                        requiredL = new List<StrategyMethod>();
                        optionalL = new List<StrategyMethod>();
                    }

                    ConditionPriorityHelper.SplitByPriority(conditions, crossRequired, crossOptional, requiredL, optionalL);

                    var hasEnabled = crossRequired.Count + crossOptional.Count + requiredL.Count + optionalL.Count > 0;
                    if (!hasEnabled)
                    {
                        if (group.MinPassConditions <= 0) passGroups++;
                        continue;
                    }

                    var requiredFailed = false;
                    var optionalPassCount = 0;

                    // P0: Cross/Required
                    foreach (var condition in crossRequired)
                    {
                        var result = _conditionEvaluator.Evaluate(context, condition);
                        trace?.IncrementConditionEval();
                        results.Add(result);
                        if (debugChecks)
                            _logger.LogDebug("\u6761\u4ef6\u68c0\u67e5: {Uid} {Stage} G{Group} M={Method} R=true P={Result}",
                                context.Strategy.UidCode, stage, groupIndex, condition.Method, result.Success);
                        if (!result.Success) { requiredFailed = true; break; }
                    }
                    if (requiredFailed) continue;

                    // P0: Cross/Optional
                    foreach (var condition in crossOptional)
                    {
                        var result = _conditionEvaluator.Evaluate(context, condition);
                        trace?.IncrementConditionEval();
                        results.Add(result);
                        if (result.Success) optionalPassCount++;
                    }

                    // P1: Required
                    foreach (var condition in requiredL)
                    {
                        var result = _conditionEvaluator.Evaluate(context, condition);
                        trace?.IncrementConditionEval();
                        results.Add(result);
                        if (debugChecks)
                            _logger.LogDebug("\u6761\u4ef6\u68c0\u67e5: {Uid} {Stage} G{Group} M={Method} R=true P={Result}",
                                context.Strategy.UidCode, stage, groupIndex, condition.Method, result.Success);
                        if (!result.Success) { requiredFailed = true; break; }
                    }
                    if (requiredFailed) continue;

                    // P2: Optional
                    if (optionalPassCount < group.MinPassConditions)
                    {
                        foreach (var condition in optionalL)
                        {
                            var result = _conditionEvaluator.Evaluate(context, condition);
                            trace?.IncrementConditionEval();
                            results.Add(result);
                            if (result.Success) optionalPassCount++;
                            if (optionalPassCount >= group.MinPassConditions) break;
                        }
                    }

                    if (optionalPassCount >= group.MinPassConditions) passGroups++;
                }
                finally
                {
                    if (_objectPool != null)
                    {
                        _objectPool.ReturnMethodList(crossRequired);
                        _objectPool.ReturnMethodList(crossOptional);
                        _objectPool.ReturnMethodList(requiredL);
                        _objectPool.ReturnMethodList(optionalL);
                    }
                }
            }

            return passGroups >= checks.MinPassGroups;
        }

        private void ExecuteActions(
            StrategyExecutionContext context,
            ActionSet actions,
            IReadOnlyList<ConditionEvaluationResult> triggerResults,
            string stage,
            StrategyRunTrace? trace)
        {
            if (actions == null || !actions.Enabled)
            {
                return;
            }

            var optionalSuccessCount = 0;
            var hasEnabled = false;

            var actionConditions = actions.Conditions;
            if (actionConditions == null || actionConditions.Count == 0) return;
            var debugActions = _diagnostics.LogEveryRunTask && _logger.IsEnabled(LogLevel.Debug);

            foreach (var action in actionConditions)
            {
                if (action == null || !action.Enabled)
                {
                    continue;
                }

                trace?.IncrementActionExec();
                hasEnabled = true;
                var result = ActionMethodRegistry.Run(context, action, triggerResults);
                if (debugActions)
                    _logger.LogDebug("动作执行: {Uid} {Stage} M={Method} R={Required} P={Result}",
                        context.Strategy.UidCode, stage, action.Method, action.Required, result.Success);

                if (result.Success && IsOpenAction(action))
                {
                    trace?.AddOpenTaskStrategy(context.Strategy.UidCode);
                    var actionTaskTraceId = ExtractActionTaskTraceId(result.Message);
                    if (!string.IsNullOrWhiteSpace(actionTaskTraceId))
                    {
                        trace?.AddOpenTaskTraceId(actionTaskTraceId);
                    }
                    trace?.IncrementOpenTask();
                }

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

            // min pass check (no debug log in hot path)
        }

        private static string BuildIndexKey(string exchange, string symbol, int timeframeSec)
        {
            return $"{exchange}|{symbol}|{timeframeSec}";
        }

        private void LogStrategyRun(
            MarketDataTask task,
            int matchedCount,
            long durationMs,
            StrategyRunTrace? trace,
            Exception? runError = null)
        {
            var executedIds = BuildDelimitedIds(trace?.EnumerateExecutedStrategyIds());
            var openIds = BuildDelimitedIds(trace?.EnumerateOpenTaskStrategyIds());
            var openTaskTraceIds = BuildDelimitedIds(trace?.EnumerateOpenTaskTraceIds());
            var executedCount = trace?.ExecutedCount ?? 0;
            var skippedCount = trace?.SkippedCount ?? 0;
            var conditionEvalCount = trace?.ConditionEvalCount ?? 0;
            var actionExecCount = trace?.ActionExecCount ?? 0;
            var openTaskCount = trace?.OpenTaskCount ?? 0;
            var modeText = task.IsBarClose ? "close" : "update";
            var traceId = MarketDataTask.NormalizeTraceId(task.TraceId);
            var runStatus = ResolveRunStatus(matchedCount, trace, runError);

            if (executedCount > 0)
            {
                _logger.LogInformation(
                    "[策略运行] 追踪ID={TraceId} 处理方={Instance} 交易所={Exchange} 交易对={Symbol} 周期={Timeframe} 模式={Mode} 时间={Time} 耗时={Duration}毫秒 匹配={Matched} 执行={Executed} 跳过={Skipped} 条件={Conditions} 动作={Actions} 开放任务={OpenTasks} 执行ID={ExecIds} 开放ID={OpenIds}",
                    traceId,
                    _engineInstanceId,
                    task.Exchange,
                    task.Symbol,
                    task.Timeframe,
                    modeText,
                    FormatTimestampIso(task.CandleTimestamp),
                    durationMs,
                    matchedCount,
                    executedCount,
                    skippedCount,
                    conditionEvalCount,
                    actionExecCount,
                    openTaskCount,
                    executedIds,
                    openIds);
            }

            if (trace != null && ShouldLogRunProfile(durationMs, trace))
            {
                var avgStrategyMs = trace.GetStrategyExecAverageMs();
                var maxStrategyMs = trace.GetStrategyExecMaxMs();
                var samples = trace.GetStrategyExecSamples();
                if (IsSlowRun(durationMs, trace))
                {
                    _logger.LogWarning(
                        "[策略运行画像] 追踪ID={TraceId} 处理方={Instance} 交易所={Exchange} 交易对={Symbol} 周期={Timeframe} 模式={Mode} 时间={Time} 总耗时={Duration}毫秒 分段=查找{Lookup}ms/指标{Indicator}ms/执行{Execute}ms 匹配={Matched} 可运行={Runnable} 执行={Executed} 状态跳过={StateSkipped} 时间门禁跳过={GateSkipped} 条件={Conditions} 动作={Actions} 开放任务={OpenTasks} 指标请求={IndicatorReq} 指标成功={IndicatorSuccess}/{IndicatorTotal} 单策略样本={StrategySamples} 单策略均值={StrategyAvg}ms 单策略最大={StrategyMax}ms",
                        traceId,
                        _engineInstanceId,
                        task.Exchange,
                        task.Symbol,
                        task.Timeframe,
                        modeText,
                        FormatTimestampIso(task.CandleTimestamp),
                        durationMs,
                        trace.LookupMs,
                        trace.IndicatorMs,
                        trace.ExecuteMs,
                        matchedCount,
                        trace.RunnableStrategyCount,
                        executedCount,
                        trace.SkippedByStateCount,
                        trace.SkippedByRuntimeGateCount,
                        conditionEvalCount,
                        actionExecCount,
                        openTaskCount,
                        trace.IndicatorRequestCount,
                        trace.IndicatorSuccessCount,
                        trace.IndicatorTotalCount,
                        samples,
                        avgStrategyMs,
                        maxStrategyMs);
                }
                else
                {
                    _logger.LogInformation(
                        "[策略运行画像] 追踪ID={TraceId} 处理方={Instance} 交易所={Exchange} 交易对={Symbol} 周期={Timeframe} 模式={Mode} 时间={Time} 总耗时={Duration}毫秒 分段=查找{Lookup}ms/指标{Indicator}ms/执行{Execute}ms 匹配={Matched} 可运行={Runnable} 执行={Executed} 状态跳过={StateSkipped} 时间门禁跳过={GateSkipped} 条件={Conditions} 动作={Actions} 开放任务={OpenTasks} 指标请求={IndicatorReq} 指标成功={IndicatorSuccess}/{IndicatorTotal} 单策略样本={StrategySamples} 单策略均值={StrategyAvg}ms 单策略最大={StrategyMax}ms",
                        traceId,
                        _engineInstanceId,
                        task.Exchange,
                        task.Symbol,
                        task.Timeframe,
                        modeText,
                        FormatTimestampIso(task.CandleTimestamp),
                        durationMs,
                        trace.LookupMs,
                        trace.IndicatorMs,
                        trace.ExecuteMs,
                        matchedCount,
                        trace.RunnableStrategyCount,
                        executedCount,
                        trace.SkippedByStateCount,
                        trace.SkippedByRuntimeGateCount,
                        conditionEvalCount,
                        actionExecCount,
                        openTaskCount,
                        trace.IndicatorRequestCount,
                        trace.IndicatorSuccessCount,
                        trace.IndicatorTotalCount,
                        samples,
                        avgStrategyMs,
                        maxStrategyMs);
                }
            }

            if (runError != null)
            {
                _logger.LogError(
                    runError,
                    "[策略运行失败] 追踪ID={TraceId} 处理方={Instance} 交易所={Exchange} 交易对={Symbol} 周期={Timeframe} 模式={Mode} 时间={Time} 已耗时={Duration}毫秒",
                    traceId,
                    _engineInstanceId,
                    task.Exchange,
                    task.Symbol,
                    task.Timeframe,
                    modeText,
                    FormatTimestampIso(task.CandleTimestamp),
                    durationMs);
            }

            TraceTaskRun(task, matchedCount, durationMs, trace, runError);

            if (_runLogQueue == null)
            {
                return;
            }

            var log = new StrategyEngineRunLog
            {
                RunAt = DateTime.UtcNow,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Timeframe = task.Timeframe,
                CandleTimestamp = task.CandleTimestamp,
                IsBarClose = task.IsBarClose,
                DurationMs = (int)Math.Min(durationMs, int.MaxValue),
                MatchedCount = matchedCount,
                ExecutedCount = executedCount,
                SkippedCount = skippedCount,
                ConditionEvalCount = conditionEvalCount,
                ActionExecCount = actionExecCount,
                OpenTaskCount = openTaskCount,
                TraceId = traceId,
                RunStatus = runStatus,
                LookupMs = trace?.LookupMs ?? 0,
                IndicatorMs = trace?.IndicatorMs ?? 0,
                ExecuteMs = trace?.ExecuteMs ?? 0,
                RunnableStrategyCount = trace?.RunnableStrategyCount ?? 0,
                StateSkippedCount = trace?.SkippedByStateCount ?? 0,
                RuntimeGateSkippedCount = trace?.SkippedByRuntimeGateCount ?? 0,
                IndicatorRequestCount = trace?.IndicatorRequestCount ?? 0,
                IndicatorSuccessCount = trace?.IndicatorSuccessCount ?? 0,
                IndicatorTotalCount = trace?.IndicatorTotalCount ?? 0,
                ExecutedStrategyIds = string.IsNullOrWhiteSpace(executedIds) ? null : executedIds,
                OpenTaskStrategyIds = string.IsNullOrWhiteSpace(openIds) ? null : openIds,
                OpenTaskTraceIds = string.IsNullOrWhiteSpace(openTaskTraceIds) ? null : openTaskTraceIds,
                ExtraJson = _diagnostics.PersistRunProfileExtraJson
                    ? BuildRunExtraJson(durationMs, matchedCount, trace, runError, traceId)
                    : null,
                EngineInstance = _engineInstanceId
            };

            if (!_runLogQueue.TryEnqueue(log))
            {
                _logger.LogWarning("策略运行日志入队失败");
            }
        }

        private void TraceTaskClaim(MarketDataTask task)
        {
            if (!ShouldPersistTaskTrace())
            {
                return;
            }

            var traceId = MarketDataTask.NormalizeTraceId(task.TraceId);
            var signalLagMs = ResolveSignalLagMs(task.CandleTimestamp);
            var payload = new
            {
                mode = task.IsBarClose ? "close" : "update",
                signalLagMs
            };

            _taskTraceLogQueue!.TryEnqueue(new StrategyTaskTraceLog
            {
                TraceId = traceId,
                ParentTraceId = null,
                EventStage = "strategy.claim",
                EventStatus = "start",
                ActorModule = nameof(RealTimeStrategyEngine),
                ActorInstance = _engineInstanceId,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Timeframe = task.Timeframe,
                CandleTimestamp = task.CandleTimestamp,
                IsBarClose = task.IsBarClose,
                Method = task.IsBarClose ? "close" : "update",
                DurationMs = 0,
                MetricsJson = SerializeTracePayload(payload)
            });
        }

        private void TraceTaskRun(
            MarketDataTask task,
            int matchedCount,
            long durationMs,
            StrategyRunTrace? trace,
            Exception? runError)
        {
            var status = ResolveRunStatus(matchedCount, trace, runError);
            if (!ShouldPersistTaskTrace(durationMs, status, runError))
            {
                return;
            }

            var traceId = MarketDataTask.NormalizeTraceId(task.TraceId);
            var payload = new
            {
                stage = new
                {
                    lookupMs = trace?.LookupMs ?? 0,
                    indicatorMs = trace?.IndicatorMs ?? 0,
                    executeMs = trace?.ExecuteMs ?? 0,
                    totalMs = (int)Math.Min(durationMs, int.MaxValue)
                },
                counters = new
                {
                    matchedCount,
                    runnableCount = trace?.RunnableStrategyCount ?? 0,
                    executedCount = trace?.ExecutedCount ?? 0,
                    skippedCount = trace?.SkippedCount ?? 0,
                    conditionEvalCount = trace?.ConditionEvalCount ?? 0,
                    actionExecCount = trace?.ActionExecCount ?? 0,
                    openTaskCount = trace?.OpenTaskCount ?? 0,
                    indicatorRequestCount = trace?.IndicatorRequestCount ?? 0,
                    indicatorSuccessCount = trace?.IndicatorSuccessCount ?? 0,
                    indicatorTotalCount = trace?.IndicatorTotalCount ?? 0
                },
                strategy = new
                {
                    stateSkippedCount = trace?.SkippedByStateCount ?? 0,
                    runtimeGateSkippedCount = trace?.SkippedByRuntimeGateCount ?? 0,
                    perStrategySamples = trace?.GetStrategyExecSamples() ?? 0,
                    perStrategyAvgMs = trace?.GetStrategyExecAverageMs() ?? 0,
                    perStrategyMaxMs = trace?.GetStrategyExecMaxMs() ?? 0
                }
            };

            _taskTraceLogQueue!.TryEnqueue(new StrategyTaskTraceLog
            {
                TraceId = traceId,
                ParentTraceId = null,
                EventStage = "strategy.run",
                EventStatus = status,
                ActorModule = nameof(RealTimeStrategyEngine),
                ActorInstance = _engineInstanceId,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Timeframe = task.Timeframe,
                CandleTimestamp = task.CandleTimestamp,
                IsBarClose = task.IsBarClose,
                Method = task.IsBarClose ? "close" : "update",
                DurationMs = (int)Math.Min(durationMs, int.MaxValue),
                MetricsJson = SerializeTracePayload(payload),
                ErrorMessage = runError?.Message
            });
        }

        private bool ShouldPersistTaskTrace(
            long? durationMs = null,
            string? status = null,
            Exception? error = null)
        {
            if (_taskTraceLogQueue == null || !_diagnostics.EnableTaskTracePersist)
            {
                return false;
            }

            if (_diagnostics.TaskTraceLogEveryEvent)
            {
                return true;
            }

            if (error != null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(status) &&
                !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return durationMs.HasValue && durationMs.Value >= _diagnostics.SlowTaskTraceThresholdMs;
        }

        private static string ResolveRunStatus(int matchedCount, StrategyRunTrace? trace, Exception? runError)
        {
            if (runError != null)
            {
                return "fail";
            }

            if (matchedCount <= 0)
            {
                return "skip_nomatch";
            }

            if (trace == null || trace.ExecutedCount <= 0)
            {
                return "skip_no_execute";
            }

            return "success";
        }

        private static int ResolveSignalLagMs(long candleTimestamp)
        {
            if (candleTimestamp <= 0)
            {
                return -1;
            }

            var lag = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - candleTimestamp;
            if (lag <= 0)
            {
                return 0;
            }

            return (int)Math.Min(lag, int.MaxValue);
        }

        private static string? SerializeTracePayload(object payload)
        {
            try
            {
                return JsonSerializer.Serialize(payload);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsOpenAction(StrategyMethod action)
        {
            if (action == null)
            {
                return false;
            }

            if (!string.Equals(action.Method, "MakeTrade", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (action.Param == null || action.Param.Length == 0)
            {
                return false;
            }

            foreach (var param in action.Param)
            {
                if (string.Equals(param, "Long", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(param, "Short", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 从动作执行返回消息中提取动作任务ID（taskTraceId）。
        /// </summary>
        private static string? ExtractActionTaskTraceId(StringBuilder? messageBuilder)
        {
            if (messageBuilder == null || messageBuilder.Length == 0)
            {
                return null;
            }

            var message = messageBuilder.ToString();
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            const string marker = "taskTraceId=";
            var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var start = idx + marker.Length;
            if (start >= message.Length)
            {
                return null;
            }

            var end = start;
            while (end < message.Length)
            {
                var ch = message[end];
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9'))
                {
                    end++;
                    continue;
                }

                break;
            }

            if (end <= start)
            {
                return null;
            }

            return message.Substring(start, end - start);
        }

        private static string BuildDelimitedIds(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(id);
            }

            return builder.ToString();
        }

        private static string FormatTimestampIso(long timestamp)
        {
            if (timestamp <= 0)
            {
                return "N/A";
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .ToLocalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss");
        }

        private bool ShouldLogRunProfile(long durationMs, StrategyRunTrace trace)
        {
            if (!_diagnostics.EnableRunProfileLog)
            {
                return false;
            }

            if (_diagnostics.LogEveryRunTask)
            {
                return true;
            }

            if (trace.ExecutedCount > 0)
            {
                return true;
            }

            return IsSlowRun(durationMs, trace);
        }

        private bool IsSlowRun(long durationMs, StrategyRunTrace trace)
        {
            return durationMs >= _diagnostics.SlowRunThresholdMs ||
                   trace.IndicatorMs >= _diagnostics.SlowIndicatorRefreshThresholdMs;
        }

        private string? BuildRunExtraJson(
            long durationMs,
            int matchedCount,
            StrategyRunTrace? trace,
            Exception? runError,
            string traceId)
        {
            if (trace == null)
            {
                return null;
            }

            var payload = new
            {
                trace = new
                {
                    traceId,
                    engineInstance = _engineInstanceId,
                    status = runError == null ? "success" : "fail",
                    error = runError?.Message
                },
                stage = new
                {
                    lookupMs = trace.LookupMs,
                    indicatorMs = trace.IndicatorMs,
                    executeMs = trace.ExecuteMs,
                    totalMs = (int)Math.Min(durationMs, int.MaxValue)
                },
                indicator = new
                {
                    requestCount = trace.IndicatorRequestCount,
                    successCount = trace.IndicatorSuccessCount,
                    totalCount = trace.IndicatorTotalCount
                },
                strategy = new
                {
                    matchedCount,
                    runnableCount = trace.RunnableStrategyCount,
                    executedCount = trace.ExecutedCount,
                    stateSkippedCount = trace.SkippedByStateCount,
                    runtimeGateSkippedCount = trace.SkippedByRuntimeGateCount,
                    perStrategySamples = trace.GetStrategyExecSamples(),
                    perStrategyAvgMs = trace.GetStrategyExecAverageMs(),
                    perStrategyMaxMs = trace.GetStrategyExecMaxMs()
                },
                counters = new
                {
                    skippedCount = trace.SkippedCount,
                    conditionEvalCount = trace.ConditionEvalCount,
                    actionExecCount = trace.ActionExecCount,
                    openTaskCount = trace.OpenTaskCount
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private readonly struct IndicatorRefreshSnapshot
        {
            public IndicatorRefreshSnapshot(int requestCount, int successCount, int totalCount)
            {
                RequestCount = Math.Max(0, requestCount);
                SuccessCount = Math.Max(0, successCount);
                TotalCount = Math.Max(0, totalCount);
            }

            public int RequestCount { get; }
            public int SuccessCount { get; }
            public int TotalCount { get; }

            public static IndicatorRefreshSnapshot Empty => new(0, 0, 0);
        }

        private sealed class StrategyRunTrace
        {
            private long _strategyExecTotalMs;
            private int _strategyExecSamples;
            private int _strategyExecMaxMs;
            private readonly ConcurrentBag<string> _executedStrategyIds = new();
            private readonly ConcurrentBag<string> _openTaskStrategyIds = new();
            private readonly ConcurrentBag<string> _openTaskTraceIds = new();

            public int LookupMs;
            public int IndicatorMs;
            public int ExecuteMs;
            public int IndicatorRequestCount;
            public int IndicatorSuccessCount;
            public int IndicatorTotalCount;
            public int RunnableStrategyCount;
            public int SkippedByStateCount;
            public int SkippedByRuntimeGateCount;
            public int ExecutedCount;
            public int SkippedCount;
            public int ConditionEvalCount;
            public int ActionExecCount;
            public int OpenTaskCount;

            public void IncrementRunnableStrategy()
            {
                Interlocked.Increment(ref RunnableStrategyCount);
            }

            public void IncrementSkippedByState()
            {
                Interlocked.Increment(ref SkippedByStateCount);
            }

            public void IncrementSkippedByRuntimeGate()
            {
                Interlocked.Increment(ref SkippedByRuntimeGateCount);
            }

            public void IncrementExecuted()
            {
                Interlocked.Increment(ref ExecutedCount);
            }

            public void IncrementSkipped()
            {
                Interlocked.Increment(ref SkippedCount);
            }

            public void IncrementConditionEval()
            {
                Interlocked.Increment(ref ConditionEvalCount);
            }

            public void IncrementActionExec()
            {
                Interlocked.Increment(ref ActionExecCount);
            }

            public void IncrementOpenTask()
            {
                Interlocked.Increment(ref OpenTaskCount);
            }

            public void AddExecutedStrategy(string uid)
            {
                if (string.IsNullOrWhiteSpace(uid))
                {
                    return;
                }

                _executedStrategyIds.Add(uid);
            }

            public void AddOpenTaskStrategy(string uid)
            {
                if (string.IsNullOrWhiteSpace(uid))
                {
                    return;
                }

                _openTaskStrategyIds.Add(uid);
            }

            public IEnumerable<string> EnumerateExecutedStrategyIds()
            {
                return _executedStrategyIds;
            }

            public IEnumerable<string> EnumerateOpenTaskStrategyIds()
            {
                return _openTaskStrategyIds;
            }

            public void AddOpenTaskTraceId(string traceId)
            {
                if (string.IsNullOrWhiteSpace(traceId))
                {
                    return;
                }

                _openTaskTraceIds.Add(traceId);
            }

            public IEnumerable<string> EnumerateOpenTaskTraceIds()
            {
                return _openTaskTraceIds;
            }

            public void RecordStrategyExec(long elapsedMs)
            {
                var ms = (int)Math.Clamp(elapsedMs, 0L, (long)int.MaxValue);
                Interlocked.Add(ref _strategyExecTotalMs, ms);
                Interlocked.Increment(ref _strategyExecSamples);

                while (true)
                {
                    var currentMax = Volatile.Read(ref _strategyExecMaxMs);
                    if (ms <= currentMax)
                    {
                        break;
                    }

                    if (Interlocked.CompareExchange(ref _strategyExecMaxMs, ms, currentMax) == currentMax)
                    {
                        break;
                    }
                }
            }

            public int GetStrategyExecSamples()
            {
                return Volatile.Read(ref _strategyExecSamples);
            }

            public int GetStrategyExecAverageMs()
            {
                var samples = Volatile.Read(ref _strategyExecSamples);
                if (samples <= 0)
                {
                    return 0;
                }

                var total = Volatile.Read(ref _strategyExecTotalMs);
                return (int)Math.Clamp(total / samples, 0L, (long)int.MaxValue);
            }

            public int GetStrategyExecMaxMs()
            {
                return Volatile.Read(ref _strategyExecMaxMs);
            }
        }

        private static bool IsRunnableState(StrategyState state)
        {
            return state == StrategyState.Running
                || state == StrategyState.Testing
                || state == StrategyState.PausedOpenPosition;
        }

        private static string FormatTimestamp(long timestamp)
        {
            if (timestamp <= 0)
            {
                return "N/A";
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");
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

        private IndicatorRefreshSnapshot UpdateIndicatorsBeforeExecute(IEnumerable<StrategyModel> strategies, MarketDataTask task)
        {
            if (_indicatorEngine == null)
            {
                return IndicatorRefreshSnapshot.Empty;
            }

            // 指标自动预计算已覆盖到当前行情任务时，跳过同步刷新，减少重复计算。
            if (_indicatorEngine.IsTaskPrecomputed(task))
            {
                return IndicatorRefreshSnapshot.Empty;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(task.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(task.Symbol);
            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(task.Timeframe);
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                timeframe = task.Timeframe;
            }

            var requestMap = new Dictionary<IndicatorKey, IndicatorRequest>();
            foreach (var strategy in strategies)
            {
                if (!IsRunnableState(strategy.State))
                {
                    continue;
                }

                if (!_indicatorRequestsByStrategy.TryGetValue(strategy.UidCode, out var requests))
                {
                    continue;
                }

                foreach (var request in requests)
                {
                    var key = request.Key;
                    if (key.Exchange != exchange || key.Symbol != symbol || key.Timeframe != timeframe)
                    {
                        continue;
                    }

                    if (requestMap.TryGetValue(key, out var existing))
                    {
                        requestMap[key] = existing.WithMaxOffset(Math.Max(existing.MaxOffset, request.MaxOffset));
                    }
                    else
                    {
                        requestMap[key] = request;
                    }
                }
            }

            if (requestMap.Count == 0)
            {
                return IndicatorRefreshSnapshot.Empty;
            }

            var normalizedTask = new MarketDataTask(
                exchange,
                symbol,
                timeframe,
                task.CandleTimestamp,
                task.TimeframeSec,
                task.IsBarClose,
                MarketDataTask.NormalizeTraceId(task.TraceId));
            var indicatorTask = new IndicatorTask(normalizedTask, requestMap.Values.ToList());
            var result = _indicatorEngine.ProcessTaskNow(indicatorTask);
            if (result.TotalCount > 0 && result.SuccessCount >= result.TotalCount)
            {
                _indicatorEngine.MarkTaskPrecomputed(normalizedTask);
            }
            if (_diagnostics.LogEveryRunTask && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "指标刷新: {Exchange} {Symbol} {Timeframe} 时间={Time} 成功={Success}/{Total}",
                    normalizedTask.Exchange,
                    normalizedTask.Symbol,
                    normalizedTask.Timeframe,
                    FormatTimestamp(normalizedTask.CandleTimestamp),
                    result.SuccessCount,
                    result.TotalCount);
            }
            return new IndicatorRefreshSnapshot(
                requestMap.Count,
                result.SuccessCount,
                result.TotalCount);
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

    }
}
