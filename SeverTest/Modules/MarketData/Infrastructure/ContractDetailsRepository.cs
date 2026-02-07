using ServerTest.Infrastructure.Db;
using ServerTest.Modules.MarketData.Domain;

namespace ServerTest.Modules.MarketData.Infrastructure
{
    /// <summary>
    /// 合约详情数据访问层
    /// </summary>
    public sealed class ContractDetailsRepository
    {
        private readonly IDbManager _db;

        public ContractDetailsRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// 确保表结构存在
        /// </summary>
        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS `contract_details` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `exchange` VARCHAR(32) NOT NULL COMMENT '交易所名称（binance/okx/bitget）',
  `symbol` VARCHAR(64) NOT NULL COMMENT '交易对符号（CCXT 格式，如 BTC/USDT:USDT）',
  `base` VARCHAR(32) NOT NULL COMMENT '基础资产（如 BTC）',
  `quote` VARCHAR(32) NOT NULL COMMENT '计价资产（如 USDT）',
  `settle` VARCHAR(32) NULL COMMENT '结算资产（合约结算币种）',
  `is_contract` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否为合约市场',
  `is_swap` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否为永续合约',
  `is_future` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否为交割合约',
  `is_linear` TINYINT(1) NULL COMMENT '是否为线性合约',
  `is_inverse` TINYINT(1) NULL COMMENT '是否为反向合约',
  `contract_size` DECIMAL(20, 8) NULL COMMENT '合约乘数',
  `min_order_amount` DECIMAL(20, 8) NULL COMMENT '最小订单数量',
  `max_order_amount` DECIMAL(20, 8) NULL COMMENT '最大订单数量',
  `min_order_price` DECIMAL(20, 8) NULL COMMENT '最小订单价格',
  `max_order_price` DECIMAL(20, 8) NULL COMMENT '最大订单价格',
  `amount_precision` INT NULL COMMENT '数量精度（小数点后位数）',
  `price_precision` INT NULL COMMENT '价格精度（小数点后位数）',
  `tick_size` DECIMAL(20, 8) NULL COMMENT '最小价格变动',
  `active` TINYINT(1) NULL COMMENT '是否活跃（可交易）',
  `last_updated` BIGINT NULL COMMENT '市场信息最后更新时间（Unix 毫秒时间戳）',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uk_exchange_symbol` (`exchange`, `symbol`),
  KEY `idx_exchange` (`exchange`),
  KEY `idx_base_quote` (`base`, `quote`),
  KEY `idx_active` (`active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='合约详情表';
";
            return _db.ExecuteAsync(sql, null, null, ct);
        }

        /// <summary>
        /// 获取指定交易所的所有合约详情
        /// </summary>
        public Task<IEnumerable<ContractDetails>> GetByExchangeAsync(string exchange, CancellationToken ct = default)
        {
            const string sql = @"
SELECT 
  exchange AS Exchange,
  symbol AS Symbol,
  base AS Base,
  quote AS Quote,
  settle AS Settle,
  is_contract AS IsContract,
  is_swap AS IsSwap,
  is_future AS IsFuture,
  is_linear AS IsLinear,
  is_inverse AS IsInverse,
  contract_size AS ContractSize,
  min_order_amount AS MinOrderAmount,
  max_order_amount AS MaxOrderAmount,
  min_order_price AS MinOrderPrice,
  max_order_price AS MaxOrderPrice,
  amount_precision AS AmountPrecision,
  price_precision AS PricePrecision,
  tick_size AS TickSize,
  active AS Active,
  last_updated AS LastUpdated
FROM contract_details
WHERE exchange = @Exchange
ORDER BY base, quote, symbol
";
            return _db.QueryAsync<ContractDetails>(sql, new { Exchange = exchange }, null, ct);
        }

