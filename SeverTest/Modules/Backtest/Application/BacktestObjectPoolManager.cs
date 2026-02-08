using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ccxt;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测对象池管理器：
    /// - 启动时按配置预热；
    /// - 运行中池空自动扩容；
    /// - 统一管理回测热点对象的租借与归还。
    /// </summary>
    public sealed class BacktestObjectPoolManager
    {
        private readonly ILogger<BacktestObjectPoolManager> _logger;
        private readonly SimpleObjectPool<Dictionary<string, OHLCV>> _barDictionaryPool;
        private readonly SimpleObjectPool<List<ConditionEvaluationResult>> _conditionResultListPool;
        private readonly SimpleObjectPool<List<StrategyMethod>> _strategyMethodListPool;
        private readonly SimpleObjectPool<HashSet<long>> _timestampSetPool;
        private readonly SimpleObjectPool<IndicatorTask> _indicatorTaskPool;
        private readonly SimpleObjectPool<StrategyExecutionContext> _strategyContextPool;

        public BacktestObjectPoolManager(ILogger<BacktestObjectPoolManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _barDictionaryPool = new SimpleObjectPool<Dictionary<string, OHLCV>>(
                () => new Dictionary<string, OHLCV>(StringComparer.OrdinalIgnoreCase),
                dict => dict.Clear());
            _conditionResultListPool = new SimpleObjectPool<List<ConditionEvaluationResult>>(
                () => new List<ConditionEvaluationResult>(32),
                list => list.Clear());
            _strategyMethodListPool = new SimpleObjectPool<List<StrategyMethod>>(
                () => new List<StrategyMethod>(16),
                list => list.Clear());
            _timestampSetPool = new SimpleObjectPool<HashSet<long>>(
                () => new HashSet<long>(),
                set => set.Clear());
            _indicatorTaskPool = new SimpleObjectPool<IndicatorTask>(
                () => new IndicatorTask(),
                task => task.Reset(default, null));
            _strategyContextPool = new SimpleObjectPool<StrategyExecutionContext>(
                () => new StrategyExecutionContext(),
                context => context.Clear());
        }

        public void Warmup(
            int barDictionaryCount,
            int conditionResultListCount,
            int strategyMethodListCount,
            int timestampSetCount,
            int indicatorTaskCount,
            int strategyContextCount)
        {
            _barDictionaryPool.Warmup(Math.Max(0, barDictionaryCount));
            _conditionResultListPool.Warmup(Math.Max(0, conditionResultListCount));
            _strategyMethodListPool.Warmup(Math.Max(0, strategyMethodListCount));
            _timestampSetPool.Warmup(Math.Max(0, timestampSetCount));
            _indicatorTaskPool.Warmup(Math.Max(0, indicatorTaskCount));
            _strategyContextPool.Warmup(Math.Max(0, strategyContextCount));

            _logger.LogInformation(
                "回测对象池预热完成：K线字典={BarDict} 条件结果List={ConditionResult} 条件方法List={StrategyMethod} 时间戳HashSet={TimestampSet} 指标任务={IndicatorTask} 策略上下文={StrategyContext}",
                _barDictionaryPool.AvailableCount,
                _conditionResultListPool.AvailableCount,
                _strategyMethodListPool.AvailableCount,
                _timestampSetPool.AvailableCount,
                _indicatorTaskPool.AvailableCount,
                _strategyContextPool.AvailableCount);
        }

        public Dictionary<string, OHLCV> RentBarDictionary() => _barDictionaryPool.Rent();

        public void ReturnBarDictionary(Dictionary<string, OHLCV>? dictionary)
        {
            if (dictionary == null)
            {
                return;
            }

            _barDictionaryPool.Return(dictionary);
        }

        public List<ConditionEvaluationResult> RentConditionResultList() => _conditionResultListPool.Rent();

        public void ReturnConditionResultList(List<ConditionEvaluationResult>? list)
        {
            if (list == null)
            {
                return;
            }

            _conditionResultListPool.Return(list);
        }

        public List<StrategyMethod> RentStrategyMethodList() => _strategyMethodListPool.Rent();

        public void ReturnStrategyMethodList(List<StrategyMethod>? list)
        {
            if (list == null)
            {
                return;
            }

            _strategyMethodListPool.Return(list);
        }

        public HashSet<long> RentTimestampSet() => _timestampSetPool.Rent();

        public void ReturnTimestampSet(HashSet<long>? set)
        {
            if (set == null)
            {
                return;
            }

            _timestampSetPool.Return(set);
        }

        public IndicatorTask RentIndicatorTask(MarketDataTask marketTask, IReadOnlyList<IndicatorRequest>? requests)
        {
            var task = _indicatorTaskPool.Rent();
            task.Reset(marketTask, requests);
            return task;
        }

        public void ReturnIndicatorTask(IndicatorTask? task)
        {
            if (task == null)
            {
                return;
            }

            _indicatorTaskPool.Return(task);
        }

        public StrategyExecutionContext RentStrategyExecutionContext(
            StrategyModel strategy,
            MarketDataTask task,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null,
            DateTimeOffset? currentTime = null)
        {
            var context = _strategyContextPool.Rent();
            context.Reset(strategy, task, valueResolver, actionExecutor, currentTime);
            return context;
        }

        public void ReturnStrategyExecutionContext(StrategyExecutionContext? context)
        {
            if (context == null)
            {
                return;
            }

            _strategyContextPool.Return(context);
        }

        private sealed class SimpleObjectPool<T> where T : class
        {
            private readonly ConcurrentBag<T> _items = new();
            private readonly Func<T> _factory;
            private readonly Action<T>? _reset;
            private int _createdCount;

            public SimpleObjectPool(Func<T> factory, Action<T>? reset = null)
            {
                _factory = factory ?? throw new ArgumentNullException(nameof(factory));
                _reset = reset;
            }

            public int AvailableCount => _items.Count;
            public int CreatedCount => Volatile.Read(ref _createdCount);

            public void Warmup(int targetCount)
            {
                if (targetCount <= 0)
                {
                    return;
                }

                var needCreate = targetCount - _items.Count;
                for (var i = 0; i < needCreate; i++)
                {
                    _items.Add(CreateItem());
                }
            }

            public T Rent()
            {
                if (_items.TryTake(out var item))
                {
                    return item;
                }

                return CreateItem();
            }

            public void Return(T item)
            {
                _reset?.Invoke(item);
                _items.Add(item);
            }

            private T CreateItem()
            {
                Interlocked.Increment(ref _createdCount);
                return _factory();
            }
        }
    }
}
