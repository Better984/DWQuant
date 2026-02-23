using ServerTest.Modules.Indicators.Domain;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// 指标采集器接口：一个采集器可支持多个指标定义。
    /// </summary>
    public interface IIndicatorCollector
    {
        string CollectorName { get; }

        bool CanHandle(IndicatorDefinition definition);

        Task<IndicatorCollectResult> CollectAsync(IndicatorDefinition definition, string scopeKey, CancellationToken ct);
    }
}
