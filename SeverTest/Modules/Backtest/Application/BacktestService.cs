using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Application
{
    public sealed class BacktestService : BaseService
    {
        private readonly BacktestRunner _runner;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly DatabaseService _db;
        private readonly ContractDetailsCacheService _contractCache;
        private readonly BacktestProgressPushService _progressPushService;

        public BacktestService(
            BacktestRunner runner,
            StrategyJsonLoader strategyLoader,
            DatabaseService db,
            ContractDetailsCacheService contractCache,
            BacktestProgressPushService progressPushService,
            ILogger<BacktestService> logger)
            : base(logger)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _contractCache = contractCache ?? throw new ArgumentNullException(nameof(contractCache));
            _progressPushService = progressPushService ?? throw new ArgumentNullException(nameof(progressPushService));
        }

        public async Task<BacktestRunResult> RunAsync(
            BacktestRunRequest request,
            string? reqId,
            long? userId,
            long? taskId,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var configJson = request.ConfigJson;
            if (string.IsNullOrWhiteSpace(configJson))
            {
                if (!request.UsId.HasValue || request.UsId.Value <= 0)
                {
                    throw new InvalidOperationException("缺少策略配置或策略实例ID");
                }

                configJson = await LoadConfigJsonAsync(request.UsId.Value, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(configJson))
                {
                    throw new InvalidOperationException("未找到策略配置");
                }
            }

            var config = _strategyLoader.ParseConfig(configJson);
            if (config == null)
            {
                throw new InvalidOperationException("策略配置解析失败");
            }

            // 确保合约详情缓存已加载，便于处理下单数量精度与合约乘数
            await _contractCache.InitializeAsync(ct).ConfigureAwait(false);

            var progressContext = new BacktestProgressContext
            {
                ReqId = reqId,
                UserId = userId,
                TaskId = taskId
            };

            try
            {
                return await _runner.RunAsync(request, config, progressContext, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await _progressPushService
                    .PublishStageAsync(
                        progressContext,
                        "failed",
                        "回测失败",
                        $"回测执行失败: {ex.Message}",
                        null,
                        null,
                        null,
                        true,
                        ct)
                    .ConfigureAwait(false);
                throw;
            }
        }

        private async Task<string?> LoadConfigJsonAsync(long usId, CancellationToken ct)
        {
            await using var connection = await _db.GetConnectionAsync().ConfigureAwait(false);
            var cmd = new MySqlCommand(@"
SELECT sv.config_json
FROM user_strategy us
JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.us_id = @us_id
LIMIT 1
", connection);
            cmd.Parameters.AddWithValue("@us_id", usId);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result == null || result == DBNull.Value)
            {
                return null;
            }

            return Convert.ToString(result);
        }
    }
}
