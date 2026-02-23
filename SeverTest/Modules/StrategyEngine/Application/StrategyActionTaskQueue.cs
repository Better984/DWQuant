using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Options;
using System.Threading.Channels;
using ServerTest.Services;
using System.Collections.Concurrent;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class StrategyActionTaskQueue
    {
        private readonly Channel<StrategyActionTask> _channel;
        private readonly QueuePressureMonitor _queueMonitor;
        private readonly ILogger<StrategyActionTaskQueue> _logger;
        private readonly ConcurrentDictionary<long, byte> _blockedStrategyMap = new();

        public StrategyActionTaskQueue(
            ILogger<StrategyActionTaskQueue> logger,
            IOptions<RuntimeQueueOptions> queueOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            // 初始化队列，启用有界通道与背压策略
            _channel = ChannelFactory.Create<StrategyActionTask>(
                options.StrategyAction,
                "StrategyActionQueue",
                _logger,
                singleReader: false,
                singleWriter: false);
            _queueMonitor = new QueuePressureMonitor("StrategyActionQueue", options.StrategyAction, _logger);
        }

        public bool TryEnqueue(StrategyActionTask task)
        {
            if (!_channel.Writer.TryWrite(task))
            {
                _queueMonitor.OnEnqueueFailed();
                return false;
            }

            _queueMonitor.OnEnqueueSuccess();
            return true;
        }

        public bool TryDequeue(out StrategyActionTask task)
        {
            if (_channel.Reader.TryRead(out var dequeued) && dequeued != null)
            {
                task = dequeued;
                _queueMonitor.OnDequeue();
                return true;
            }

            task = null!;
            return false;
        }

        /// <summary>
        /// 删除/下线策略时封禁该策略后续动作，避免残留任务继续执行。
        /// </summary>
        public void BlockStrategy(long usId)
        {
            if (usId <= 0)
            {
                return;
            }

            _blockedStrategyMap[usId] = 0;
            _logger.LogInformation("策略动作队列已封禁策略: usId={UsId}", usId);
        }

        /// <summary>
        /// 删除失败回滚时取消封禁，恢复策略动作消费。
        /// </summary>
        public void UnblockStrategy(long usId)
        {
            if (usId <= 0)
            {
                return;
            }

            if (_blockedStrategyMap.TryRemove(usId, out _))
            {
                _logger.LogInformation("策略动作队列已解除封禁: usId={UsId}", usId);
            }
        }

        public bool IsBlocked(long? usId)
        {
            return usId.HasValue && usId.Value > 0 && _blockedStrategyMap.ContainsKey(usId.Value);
        }

        public ValueTask<StrategyActionTask> ReadAsync(CancellationToken cancellationToken)
        {
            return ReadAsyncInternal(cancellationToken);
        }

        public IAsyncEnumerable<StrategyActionTask> ReadAllAsync(CancellationToken cancellationToken)
        {
            return ReadAllAsyncInternal(cancellationToken);
        }

        private async ValueTask<StrategyActionTask> ReadAsyncInternal(CancellationToken cancellationToken)
        {
            while (true)
            {
                var task = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                _queueMonitor.OnDequeue();
                if (IsBlocked(task.UsId))
                {
                    _logger.LogInformation("已丢弃封禁策略动作: uid={Uid} usId={UsId} method={Method}",
                        task.Uid, task.UsId, task.Method);
                    continue;
                }

                return task;
            }
        }

        private async IAsyncEnumerable<StrategyActionTask> ReadAllAsyncInternal(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var task in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _queueMonitor.OnDequeue();
                if (IsBlocked(task.UsId))
                {
                    _logger.LogInformation("已丢弃封禁策略动作: uid={Uid} usId={UsId} method={Method}",
                        task.Uid, task.UsId, task.Method);
                    continue;
                }

                yield return task;
            }
        }
    }
}
