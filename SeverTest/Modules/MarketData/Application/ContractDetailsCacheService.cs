using ccxt;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Models;
using ServerTest.Services;
using System.Collections.Concurrent;

namespace ServerTest.Modules.MarketData.Application
{
    /// <summary>
    /// 合约详情缓存服务：缓存多个交易所的多个交易对的合约详情信息
    /// 
    /// 功能说明：
    /// - 启动时先从数据库读取已缓存的合约详情
    /// - 然后验证是否和交易所一致，如果不同则更新数据库和内存缓存
    /// - 每天凌晨重新获取并更新
    /// - 提供查询接口获取合约详情
    /// 
    /// 使用方式：
    /// 1. 在 Program.cs 中注册为 Singleton：
    ///    builder.Services.AddSingleton&lt;ContractDetailsCacheService&gt;();
    /// 
    /// 2. 如需对外提供接口，可在 Controllers 中注入此服务：
    ///    - GET /api/contracts/{exchange} - 获取指定交易所的所有合约
    ///    - GET /api/contracts/{exchange}/{symbol} - 获取指定交易所和交易对的合约详情
    ///    - GET /api/contracts - 获取所有交易所的所有合约
    /// </summary>
    public sealed class ContractDetailsCacheService : BaseService
    {
        /// <summary>
        /// 缓存：交易所 -> 交易对 -> 合约详情
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ContractDetails>> _cache = new();

        /// <summary>
        /// 交易所实例缓存：交易所名称 -> Exchange 实例
        /// </summary>
        private readonly Dictionary<string, Exchange> _exchanges = new();

        /// <summary>
        /// 缓存最后刷新时间：交易所名称 -> 时间戳
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _lastRefreshTime = new();

        /// <summary>
        /// 刷新间隔（默认 1 小时）
        /// </summary>
        private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// 初始化锁
        /// </summary>
        private readonly SemaphoreSlim _initLock = new(1, 1);

        /// <summary>
        /// 是否已初始化
        /// </summary>
        private volatile bool _isInitialized;

        /// <summary>
        /// 合约详情数据访问层
        /// </summary>
        private readonly ContractDetailsRepository _repository;

        public ContractDetailsCacheService(
            ILogger<ContractDetailsCacheService> logger,
            ContractDetailsRepository repository) : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// 初始化缓存（加载所有配置的交易所和交易对的合约详情）
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
            {
                return;
            }

            await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_isInitialized)
                {
                    return;
                }

                Logger.LogInformation("开始初始化合约详情缓存服务...");

                // 确保数据库表结构存在
                await _repository.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
                Logger.LogInformation("合约详情数据库表结构检查完成");

                // 先从数据库加载已缓存的合约详情
                await LoadFromDatabaseAsync(cancellationToken).ConfigureAwait(false);

                // 创建交易所实例
                await CreateExchangesAsync(cancellationToken).ConfigureAwait(false);

                // 验证并更新合约详情（如果与交易所不一致则更新）
                await VerifyAndUpdateContractsAsync(cancellationToken).ConfigureAwait(false);

                _isInitialized = true;

                // 输出初始化完成汇总信息
                var totalContracts = _cache.Values.Sum(c => c.Count);
                var totalExchanges = _cache.Count;
                Logger.LogInformation(
                    "========== 合约详情缓存服务初始化完成 ==========");
                Logger.LogInformation(
                    "交易所数量: {ExchangeCount}，合约总数: {TotalContracts}",
                    totalExchanges, totalContracts);

                // 输出每个交易所的统计
                foreach (var exchangeKvp in _cache.OrderBy(kvp => kvp.Key))
                {
                    var exchangeName = exchangeKvp.Key;
                    var contractCount = exchangeKvp.Value.Count;
                    var symbols = exchangeKvp.Value.Values
                        .GroupBy(c => $"{c.Base}/{c.Quote}")
                        .Select(g => g.Key)
                        .OrderBy(s => s)
                        .ToList();

                    Logger.LogInformation(
                        "  [{Exchange}] {Count} 个合约，{SymbolCount} 个交易对: {Symbols}",
                        exchangeName, contractCount, symbols.Count, string.Join(", ", symbols));
                }

