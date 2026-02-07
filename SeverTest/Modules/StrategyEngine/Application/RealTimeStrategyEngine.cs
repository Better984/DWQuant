using Microsoft.Extensions.Logging;
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
using ServerTest.Modules.StrategyEngine.Infrastructure;

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
        private readonly TestStrategyCheckLogRepository? _testCheckLogRepository; // 测试用：策略检查日志仓储（后续会删除）
        private readonly string _engineInstanceId;

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
            TestStrategyCheckLogRepository? testCheckLogRepository = null, // 测试用：策略检查日志仓储（后续会删除）
            int? maxParallelism = null)
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
            _testCheckLogRepository = testCheckLogRepository; // 测试用：可选依赖，避免非测试环境报错
            _maxParallelism = maxParallelism ?? Environment.ProcessorCount;
            _engineInstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
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

            var key = BuildIndexKey(trade.Exchange, trade.Symbol, trade.TimeframeSec);
            var bucket = _strategiesByKey.GetOrAdd(
                key,
                _ => new ConcurrentDictionary<string, StrategyModel>(StringComparer.Ordinal));
            var indicatorRequests = BuildIndicatorRequests(strategy);
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
                var indicators = string.Join("\n", indicatorRequests.Select(request => request.Key.ToString()));
                _logger.LogInformation(
                    "策略已注册: {Uid} 交易所={Exchange} 交易对={Symbol} 周期={TimeframeSec}秒 指标数={Count}\n{Indicators}",
                    strategy.UidCode,
                    trade.Exchange,
                    trade.Symbol,
                    trade.TimeframeSec,
                    indicatorRequests.Count,
                    indicators);
            }
            else
            {
                _logger.LogInformation(
                    "策略已注册: {Uid} 交易所={Exchange} 交易对={Symbol} 周期={TimeframeSec}秒 无指标",
                    strategy.UidCode,
                    trade.Exchange,
                    trade.Symbol,
                    trade.TimeframeSec);
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
            return true;
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
            var stopwatch = Stopwatch.StartNew();
            var key = BuildIndexKey(task.Exchange, task.Symbol, task.TimeframeSec);
            _strategiesByKey.TryGetValue(key, out var strategies);
            var matchedCount = strategies?.Count ?? 0;
            var modeText = task.IsBarClose ? "close" : "update";
            var metrics = new StrategyRunMetrics();


            // _logger.LogInformation(
            //     "Strategy engine task ({Mode}): {Exchange} {Symbol} {Timeframe} time={Time} matched={Count}",
            //     modeText,
            //     task.Exchange,
            //     task.Symbol,
            //     task.Timeframe,
            //     FormatTimestamp(task.CandleTimestamp),
            //     matchedCount);

            if (strategies == null || strategies.IsEmpty)
            {
                stopwatch.Stop();
                LogStrategyRun(task, matchedCount, metrics, stopwatch.ElapsedMilliseconds);
                return;
            }

            UpdateIndicatorsBeforeExecute(strategies.Values, task);

            var options = new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism };
            Parallel.ForEach(strategies.Values, options, strategy =>
            {
                if (!IsRunnableState(strategy.State))
                {
                    Interlocked.Increment(ref metrics.SkippedCount);
                    return;
                }

                var context = new StrategyExecutionContext(strategy, task, _valueResolver, _actionExecutor);
                // 运行时间门禁：禁止时段按策略策略处理入口/出口
                var runtimeGate = ResolveRuntimeGate(strategy, context.CurrentTime);
                if (!runtimeGate.AllowEntry && !runtimeGate.AllowExit)
                {
                    Interlocked.Increment(ref metrics.SkippedCount);
                    return;
                }

                ExecuteLogic(context, runtimeGate, metrics);
                metrics.AddExecutedStrategy(strategy.UidCode);
                Interlocked.Increment(ref metrics.ExecutedCount);
            });

            stopwatch.Stop();
            LogStrategyRun(task, matchedCount, metrics, stopwatch.ElapsedMilliseconds);
        }

        private void ExecuteLogic(
            StrategyExecutionContext context,
            StrategyRuntimeGate runtimeGate,
            StrategyRunMetrics? metrics)
        {
            var logic = context.StrategyConfig.Logic;
            if (logic == null)
            {
                return;
            }

            if (runtimeGate.AllowExit)
            {
                ExecuteBranch(context, logic.Exit.Long, "Exit.Long", metrics);
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

            if (!EvaluateEntryFilters(context, logic.Entry.Long, "Entry.Long", metrics))
            {
                return;
            }

            ExecuteBranch(context, logic.Entry.Long, "Entry.Long", metrics);
            //ExecuteBranch(context, logic.Entry.Short, "Entry.Short");
        }

        private bool EvaluateEntryFilters(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage,
            StrategyRunMetrics? metrics)
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

            PrecomputeRequiredConditions(context, filters, metrics);
            var results = new List<ConditionEvaluationResult>();
            var stageLabel = $"{stage}.Filter";
            var pass = EvaluateChecks(context, filters, results, stageLabel, metrics);
            if (!pass && metrics != null)
            {
                Interlocked.Increment(ref metrics.SkippedCount);
            }

            if (!pass)
            {
                _logger.LogDebug(
                    "筛选器未通过: {Uid} {Stage} 时间={Time}",
                    context.Strategy.UidCode,
                    stageLabel,
                    FormatTimestamp(context.Task.CandleTimestamp));
            }

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
            StrategyRunMetrics? metrics)
        {
            if (branch == null || !branch.Enabled)
            {
                return;
            }

            var containers = branch.Containers ?? new List<ConditionContainer>();
            var passCount = 0;
            var aggregatedResults = new List<ConditionEvaluationResult>();
            var debugEnabled = _logger.IsEnabled(LogLevel.Debug);

            PrecomputeRequiredConditions(context, containers, metrics);

            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (container == null)
                {
                    continue;
                }

                var checkResults = new List<ConditionEvaluationResult>();
                var stageLabel = $"{stage}[{i}]";
                _logger.LogDebug(
                    "策略检查开始: {Uid} {Stage} 时间={Time}",
                    context.Strategy.UidCode,
                    stageLabel,
                    FormatTimestamp(context.Task.CandleTimestamp));

                if (!EvaluateChecks(context, container.Checks, checkResults, stageLabel, metrics))
                {
                    _logger.LogDebug(
                        "策略检查失败: {Uid} {Stage} 时间={Time}",
                        context.Strategy.UidCode,
                        stageLabel,
                        FormatTimestamp(context.Task.CandleTimestamp));
                    continue;
                }

                passCount++;
                aggregatedResults.AddRange(checkResults);

                _logger.LogDebug(
                    "策略检查通过: {Uid} {Stage} 时间={Time}",
                    context.Strategy.UidCode,
                    stageLabel,
                    FormatTimestamp(context.Task.CandleTimestamp));
            }

            if (passCount < branch.MinPassConditionContainer)
            {
                _logger.LogDebug(
                    "策略容器数量不足: {Uid} {Stage} 需要={Need} 通过={Pass}",
                    context.Strategy.UidCode,
                    stage,
                    branch.MinPassConditionContainer,
                    passCount);
                return;
            }

            _logger.LogDebug(
                "策略检查通过，执行动作: {Uid} {Stage} 时间={Time}",
                context.Strategy.UidCode,
                stage,
                FormatTimestamp(context.Task.CandleTimestamp));

            ExecuteActions(context, branch.OnPass, aggregatedResults, stage, metrics);
        }

        private void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            IReadOnlyList<ConditionContainer> containers,
            StrategyRunMetrics? metrics)
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
                        metrics?.IncrementConditionEval();
                    }
                }
            }
        }

        private void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            ConditionGroupSet? checks,
            StrategyRunMetrics? metrics)
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
                    metrics?.IncrementConditionEval();
                }
            }
        }

        private bool EvaluateChecks(
            StrategyExecutionContext context,
            ConditionGroupSet checks,
            List<ConditionEvaluationResult> results,
            string stage,
            StrategyRunMetrics? metrics)
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

                // 重要：优先级拆分仅调整“评估顺序”，不改变 Required/Optional 语义。
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
                    var result = _conditionEvaluator.Evaluate(context, condition);
                    metrics?.IncrementConditionEval();
                    results.Add(result);
                    _logger.LogDebug(
                        "条件检查: {Uid} {Stage} 组{Group} 方法={Method} 必需={Required} 结果={Result} 消息={Msg}",
                        context.Strategy.UidCode,
                        stage,
                        groupIndex,
                        condition.Method,
                        condition.Required,
                        result.Success,
                        result.Message);

                    // 测试用：记录检查过程（后续会删除）
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await LogCheckResultAsync(context, stage, groupIndex, condition, result, isRequired, ct: default).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "记录测试检查日志失败（忽略）");
                        }
                    });

                    if (isRequired && !result.Success)
                    {
                        _logger.LogDebug(
                            "必需条件失败: {Uid} {Stage} 组{Group} 方法={Method}",
                            context.Strategy.UidCode,
                            stage,
                            groupIndex,
                            condition.Method);
                        requiredFailed = true;
                        return false;
                    }

                    if (!isRequired && result.Success)
                    {
                        optionalPassCount++;
                    }

                    return result.Success;
                }

                // P0：Cross/穿透类优先评估
                foreach (var condition in crossRequired)
                {
                    EvaluateCondition(condition, isRequired: true);
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
                    EvaluateCondition(condition, isRequired: false);
                }

                // P1?Required ??
                foreach (var condition in required)
                {
                    EvaluateCondition(condition, isRequired: true);
                    if (requiredFailed)
                    {
                        break;
                    }
                }

                if (requiredFailed)
                {
                    continue;
                }

                // P2：Optional 条件（达到最小通过数后提前结束）
                if (optionalPassCount < group.MinPassConditions)
                {
                    foreach (var condition in optional)
                    {
                        EvaluateCondition(condition, isRequired: false);
                        if (optionalPassCount >= group.MinPassConditions)
                        {
                            break;
                        }
                    }
                }

                var pass = optionalPassCount >= group.MinPassConditions;
                if (!pass)
                {
                    _logger.LogDebug(
                        "条件组数量不足: {Uid} {Stage} 组{Group} 需要{Need} 通过={Pass}",
                        context.Strategy.UidCode,
                        stage,
                        groupIndex,
                        group.MinPassConditions,
                        optionalPassCount);
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
            string stage,
            StrategyRunMetrics? metrics)
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

                metrics?.IncrementActionExec();
                hasEnabled = true;
                var result = ActionMethodRegistry.Run(context, action, triggerResults);
                _logger.LogDebug(
                    "动作执行: {Uid} {Stage} 方法={Method} 必需={Required} 结果={Result} 消息={Msg}",
                    context.Strategy.UidCode,
                    stage,
                    action.Method,
                    action.Required,
                    result.Success,
                    result.Message.ToString());

                if (result.Success && IsOpenAction(action))
                {
                    metrics?.AddOpenTaskStrategy(context.Strategy.UidCode);
                    metrics?.IncrementOpenTask();
                }

                if (action.Required && !result.Success)
                {
                    _logger.LogDebug(
                        "动作失败（必需）: {Uid} {Stage} 方法={Method}",
                        context.Strategy.UidCode,
                        stage,
                        action.Method);
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
                _logger.LogDebug(
                    "动作最小通过数未达到: {Uid} {Stage} 需要={Need} 通过={Pass}",
                    context.Strategy.UidCode,
                    stage,
                    actions.MinPassConditions,
                    optionalSuccessCount);
            }
        }

        private static string BuildIndexKey(string exchange, string symbol, int timeframeSec)
        {
            return $"{exchange}|{symbol}|{timeframeSec}";
        }

        private void LogStrategyRun(MarketDataTask task, int matchedCount, StrategyRunMetrics metrics, long durationMs)
        {
            var executedIds = BuildDelimitedIds(metrics.ExecutedStrategyIds.Keys);
            var openIds = BuildDelimitedIds(metrics.OpenTaskStrategyIds.Keys);
            var modeText = task.IsBarClose ? "close" : "update";

            if (metrics.ExecutedCount > 0)
            {
                _logger.LogInformation(
                    "[策略运行] 交易所={Exchange} 交易对={Symbol} 周期={Timeframe} 模式={Mode} 时间={Time} 耗时={Duration}毫秒 匹配={Matched} 执行={Executed} 跳过={Skipped} 条件={Conditions} 动作={Actions} 开放任务={OpenTasks} 执行ID={ExecIds} 开放ID={OpenIds}",
                    task.Exchange,
                    task.Symbol,
                    task.Timeframe,
                    modeText,
                    FormatTimestampIso(task.CandleTimestamp),
                    durationMs,
                    matchedCount,
                    metrics.ExecutedCount,
                    metrics.SkippedCount,
                    metrics.ConditionEvalCount,
                    metrics.ActionExecCount,
                    metrics.OpenTaskCount,
                    executedIds,
                    openIds);
            }

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
                ExecutedCount = metrics.ExecutedCount,
                SkippedCount = metrics.SkippedCount,
                ConditionEvalCount = metrics.ConditionEvalCount,
                ActionExecCount = metrics.ActionExecCount,
                OpenTaskCount = metrics.OpenTaskCount,
                ExecutedStrategyIds = string.IsNullOrWhiteSpace(executedIds) ? null : executedIds,
                OpenTaskStrategyIds = string.IsNullOrWhiteSpace(openIds) ? null : openIds,
                ExtraJson = null,
                EngineInstance = _engineInstanceId
            };

            if (!_runLogQueue.TryEnqueue(log))
            {
                _logger.LogWarning("策略运行日志入队失败");
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

        private sealed class StrategyRunMetrics
        {
            public readonly ConcurrentDictionary<string, byte> ExecutedStrategyIds = new(StringComparer.Ordinal);
            public readonly ConcurrentDictionary<string, byte> OpenTaskStrategyIds = new(StringComparer.Ordinal);
            public int ExecutedCount;
            public int SkippedCount;
            public int ConditionEvalCount;
            public int ActionExecCount;
            public int OpenTaskCount;

            public void AddExecutedStrategy(string uid)
            {
                if (string.IsNullOrWhiteSpace(uid))
                {
                    return;
                }

                ExecutedStrategyIds.TryAdd(uid, 0);
            }

            public void AddOpenTaskStrategy(string uid)
            {
                if (string.IsNullOrWhiteSpace(uid))
                {
                    return;
                }

                OpenTaskStrategyIds.TryAdd(uid, 0);
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

        private void UpdateIndicatorsBeforeExecute(IEnumerable<StrategyModel> strategies, MarketDataTask task)
        {
            if (_indicatorEngine == null)
            {
                return;
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
                return;
            }

            var normalizedTask = new MarketDataTask(exchange, symbol, timeframe, task.CandleTimestamp, task.IsBarClose);
            var indicatorTask = new IndicatorTask(normalizedTask, requestMap.Values.ToList());
            var result = _indicatorEngine.ProcessTaskNow(indicatorTask);
            _logger.LogDebug(
                "指标刷新: {Exchange} {Symbol} {Timeframe} 时间={Time} 成功={Success}/{Total}",
                normalizedTask.Exchange,
                normalizedTask.Symbol,
                normalizedTask.Timeframe,
                FormatTimestamp(normalizedTask.CandleTimestamp),
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

        /// <summary>
        /// 测试用：记录策略检查结果到数据库（后续会删除）
        /// </summary>
        private async Task LogCheckResultAsync(
            StrategyExecutionContext context,
            string stage,
            int groupIndex,
            StrategyMethod condition,
            ConditionEvaluationResult result,
            bool isRequired,
            CancellationToken ct)
        {
            if (_testCheckLogRepository == null)
            {
                return; // 非测试环境，跳过记录
            }

            try
            {
                var trade = context.StrategyConfig?.Trade;
                if (trade == null)
                {
                    return;
                }

                // 构建条件键
                var conditionKey = ConditionKeyBuilder.BuildKey(trade, condition);

                // 构建检查过程详情（JSON格式）
                var checkProcess = new
                {
                    stage = stage,
                    groupIndex = groupIndex,
                    conditionKey = conditionKey.Text,
                    conditionKeyId = conditionKey.Id,
                    method = condition.Method,
                    isRequired = isRequired,
                    enabled = condition.Enabled,
                    result = new
                    {
                        success = result.Success,
                        message = result.Message,
                        key = result.Key
                    },
                    strategy = new
                    {
                        uidCode = context.Strategy.UidCode,
                        state = context.Strategy.State.ToString()
                    },
                    task = new
                    {
                        exchange = context.Task.Exchange,
                        symbol = context.Task.Symbol,
                        timeframe = context.Task.Timeframe,
                        candleTimestamp = context.Task.CandleTimestamp,
                        isBarClose = context.Task.IsBarClose
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var checkProcessJson = JsonSerializer.Serialize(checkProcess, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var log = new TestStrategyCheckLog
                {
                    Uid = context.Strategy.CreatorUserId,
                    UsId = context.Strategy.Id,
                    Exchange = trade.Exchange,
                    Symbol = trade.Symbol,
                    Timeframe = trade.TimeframeSec.ToString(),
                    CandleTimestamp = context.Task.CandleTimestamp,
                    Stage = stage,
                    GroupIndex = groupIndex,
                    ConditionKey = conditionKey.Id,
                    Method = condition.Method,
                    IsRequired = isRequired,
                    Success = result.Success,
                    Message = result.Message ?? string.Empty,
                    CheckProcess = checkProcessJson,
                    CreatedAt = DateTime.UtcNow
                };

                await _testCheckLogRepository.InsertAsync(log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 记录失败不影响主流程，只记录警告日志
                _logger.LogWarning(ex, "记录测试检查日志失败: uid={Uid} usId={UsId} stage={Stage}", 
                    context.Strategy.CreatorUserId, context.Strategy.Id, stage);
            }
        }
    }
}
