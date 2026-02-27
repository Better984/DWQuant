using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.StrategyEngine.Application;
using System.Threading.Channels;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 按 exchange|symbol|timeframe 分片消费行情任务。
    /// 同键串行、异键并行，避免单 worker 瓶颈。
    /// </summary>
    internal sealed class KeyShardedStrategyConsumer
    {
        private readonly RealTimeStrategyEngine _engine;
        private readonly int _shardCount;
        private readonly Channel<MarketDataTask>[] _shardChannels;
        private readonly ILogger _logger;

        public KeyShardedStrategyConsumer(
            RealTimeStrategyEngine engine,
            int shardCount,
            ILogger logger)
        {
            _engine = engine;
            _shardCount = Math.Max(1, shardCount);
            _logger = logger;

            _shardChannels = new Channel<MarketDataTask>[_shardCount];
            for (var i = 0; i < _shardCount; i++)
            {
                _shardChannels[i] = Channel.CreateBounded<MarketDataTask>(
                    new BoundedChannelOptions(512)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        // 使用 DropWrite，保证队列满时行为可观测，避免静默覆盖旧任务。
                        FullMode = BoundedChannelFullMode.DropWrite
                    });
            }

            _engine.AdjustInnerParallelism(_shardCount);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "键分片策略消费启动: shardCount={ShardCount}", _shardCount);

            var workers = new Task[_shardCount + 1];
            workers[0] = RouteAsync(cancellationToken);
            for (var i = 0; i < _shardCount; i++)
            {
                var shardIndex = i;
                workers[i + 1] = ConsumeShardAsync(shardIndex, cancellationToken);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        private async Task RouteAsync(CancellationToken cancellationToken)
        {
            var subscription = _engine.MarketTaskSubscription;
            while (!cancellationToken.IsCancellationRequested)
            {
                MarketDataTask task;
                try
                {
                    task = await subscription.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var shard = ComputeShard(task);
                if (!_shardChannels[shard].Writer.TryWrite(task))
                {
                    _logger.LogWarning(
                        "分片队列写入失败，丢弃新任务: shard={Shard} exchange={Exchange} symbol={Symbol} timeframe={TimeframeSec} isBarClose={IsBarClose}",
                        shard,
                        task.Exchange,
                        task.Symbol,
                        task.TimeframeSec,
                        task.IsBarClose);
                }
            }

            for (var i = 0; i < _shardCount; i++)
                _shardChannels[i].Writer.TryComplete();
        }

        private async Task ConsumeShardAsync(int shardIndex, CancellationToken cancellationToken)
        {
            var reader = _shardChannels[shardIndex].Reader;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (reader.TryRead(out var task))
                {
                    try
                    {
                        _engine.ProcessTask(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "分片策略执行异常: shard={Shard} exchange={Exchange} symbol={Symbol}",
                            shardIndex, task.Exchange, task.Symbol);
                    }
                }
            }
        }

        private int ComputeShard(MarketDataTask task)
        {
            var hash = HashCode.Combine(task.Exchange, task.Symbol, task.TimeframeSec);
            return (hash & int.MaxValue) % _shardCount;
        }
    }
}