                Logger.LogInformation(
                    "================================================");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "合约详情缓存服务初始化失败");
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// 刷新指定交易所的合约详情缓存（从交易所获取并更新数据库）
        /// </summary>
        public async Task RefreshExchangeAsync(MarketDataConfig.ExchangeEnum exchangeEnum, CancellationToken cancellationToken = default)
        {
            var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
            if (!_exchanges.TryGetValue(exchangeName, out var exchange))
            {
                Logger.LogWarning("交易所 {Exchange} 未初始化，跳过刷新", exchangeName);
                return;
            }

            try
            {
                // 从交易所加载合约详情
                var contracts = await LoadExchangeContractsFromExchangeAsync(exchange, exchangeEnum, cancellationToken).ConfigureAwait(false);

                // 更新数据库
                await _repository.UpsertBatchAsync(contracts, cancellationToken).ConfigureAwait(false);

                // 更新内存缓存
                var cacheContracts = _cache.GetOrAdd(exchangeName, _ => new ConcurrentDictionary<string, ContractDetails>());
                foreach (var contract in contracts)
                {
                    cacheContracts[contract.Symbol] = contract;
                }

                _lastRefreshTime[exchangeName] = DateTime.UtcNow;
                Logger.LogInformation(
                    "交易所 {Exchange} 合约详情刷新完成：已更新 {Count} 个合约到数据库",
                    exchangeName, contracts.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "刷新交易所 {Exchange} 合约详情失败", exchangeName);
            }
        }

        /// <summary>
        /// 刷新所有交易所的合约详情缓存（从交易所获取并更新数据库）
        /// </summary>
        public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("开始刷新所有交易所的合约详情缓存...");

            var tasks = Enum.GetValues<MarketDataConfig.ExchangeEnum>()
                .Select(exchangeEnum => RefreshExchangeAsync(exchangeEnum, cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // 输出总体统计
            var totalContracts = _cache.Values.Sum(c => c.Count);
            Logger.LogInformation(
                "所有交易所的合约详情缓存刷新完成：共 {TotalContracts} 个合约",
                totalContracts);
        }

        /// <summary>
        /// 检查并刷新过期的缓存
        /// </summary>
        public async Task RefreshIfNeededAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var exchangesToRefresh = new List<MarketDataConfig.ExchangeEnum>();

            foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
                if (!_lastRefreshTime.TryGetValue(exchangeName, out var lastRefresh) ||
                    now - lastRefresh >= _refreshInterval)
                {
                    exchangesToRefresh.Add(exchangeEnum);
                }
            }

            if (exchangesToRefresh.Count > 0)
            {
                Logger.LogInformation("检测到 {Count} 个交易所需要刷新合约详情缓存", exchangesToRefresh.Count);
                var tasks = exchangesToRefresh.Select(ex => RefreshExchangeAsync(ex, cancellationToken)).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 获取指定交易所的所有合约详情
        /// </summary>
        /// <param name="exchangeEnum">交易所枚举</param>
        /// <returns>交易对 -> 合约详情的字典</returns>
        public Dictionary<string, ContractDetails> GetContractsByExchange(MarketDataConfig.ExchangeEnum exchangeEnum)
        {
            var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
            if (!_cache.TryGetValue(exchangeName, out var contracts))
            {
                return new Dictionary<string, ContractDetails>();
            }

            return contracts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// 获取指定交易所和交易对的合约详情
        /// </summary>
        /// <param name="exchangeEnum">交易所枚举</param>
        /// <param name="symbol">交易对符号（CCXT 格式，如 BTC/USDT:USDT）</param>
        /// <returns>合约详情，如果不存在则返回 null</returns>
        public ContractDetails? GetContract(MarketDataConfig.ExchangeEnum exchangeEnum, string symbol)
        {
            var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
            if (!_cache.TryGetValue(exchangeName, out var contracts))
            {
                return null;
            }

            contracts.TryGetValue(symbol, out var contract);
            return contract;
        }

        /// <summary>
        /// 获取所有交易所的所有合约详情
        /// </summary>
        /// <returns>交易所 -> 交易对 -> 合约详情的嵌套字典</returns>
        public Dictionary<string, Dictionary<string, ContractDetails>> GetAllContracts()
        {
            return _cache.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary(kvp2 => kvp2.Key, kvp2 => kvp2.Value));
        }

        /// <summary>
        /// 获取指定交易对在所有交易所的合约详情
        /// </summary>
        /// <param name="symbol">交易对符号（CCXT 格式，如 BTC/USDT:USDT）</param>
        /// <returns>交易所 -> 合约详情的字典</returns>
        public Dictionary<string, ContractDetails> GetContractsBySymbol(string symbol)
        {
            var result = new Dictionary<string, ContractDetails>();

            foreach (var exchangeKvp in _cache)
            {
                if (exchangeKvp.Value.TryGetValue(symbol, out var contract))
                {
                    result[exchangeKvp.Key] = contract;
                }
            }

            return result;
        }

        /// <summary>
        /// 获取配置的交易对在所有交易所的合约详情
        /// </summary>
        /// <param name="symbolEnum">交易对枚举</param>
        /// <returns>交易所 -> 合约详情的字典</returns>
        public Dictionary<string, ContractDetails> GetContractsBySymbol(MarketDataConfig.SymbolEnum symbolEnum)
        {
            var symbol = MarketDataConfig.SymbolToString(symbolEnum);
            var result = new Dictionary<string, ContractDetails>();

            // 尝试不同的符号格式
            var possibleSymbols = new[]
            {
                symbol,                    // BTC/USDT
                symbol + ":USDT",          // BTC/USDT:USDT
                symbol.Replace("/", "")    // BTCUSDT
            };

            foreach (var exchangeKvp in _cache)
            {
                foreach (var possibleSymbol in possibleSymbols)
                {
                    if (exchangeKvp.Value.TryGetValue(possibleSymbol, out var contract))
                    {
                        result[exchangeKvp.Key] = contract;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public Dictionary<string, object> GetCacheStats()
        {
            var stats = new Dictionary<string, object>
            {
                ["TotalExchanges"] = _cache.Count,
                ["IsInitialized"] = _isInitialized
            };

            var exchangeStats = new Dictionary<string, object>();
            foreach (var exchangeKvp in _cache)
            {
                var exchangeName = exchangeKvp.Key;
                var contractCount = exchangeKvp.Value.Count;
                var lastRefresh = _lastRefreshTime.TryGetValue(exchangeName, out var refreshTime)
                    ? refreshTime.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    : "从未刷新";

                exchangeStats[exchangeName] = new Dictionary<string, object>
                {
                    ["ContractCount"] = contractCount,
                    ["LastRefresh"] = lastRefresh
                };
            }

            stats["Exchanges"] = exchangeStats;
            return stats;
        }

        #region 私有方法

        /// <summary>
        /// 从数据库加载已缓存的合约详情
        /// </summary>
        private async Task LoadFromDatabaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("开始从数据库加载合约详情...");
                var allContracts = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
                var contractsList = allContracts.ToList();

                if (contractsList.Count == 0)
                {
                    Logger.LogInformation("数据库中暂无合约详情数据，将首次从交易所获取");
                    return;
                }

                // 按交易所分组加载到缓存
                foreach (var contract in contractsList)
                {
                    var contracts = _cache.GetOrAdd(contract.Exchange, _ => new ConcurrentDictionary<string, ContractDetails>());
                    contracts[contract.Symbol] = contract;
                }

                var exchangeGroups = contractsList.GroupBy(c => c.Exchange).ToList();
                Logger.LogInformation(
                    "从数据库加载完成：共 {TotalCount} 个合约，涉及 {ExchangeCount} 个交易所",
                    contractsList.Count, exchangeGroups.Count);

                foreach (var group in exchangeGroups)
                {
                    Logger.LogInformation(
                        "  [{Exchange}] {Count} 个合约",
                        group.Key, group.Count());
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "从数据库加载合约详情失败，将重新从交易所获取");
            }
        }

        /// <summary>
        /// 验证并更新合约详情（如果与交易所不一致则更新）
        /// </summary>
        private async Task VerifyAndUpdateContractsAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("开始验证合约详情是否与交易所一致...");

            var allContractsToUpdate = new List<ContractDetails>();
            var updateCount = 0;
            var unchangedCount = 0;

            foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
                if (!_exchanges.TryGetValue(exchangeName, out var exchange))
                {
                    continue;
                }

                // 从交易所获取最新的合约详情
                var exchangeContracts = await LoadExchangeContractsFromExchangeAsync(exchange, exchangeEnum, cancellationToken).ConfigureAwait(false);

                // 与数据库中的数据进行对比
                var dbContracts = _cache.TryGetValue(exchangeName, out var cached)
                    ? cached.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    : new Dictionary<string, ContractDetails>();

                foreach (var exchangeContract in exchangeContracts)
                {
                    if (dbContracts.TryGetValue(exchangeContract.Symbol, out var dbContract))
                    {
                        // 检查是否有变化
                        if (IsContractChanged(dbContract, exchangeContract))
                        {
                            allContractsToUpdate.Add(exchangeContract);
                            updateCount++;
                        }
                        else
                        {
                            unchangedCount++;
                        }
                    }
                    else
                    {
                        // 新增的合约
                        allContractsToUpdate.Add(exchangeContract);
                        updateCount++;
                    }
                }
            }

            // 批量更新数据库
            if (allContractsToUpdate.Count > 0)
            {
                Logger.LogInformation(
                    "检测到 {UpdateCount} 个合约需要更新，{UnchangedCount} 个合约无变化",
                    updateCount, unchangedCount);

                await _repository.UpsertBatchAsync(allContractsToUpdate, cancellationToken).ConfigureAwait(false);

                // 更新内存缓存
                foreach (var contract in allContractsToUpdate)
                {
                    var contracts = _cache.GetOrAdd(contract.Exchange, _ => new ConcurrentDictionary<string, ContractDetails>());
                    contracts[contract.Symbol] = contract;
                }

                Logger.LogInformation("合约详情更新完成：已更新 {Count} 个合约到数据库", allContractsToUpdate.Count);
            }
            else
            {
                Logger.LogInformation("所有合约详情与交易所一致，无需更新");
            }
        }

        /// <summary>
        /// 检查合约详情是否有变化
        /// </summary>
        private static bool IsContractChanged(ContractDetails dbContract, ContractDetails exchangeContract)
        {
            return dbContract.Base != exchangeContract.Base ||
                   dbContract.Quote != exchangeContract.Quote ||
                   dbContract.Settle != exchangeContract.Settle ||
                   dbContract.IsContract != exchangeContract.IsContract ||
                   dbContract.IsSwap != exchangeContract.IsSwap ||
                   dbContract.IsFuture != exchangeContract.IsFuture ||
                   dbContract.IsLinear != exchangeContract.IsLinear ||
                   dbContract.IsInverse != exchangeContract.IsInverse ||
                   dbContract.ContractSize != exchangeContract.ContractSize ||
                   dbContract.MinOrderAmount != exchangeContract.MinOrderAmount ||
                   dbContract.MaxOrderAmount != exchangeContract.MaxOrderAmount ||
                   dbContract.MinOrderPrice != exchangeContract.MinOrderPrice ||
                   dbContract.MaxOrderPrice != exchangeContract.MaxOrderPrice ||
                   dbContract.AmountPrecision != exchangeContract.AmountPrecision ||
                   dbContract.PricePrecision != exchangeContract.PricePrecision ||
                   dbContract.TickSize != exchangeContract.TickSize ||
                   dbContract.Active != exchangeContract.Active;
        }

        /// <summary>
        /// 从交易所加载合约详情（不保存到数据库，仅用于验证）
        /// </summary>
        private async Task<List<ContractDetails>> LoadExchangeContractsFromExchangeAsync(
            Exchange exchange,
            MarketDataConfig.ExchangeEnum exchangeEnum,
            CancellationToken cancellationToken)
        {
            var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
            var result = new List<ContractDetails>();

            try
            {
                // 确保 markets 已加载
                await exchange.LoadMarkets().ConfigureAwait(false);

                dynamic dynamicExchange = exchange;
                var markets = dynamicExchange.markets as Dictionary<string, object>;
                if (markets == null || markets.Count == 0)
                {
                    Logger.LogWarning("交易所 {Exchange} 的市场数据为空", exchangeName);
                    return result;
                }

                // 获取配置的交易对列表
                var configuredSymbols = Enum.GetValues<MarketDataConfig.SymbolEnum>()
                    .Select(MarketDataConfig.SymbolToString)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var marketKvp in markets)
                {
                    var symbol = marketKvp.Key;
                    if (marketKvp.Value is not Dictionary<string, object> marketDict)
                    {
                        continue;
                    }

                    // 检查是否为合约市场
                    if (!IsContractMarket(marketDict))
                    {
                        continue;
                    }

                    // 提取基础资产和计价资产
                    var baseAsset = GetString(marketDict, "base") ?? GetString(marketDict, "baseId") ?? string.Empty;
                    var quoteAsset = GetString(marketDict, "quote") ?? GetString(marketDict, "quoteId") ?? string.Empty;

                    // 只加载配置的交易对（匹配基础资产和计价资产）
                    var normalizedSymbol = $"{baseAsset}/{quoteAsset}";
                    if (!configuredSymbols.Contains(normalizedSymbol))
                    {
                        continue;
                    }

                    // 提取合约详情
                    var contract = ExtractContractDetails(exchangeName, symbol, marketDict);
                    result.Add(contract);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "从交易所 {Exchange} 加载合约详情失败", exchangeName);
            }

            return result;
        }

        /// <summary>
        /// 创建所有配置的交易所实例
        /// </summary>
        private async Task CreateExchangesAsync(CancellationToken cancellationToken)
        {
            foreach (var exchangeEnum in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                var exchangeName = MarketDataConfig.ExchangeToString(exchangeEnum);
                var options = MarketDataConfig.GetExchangeOptions(exchangeEnum);

                Exchange exchange = exchangeEnum switch
                {
                    MarketDataConfig.ExchangeEnum.Binance => new binanceusdm(options),
                    MarketDataConfig.ExchangeEnum.OKX => new okx(options),
                    MarketDataConfig.ExchangeEnum.Bitget => new bitget(options),
                    _ => throw new NotSupportedException($"不支持的交易所: {exchangeEnum}")
                };

                try
                {
                    await exchange.LoadMarkets().ConfigureAwait(false);
                    _exchanges[exchangeName] = exchange;
                    Logger.LogInformation("交易所 {Exchange} 初始化完成，已加载市场数据", exchangeName);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "交易所 {Exchange} 初始化失败", exchangeName);
                    // 继续处理其他交易所，不中断整个初始化流程
                }
            }
        }


        /// <summary>
        /// 从 CCXT market 对象提取合约详情
        /// </summary>
        private ContractDetails ExtractContractDetails(string exchange, string symbol, Dictionary<string, object> marketDict)
        {
            var contract = new ContractDetails
            {
                Exchange = exchange,
                Symbol = symbol,
                Base = GetString(marketDict, "base") ?? GetString(marketDict, "baseId") ?? string.Empty,
                Quote = GetString(marketDict, "quote") ?? GetString(marketDict, "quoteId") ?? string.Empty,
                Settle = GetString(marketDict, "settle") ?? GetString(marketDict, "settleId"),
                IsContract = GetBool(marketDict, "contract"),
                IsSwap = GetBool(marketDict, "swap"),
                IsFuture = GetBool(marketDict, "future"),
                IsLinear = GetBoolNullable(marketDict, "linear"),
                IsInverse = GetBoolNullable(marketDict, "inverse"),
                ContractSize = GetDecimalNullable(marketDict, "contractSize"),
                Active = GetBoolNullable(marketDict, "active"),
                LastUpdated = GetLongNullable(marketDict, "lastUpdated")
            };

            // 提取精度信息
            if (marketDict.TryGetValue("precision", out var precisionObj) && precisionObj is Dictionary<string, object> precisionDict)
            {
                contract.AmountPrecision = GetIntNullable(precisionDict, "amount");
                contract.PricePrecision = GetIntNullable(precisionDict, "price");
                contract.TickSize = GetDecimalNullable(precisionDict, "price");
            }

            // 提取 limits 信息
            if (marketDict.TryGetValue("limits", out var limitsObj) && limitsObj is Dictionary<string, object> limitsDict)
            {
                if (limitsDict.TryGetValue("amount", out var amountObj) && amountObj is Dictionary<string, object> amountDict)
                {
                    contract.MinOrderAmount = GetDecimalNullable(amountDict, "min");
                    contract.MaxOrderAmount = GetDecimalNullable(amountDict, "max");
                }

                if (limitsDict.TryGetValue("price", out var priceObj) && priceObj is Dictionary<string, object> priceDict)
                {
                    contract.MinOrderPrice = GetDecimalNullable(priceDict, "min");
                    contract.MaxOrderPrice = GetDecimalNullable(priceDict, "max");
                }
            }

            return contract;
        }

        /// <summary>
        /// 检查是否为合约市场
        /// </summary>
        private static bool IsContractMarket(Dictionary<string, object> marketDict)
        {
            return GetBool(marketDict, "swap") || GetBool(marketDict, "future") || GetBool(marketDict, "contract");
        }

        /// <summary>
        /// 从字典中获取字符串值
        /// </summary>
        private static string? GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        /// <summary>
        /// 从字典中获取布尔值
        /// </summary>
        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (bool.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }

            return false;
        }

        /// <summary>
        /// 从字典中获取可空布尔值
        /// </summary>
        private static bool? GetBoolNullable(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (bool.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>
        /// 从字典中获取可空小数
        /// </summary>
        private static decimal? GetDecimalNullable(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            try
            {
                if (value is decimal decimalValue)
                {
                    return decimalValue;
                }

                if (value is double doubleValue)
                {
                    return (decimal)doubleValue;
                }

                if (decimal.TryParse(value.ToString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        /// <summary>
        /// 从字典中获取可空整数
        /// </summary>
        private static int? GetIntNullable(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            try
            {
                if (value is int intValue)
                {
                    return intValue;
                }

                if (int.TryParse(value.ToString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        /// <summary>
        /// 从字典中获取可空长整数
        /// </summary>
        private static long? GetLongNullable(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            try
            {
                if (value is long longValue)
                {
                    return longValue;
                }

                if (long.TryParse(value.ToString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        #endregion
    }
}
