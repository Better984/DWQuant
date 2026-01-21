using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Strategy;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Services
{
    public sealed class RealTimeStrategyEngine
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly ILogger<RealTimeStrategyEngine> _logger;
        private readonly IStrategyValueResolver _valueResolver;
        private readonly IStrategyActionExecutor? _actionExecutor;
        private readonly int _maxParallelism;
        private readonly IndicatorEngine? _indicatorEngine;
        private readonly ConditionEvaluator _conditionEvaluator;
        private readonly ConditionUsageTracker _conditionUsageTracker;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StrategyModel>> _strategiesByKey = new();
        private readonly ConcurrentDictionary<string, string> _strategyKeyByUid = new();
        private readonly ConcurrentDictionary<string, List<IndicatorRequest>> _indicatorRequestsByStrategy = new();

        public RealTimeStrategyEngine(
            MarketDataEngine marketDataEngine,
            ILogger<RealTimeStrategyEngine> logger,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null,
            IndicatorEngine? indicatorEngine = null,
            ConditionEvaluator? conditionEvaluator = null,
            ConditionUsageTracker? conditionUsageTracker = null,
            int? maxParallelism = null)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _valueResolver = valueResolver ?? NoopStrategyValueResolver.Instance;
            _actionExecutor = actionExecutor;
            _indicatorEngine = indicatorEngine;
            _conditionEvaluator = conditionEvaluator ?? throw new ArgumentNullException(nameof(conditionEvaluator));
            _conditionUsageTracker = conditionUsageTracker ?? throw new ArgumentNullException(nameof(conditionUsageTracker));
            _maxParallelism = maxParallelism ?? Environment.ProcessorCount;
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
                _logger.LogWarning("Strategy {Uid} missing trade config", strategy.UidCode);
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

            if (indicatorRequests.Count > 0)
            {
                var indicators = string.Join("\n", indicatorRequests.Select(request => request.Key.ToString()));
                _logger.LogInformation(
                    "Strategy registered: {Uid} exchange={Exchange} symbol={Symbol} timeframe={TimeframeSec}s indicators={Count}\n{Indicators}",
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
                    "Strategy registered: {Uid} exchange={Exchange} symbol={Symbol} timeframe={TimeframeSec}s no indicators",
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
            _conditionUsageTracker.RemoveStrategy(uidCode);
            return true;
        }

        public bool TryProcessNextTask()
        {
            if (!_marketDataEngine.TryDequeueMarketTask(out var task))
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
                    task = await _marketDataEngine.ReadMarketTaskAsync(cancellationToken).ConfigureAwait(false);
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
            var key = BuildIndexKey(task.Exchange, task.Symbol, task.TimeframeSec);
            _strategiesByKey.TryGetValue(key, out var strategies);
            var matchedCount = strategies?.Count ?? 0;
            var modeText = task.IsBarClose ? "close" : "update";

            _logger.LogInformation(
                "Strategy engine task ({Mode}): {Exchange} {Symbol} {Timeframe} time={Time} matched={Count}",
                modeText,
                task.Exchange,
                task.Symbol,
                task.Timeframe,
                FormatTimestamp(task.CandleTimestamp),
                matchedCount);

            if (strategies == null || strategies.IsEmpty)
            {
                return;
            }

            UpdateIndicatorsBeforeExecute(strategies.Values, task);

            var options = new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism };
            var executed = 0;
            Parallel.ForEach(strategies.Values, options, strategy =>
            {
                if (!IsRunnableState(strategy.State))
                {
                    return;
                }

                var context = new StrategyExecutionContext(strategy, task, _valueResolver, _actionExecutor);
                ExecuteLogic(context);
                Interlocked.Increment(ref executed);
            });

            if (executed > 0)
            {
                _logger.LogInformation(
                    "Strategy engine completed: {Exchange} {Symbol} {Timeframe} time={Time} executed={Count}",
                    task.Exchange,
                    task.Symbol,
                    task.Timeframe,
                    FormatTimestamp(task.CandleTimestamp),
                    executed);
            }
        }

        private void ExecuteLogic(StrategyExecutionContext context)
        {
            var logic = context.StrategyConfig.Logic;
            if (logic == null)
            {
                return;
            }

            ExecuteBranch(context, logic.Exit.Long, "Exit.Long");
            //ExecuteBranch(context, logic.Exit.Short, "Exit.Short");
            ExecuteBranch(context, logic.Entry.Long, "Entry.Long");
            //ExecuteBranch(context, logic.Entry.Short, "Entry.Short");
        }

        private void ExecuteBranch(
            StrategyExecutionContext context,
            StrategyLogicBranch branch,
            string stage)
        {
            if (branch == null || !branch.Enabled)
            {
                return;
            }

            var containers = branch.Containers ?? new List<ConditionContainer>();
            var passCount = 0;
            var aggregatedResults = new List<ConditionEvaluationResult>();

            PrecomputeRequiredConditions(context, containers);

            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (container == null)
                {
                    continue;
                }

                var checkResults = new List<ConditionEvaluationResult>();
                var stageLabel = $"{stage}[{i}]";
                _logger.LogInformation(
                    "Strategy checks start: {Uid} {Stage} time={Time}",
                    context.Strategy.UidCode,
                    stageLabel,
                    FormatTimestamp(context.Task.CandleTimestamp));

                if (!EvaluateChecks(context, container.Checks, checkResults, stageLabel))
                {
                    _logger.LogInformation(
                        "Strategy checks failed: {Uid} {Stage} time={Time}",
                        context.Strategy.UidCode,
                        stageLabel,
                        FormatTimestamp(context.Task.CandleTimestamp));
                    continue;
                }

                passCount++;
                aggregatedResults.AddRange(checkResults);

                _logger.LogInformation(
                    "Strategy checks passed: {Uid} {Stage} time={Time}",
                    context.Strategy.UidCode,
                    stageLabel,
                    FormatTimestamp(context.Task.CandleTimestamp));
            }

            if (passCount < branch.MinPassConditionContainer)
            {
                _logger.LogInformation(
                    "Strategy containers not enough: {Uid} {Stage} Need={Need} Pass={Pass}",
                    context.Strategy.UidCode,
                    stage,
                    branch.MinPassConditionContainer,
                    passCount);
                return;
            }

            _logger.LogInformation(
                "Strategy checks passed, executing actions: {Uid} {Stage} time={Time}",
                context.Strategy.UidCode,
                stage,
                FormatTimestamp(context.Task.CandleTimestamp));

            ExecuteActions(context, branch.OnPass, aggregatedResults, stage);
        }

        private void PrecomputeRequiredConditions(
            StrategyExecutionContext context,
            IReadOnlyList<ConditionContainer> containers)
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
                    }
                }
            }
        }

        private bool EvaluateChecks(
            StrategyExecutionContext context,
            ConditionGroupSet checks,
            List<ConditionEvaluationResult> results,
            string stage)
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

                var hasEnabled = false;
                var requiredFailed = false;
                foreach (var condition in group.Conditions ?? new List<StrategyMethod>())
                {
                    if (condition == null || !condition.Enabled)
                    {
                        continue;
                    }

                    hasEnabled = true;
                    if (!condition.Required)
                    {
                        continue;
                    }

                    var result = _conditionEvaluator.Evaluate(context, condition);
                    results.Add(result);
                    _logger.LogInformation(
                        "Condition check: {Uid} {Stage} Group={Group} Method={Method} Required={Required} Result={Result} Msg={Msg}",
                        context.Strategy.UidCode,
                        stage,
                        groupIndex,
                        condition.Method,
                        condition.Required,
                        result.Success,
                        result.Message);

                    if (!result.Success)
                    {
                        _logger.LogInformation(
                            "Condition required failed: {Uid} {Stage} Group={Group} Method={Method}",
                            context.Strategy.UidCode,
                            stage,
                            groupIndex,
                            condition.Method);
                        requiredFailed = true;
                        break;
                    }
                }

                if (requiredFailed)
                {
                    continue;
                }

                if (!hasEnabled)
                {
                    if (group.MinPassConditions <= 0)
                    {
                        passGroups++;
                    }
                    continue;
                }

                var optionalPassCount = 0;
                foreach (var condition in group.Conditions ?? new List<StrategyMethod>())
                {
                    if (condition == null || !condition.Enabled || condition.Required)
                    {
                        continue;
                    }

                    var result = _conditionEvaluator.Evaluate(context, condition);
                    results.Add(result);
                    _logger.LogInformation(
                        "Condition check: {Uid} {Stage} Group={Group} Method={Method} Required={Required} Result={Result} Msg={Msg}",
                        context.Strategy.UidCode,
                        stage,
                        groupIndex,
                        condition.Method,
                        condition.Required,
                        result.Success,
                        result.Message);

                    if (result.Success)
                    {
                        optionalPassCount++;
                    }
                }

                var pass = optionalPassCount >= group.MinPassConditions;
                if (!pass)
                {
                    _logger.LogInformation(
                        "Condition group not enough: {Uid} {Stage} Group={Group} Need={Need} Pass={Pass}",
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
                _logger.LogInformation(
                    "Action execute: {Uid} {Stage} Method={Method} Required={Required} Result={Result} Msg={Msg}",
                    context.Strategy.UidCode,
                    stage,
                    action.Method,
                    action.Required,
                    result.Success,
                    result.Message.ToString());

                if (action.Required && !result.Success)
                {
                    _logger.LogInformation(
                        "Action failed (Required): {Uid} {Stage} Method={Method}",
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
                _logger.LogInformation(
                    "Action min pass not reached: {Uid} {Stage} Need={Need} Pass={Pass}",
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

        private static bool IsRunnableState(StrategyState state)
        {
            return state == StrategyState.Running || state == StrategyState.Testing;
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
            _logger.LogInformation(
                "Indicator refresh: {Exchange} {Symbol} {Timeframe} time={Time} success={Success}/{Total}",
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
