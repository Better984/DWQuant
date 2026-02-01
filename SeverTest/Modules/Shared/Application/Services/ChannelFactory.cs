using Microsoft.Extensions.Logging;
using ServerTest.Options;
using System.Threading.Channels;

namespace ServerTest.Services
{
    /// <summary>
    /// 通道工厂：统一创建有界/无界通道，集中处理背压策略
    /// </summary>
    internal static class ChannelFactory
    {
        public static Channel<T> Create<T>(
            QueueOptions? options,
            string queueName,
            ILogger logger,
            bool singleReader,
            bool singleWriter)
        {
            if (options == null || options.Capacity <= 0)
            {
                logger.LogInformation("队列使用无界通道: {Queue}", queueName);
                return Channel.CreateUnbounded<T>(new UnboundedChannelOptions
                {
                    SingleReader = singleReader,
                    SingleWriter = singleWriter,
                    AllowSynchronousContinuations = false
                });
            }

            var fullMode = ParseFullMode(options.FullMode);
            var boundedOptions = new BoundedChannelOptions(options.Capacity)
            {
                SingleReader = singleReader,
                SingleWriter = singleWriter,
                AllowSynchronousContinuations = false,
                FullMode = fullMode
            };

            logger.LogInformation(
                "队列使用有界通道: {Queue} 容量={Capacity} 满策略={FullMode}",
                queueName,
                boundedOptions.Capacity,
                boundedOptions.FullMode);

            return Channel.CreateBounded<T>(boundedOptions);
        }

        private static BoundedChannelFullMode ParseFullMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return BoundedChannelFullMode.DropOldest;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "wait" => BoundedChannelFullMode.Wait,
                "dropoldest" => BoundedChannelFullMode.DropOldest,
                "dropnewest" => BoundedChannelFullMode.DropNewest,
                "dropwrite" => BoundedChannelFullMode.DropWrite,
                _ => BoundedChannelFullMode.DropOldest
            };
        }
    }
}
