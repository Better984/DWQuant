using System.Collections.Concurrent;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using StrategyModel = ServerTest.Models.Strategy.Strategy;
using ServerTest.Models;

namespace ServerTest.Modules.StrategyEngine.Infrastructure
{
    /// <summary>
    /// 实盘对象池管理器：
    /// 池化 RealTimeStrategyEngine 热路径上的高频分配对象，
    /// 消除 GC 压力，结构与回测 BacktestObjectPoolManager 对齐。
    /// </summary>
    public sealed class LiveTradingObjectPoolManager
    {
        private readonly SimpleObjectPool<StrategyExecutionContext> _contextPool;
        private readonly SimpleObjectPool<List<ConditionEvaluationResult>> _resultListPool;
        private readonly SimpleObjectPool<List<StrategyMethod>> _methodListPool;

        public LiveTradingObjectPoolManager()
        {
            _contextPool = new SimpleObjectPool<StrategyExecutionContext>(
                factory: () => new StrategyExecutionContext(),
                reset: ctx => ctx.Clear());

            _resultListPool = new SimpleObjectPool<List<ConditionEvaluationResult>>(
                factory: () => new List<ConditionEvaluationResult>(32),
                reset: list => list.Clear());

            _methodListPool = new SimpleObjectPool<List<StrategyMethod>>(
                factory: () => new List<StrategyMethod>(16),
                reset: list => list.Clear());
        }

        public void Warmup(int contextCount, int resultListCount, int methodListCount)
        {
            _contextPool.Warmup(Math.Max(0, contextCount));
            _resultListPool.Warmup(Math.Max(0, resultListCount));
            _methodListPool.Warmup(Math.Max(0, methodListCount));
        }

        public int ContextPoolCreatedCount => _contextPool.CreatedCount;
        public int ResultListPoolCreatedCount => _resultListPool.CreatedCount;
        public int MethodListPoolCreatedCount => _methodListPool.CreatedCount;

        public StrategyExecutionContext RentContext(
            StrategyModel strategy,
            MarketDataTask task,
            IStrategyValueResolver? valueResolver = null,
            IStrategyActionExecutor? actionExecutor = null)
        {
            var context = _contextPool.Rent();
            context.Reset(strategy, task, valueResolver, actionExecutor);
            return context;
        }

        public void ReturnContext(StrategyExecutionContext? context)
        {
            if (context != null) _contextPool.Return(context);
        }

        public List<ConditionEvaluationResult> RentResultList() => _resultListPool.Rent();

        public void ReturnResultList(List<ConditionEvaluationResult>? list)
        {
            if (list != null) _resultListPool.Return(list);
        }

        public List<StrategyMethod> RentMethodList() => _methodListPool.Rent();

        public void ReturnMethodList(List<StrategyMethod>? list)
        {
            if (list != null) _methodListPool.Return(list);
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
                var needCreate = targetCount - _items.Count;
                for (var i = 0; i < needCreate; i++)
                {
                    _items.Add(CreateItem());
                }
            }

            public T Rent()
            {
                return _items.TryTake(out var item) ? item : CreateItem();
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