        /// <summary>
        /// 获取指定交易所和交易对的合约详情
        /// </summary>
        public Task<ContractDetails?> GetByExchangeAndSymbolAsync(string exchange, string symbol, CancellationToken ct = default)
        {
            const string sql = @"
SELECT 
  exchange AS Exchange,
  symbol AS Symbol,
  base AS Base,
  quote AS Quote,
  settle AS Settle,
  is_contract AS IsContract,
  is_swap AS IsSwap,
  is_future AS IsFuture,
  is_linear AS IsLinear,
  is_inverse AS IsInverse,
  contract_size AS ContractSize,
  min_order_amount AS MinOrderAmount,
  max_order_amount AS MaxOrderAmount,
  min_order_price AS MinOrderPrice,
  max_order_price AS MaxOrderPrice,
  amount_precision AS AmountPrecision,
  price_precision AS PricePrecision,
  tick_size AS TickSize,
  active AS Active,
  last_updated AS LastUpdated
FROM contract_details
WHERE exchange = @Exchange AND symbol = @Symbol
LIMIT 1
";
            return _db.QuerySingleOrDefaultAsync<ContractDetails>(sql, new { Exchange = exchange, Symbol = symbol }, null, ct);
        }

        /// <summary>
        /// 获取所有合约详情
        /// </summary>
        public Task<IEnumerable<ContractDetails>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT 
  exchange AS Exchange,
  symbol AS Symbol,
  base AS Base,
  quote AS Quote,
  settle AS Settle,
  is_contract AS IsContract,
  is_swap AS IsSwap,
  is_future AS IsFuture,
  is_linear AS IsLinear,
  is_inverse AS IsInverse,
  contract_size AS ContractSize,
  min_order_amount AS MinOrderAmount,
  max_order_amount AS MaxOrderAmount,
  min_order_price AS MinOrderPrice,
  max_order_price AS MaxOrderPrice,
  amount_precision AS AmountPrecision,
  price_precision AS PricePrecision,
  tick_size AS TickSize,
  active AS Active,
  last_updated AS LastUpdated
FROM contract_details
ORDER BY exchange, base, quote, symbol
";
            return _db.QueryAsync<ContractDetails>(sql, null, null, ct);
        }

        /// <summary>
        /// 批量插入或更新合约详情（使用 INSERT ... ON DUPLICATE KEY UPDATE）
        /// </summary>
        public Task UpsertBatchAsync(IEnumerable<ContractDetails> contracts, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO contract_details (
  exchange, symbol, base, quote, settle,
  is_contract, is_swap, is_future, is_linear, is_inverse,
  contract_size, min_order_amount, max_order_amount,
  min_order_price, max_order_price,
  amount_precision, price_precision, tick_size,
  active, last_updated
) VALUES (
  @Exchange, @Symbol, @Base, @Quote, @Settle,
  @IsContract, @IsSwap, @IsFuture, @IsLinear, @IsInverse,
  @ContractSize, @MinOrderAmount, @MaxOrderAmount,
  @MinOrderPrice, @MaxOrderPrice,
  @AmountPrecision, @PricePrecision, @TickSize,
  @Active, @LastUpdated
)
ON DUPLICATE KEY UPDATE
  base = VALUES(base),
  quote = VALUES(quote),
  settle = VALUES(settle),
  is_contract = VALUES(is_contract),
  is_swap = VALUES(is_swap),
  is_future = VALUES(is_future),
  is_linear = VALUES(is_linear),
  is_inverse = VALUES(is_inverse),
  contract_size = VALUES(contract_size),
  min_order_amount = VALUES(min_order_amount),
  max_order_amount = VALUES(max_order_amount),
  min_order_price = VALUES(min_order_price),
  max_order_price = VALUES(max_order_price),
  amount_precision = VALUES(amount_precision),
  price_precision = VALUES(price_precision),
  tick_size = VALUES(tick_size),
  active = VALUES(active),
  last_updated = VALUES(last_updated),
  updated_at = CURRENT_TIMESTAMP
";
            var contractsList = contracts.ToList();
            if (contractsList.Count == 0)
            {
                return Task.CompletedTask;
            }

            return _db.ExecuteAsync(sql, contractsList, null, ct);
        }

        /// <summary>
        /// 删除指定交易所的所有合约详情
        /// </summary>
        public Task DeleteByExchangeAsync(string exchange, CancellationToken ct = default)
        {
            const string sql = @"DELETE FROM contract_details WHERE exchange = @Exchange";
            return _db.ExecuteAsync(sql, new { Exchange = exchange }, null, ct);
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public async Task<Dictionary<string, int>> GetStatisticsAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT exchange, COUNT(*) AS count
FROM contract_details
GROUP BY exchange
";
            var results = await _db.QueryAsync<(string Exchange, int Count)>(sql, null, null, ct).ConfigureAwait(false);
            return results.ToDictionary(x => x.Exchange, x => x.Count);
        }
    }
}
