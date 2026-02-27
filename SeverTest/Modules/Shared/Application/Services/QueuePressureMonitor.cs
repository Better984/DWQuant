using Microsoft.Extensions.Logging;
using ServerTest.Options;

namespace ServerTest.Services
{
    /// <summary>
    /// 队列压力监控：用于轻量统计与告警限流
    /// </summary>
    internal sealed class QueuePressureMonitor
    {
        private readonly string _queueName;
        private readonly ILogger _logger;
        private readonly int _capacity;
        private readonly int _warningThreshold;
        private readonly long _warningIntervalTicks;
        private long _current;
        private long _lastWarningTicks;

        public QueuePressureMonitor(string queueName, QueueOptions? options, ILogger logger)
        {
            _queueName = queueName;
            _logger = logger;

            if (options == null || options.Capacity <= 0)
            {
                _capacity = 0;
                _warningThreshold = 0;
                _warningIntervalTicks = TimeSpan.Zero.Ticks;
                return;
            }

            _capacity = options.Capacity;
            var percent = Math.Clamp(options.WarningThresholdPercent, 1, 100);
            _warningThreshold = Math.Max(1, (int)Math.Ceiling(_capacity * percent / 100.0));
            var intervalSeconds = Math.Max(1, options.WarningIntervalSeconds);
            _warningIntervalTicks = TimeSpan.FromSeconds(intervalSeconds).Ticks;
        }

        public void OnEnqueueSuccess()
        {
            if (_capacity <= 0)
            {
                return;
            }

            var current = Interlocked.Increment(ref _current);
            if (current < _warningThreshold)
            {
                return;
            }

            TryWarn(current, "队列积压偏高");
        }

        public void OnEnqueueFailed()
        {
            if (_capacity <= 0)
            {
                return;
            }

            var current = Volatile.Read(ref _current);
            TryWarn(current, "队列已满，任务入队失败");
        }

        public void OnDequeue()
        {
            if (_capacity <= 0)
            {
                return;
            }

            var current = Interlocked.Decrement(ref _current);
            if (current < 0)
            {
                Interlocked.Exchange(ref _current, 0);
            }
        }

        private void TryWarn(long current, string reason)
        {
            if (_warningIntervalTicks <= 0)
            {
                return;
            }

            var nowTicks = DateTime.UtcNow.Ticks;
            var lastTicks = Interlocked.Read(ref _lastWarningTicks);
            if (nowTicks - lastTicks < _warningIntervalTicks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastWarningTicks, nowTicks, lastTicks) != lastTicks)
            {
                return;
            }

            _logger.LogWarning(
                "{Reason}: {Queue} 当前={Current} 容量={Capacity}",
                reason,
                _queueName,
                current,
                _capacity);
        }
    }
}
