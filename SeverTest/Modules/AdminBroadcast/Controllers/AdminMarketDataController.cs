using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.Options;
using System.Linq;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/marketdata")]
    public sealed class AdminMarketDataController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly HistoricalMarketDataCache _cache;
        private readonly BusinessRulesOptions _businessRules;

        public AdminMarketDataController(
            ILogger<AdminMarketDataController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            HistoricalMarketDataCache cache,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.marketdata.cache-snapshots")]
        [HttpPost("cache-snapshots")]
        public async Task<IActionResult> GetCacheSnapshots([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            try
            {
                var snapshots = _cache.GetCacheSnapshots();
                var result = snapshots.Select(s => new
                {
                    exchange = s.Exchange,
                    symbol = s.Symbol,
                    timeframe = s.Timeframe,
                    startTime = s.StartTime.ToString("O"),
                    endTime = s.EndTime.ToString("O"),
                    count = s.Count,
                }).ToList();

                return Ok(ApiResponse<object>.Ok(new { snapshots = result }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取缓存快照失败");
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        [ProtocolType("admin.marketdata.refresh-cache")]
        [HttpPost("refresh-cache")]
        public async Task<IActionResult> RefreshCache([FromBody] ProtocolRequest<RefreshCacheRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            try
            {
                var exchangeEnum = ParseExchange(payload.Exchange);
                var symbolEnum = ParseSymbol(payload.Symbol);
                var timeframeEnum = ParseTimeframe(payload.Timeframe);

                // 根据参数刷新缓存
                if (exchangeEnum.HasValue && symbolEnum.HasValue && timeframeEnum.HasValue)
                {
                    // 刷新特定交易所+币种+周期
                    _cache.InvalidateCache(exchangeEnum.Value, timeframeEnum.Value, symbolEnum.Value);
                    Logger.LogInformation("管理员刷新缓存: Exchange={Exchange}, Symbol={Symbol}, Timeframe={Timeframe}",
                        payload.Exchange, payload.Symbol, payload.Timeframe);
                }
                else if (exchangeEnum.HasValue && symbolEnum.HasValue)
                {
                    // 刷新特定交易所+币种的所有周期
                    var allTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>();
                    foreach (var tf in allTimeframes)
                    {
                        try
                        {
                            _cache.InvalidateCache(exchangeEnum.Value, tf, symbolEnum.Value);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "刷新缓存失败: Exchange={Exchange}, Symbol={Symbol}, Timeframe={Timeframe}",
                                payload.Exchange, payload.Symbol, tf);
                        }
                    }
                    Logger.LogInformation("管理员刷新缓存: Exchange={Exchange}, Symbol={Symbol}, 所有周期",
                        payload.Exchange, payload.Symbol);
                }
                else if (exchangeEnum.HasValue)
                {
                    // 刷新特定交易所的所有币种和周期
                    var allSymbols = Enum.GetValues<MarketDataConfig.SymbolEnum>();
                    var allTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>();
                    foreach (var symbol in allSymbols)
                    {
                        foreach (var tf in allTimeframes)
                        {
                            try
                            {
                                _cache.InvalidateCache(exchangeEnum.Value, tf, symbol);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning(ex, "刷新缓存失败: Exchange={Exchange}, Symbol={Symbol}, Timeframe={Timeframe}",
                                    payload.Exchange, symbol, tf);
                            }
                        }
                    }
                    Logger.LogInformation("管理员刷新缓存: Exchange={Exchange}, 所有币种和周期", payload.Exchange);
                }
                else
                {
                    // 刷新所有缓存 - 清空所有交易所的所有币种和周期
                    var allExchanges = Enum.GetValues<MarketDataConfig.ExchangeEnum>();
                    var allSymbols = Enum.GetValues<MarketDataConfig.SymbolEnum>();
                    var allTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>();
                    foreach (var exchange in allExchanges)
                    {
                        foreach (var symbol in allSymbols)
                        {
                            foreach (var tf in allTimeframes)
                            {
                                try
                                {
                                    _cache.InvalidateCache(exchange, tf, symbol);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogWarning(ex, "刷新缓存失败: Exchange={Exchange}, Symbol={Symbol}, Timeframe={Timeframe}",
                                        exchange, symbol, tf);
                                }
                            }
                        }
                    }
                    Logger.LogInformation("管理员刷新所有历史行情缓存");
                }

                return Ok(ApiResponse<object>.Ok(new { success = true }, "刷新成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "刷新缓存失败");
                return StatusCode(500, ApiResponse<object>.Error($"刷新失败: {ex.Message}"));
            }
        }

        private static MarketDataConfig.ExchangeEnum? ParseExchange(string? exchange)
        {
            if (string.IsNullOrWhiteSpace(exchange))
                return null;

            // 从字符串映射到枚举
            return exchange.ToLowerInvariant() switch
            {
                "binance" => MarketDataConfig.ExchangeEnum.Binance,
                "okx" => MarketDataConfig.ExchangeEnum.OKX,
                "bitget" => MarketDataConfig.ExchangeEnum.Bitget,
                _ => null
            };
        }

        private static MarketDataConfig.TimeframeEnum? ParseTimeframe(string? timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe))
                return null;

            // 从字符串映射到枚举
            return timeframe.ToLowerInvariant() switch
            {
                "1m" => MarketDataConfig.TimeframeEnum.m1,
                "3m" => MarketDataConfig.TimeframeEnum.m3,
                "5m" => MarketDataConfig.TimeframeEnum.m5,
                "15m" => MarketDataConfig.TimeframeEnum.m15,
                "30m" => MarketDataConfig.TimeframeEnum.m30,
                "1h" => MarketDataConfig.TimeframeEnum.h1,
                "2h" => MarketDataConfig.TimeframeEnum.h2,
                "4h" => MarketDataConfig.TimeframeEnum.h4,
                "6h" => MarketDataConfig.TimeframeEnum.h6,
                "8h" => MarketDataConfig.TimeframeEnum.h8,
                "12h" => MarketDataConfig.TimeframeEnum.h12,
                "1d" => MarketDataConfig.TimeframeEnum.d1,
                "3d" => MarketDataConfig.TimeframeEnum.d3,
                "1w" => MarketDataConfig.TimeframeEnum.w1,
                "1mo" => MarketDataConfig.TimeframeEnum.mo1,
                _ => null
            };
        }

        private static MarketDataConfig.SymbolEnum? ParseSymbol(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            // 标准化符号格式 (BTCUSDT -> BTC/USDT, BTC_USDT -> BTC/USDT)
            var normalized = symbol.Replace("_", "/", StringComparison.Ordinal).ToUpperInvariant();
            if (!normalized.Contains("/", StringComparison.Ordinal))
            {
                // 如果没有斜杠，尝试添加/USDT
                normalized = normalized + "/USDT";
            }

            return normalized switch
            {
                "BTC/USDT" => MarketDataConfig.SymbolEnum.BTC_USDT,
                "ETH/USDT" => MarketDataConfig.SymbolEnum.ETH_USDT,
                "XRP/USDT" => MarketDataConfig.SymbolEnum.XRP_USDT,
                "SOL/USDT" => MarketDataConfig.SymbolEnum.SOL_USDT,
                "DOGE/USDT" => MarketDataConfig.SymbolEnum.DOGE_USDT,
                "BNB/USDT" => MarketDataConfig.SymbolEnum.BNB_USDT,
                _ => null
            };
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty).ConfigureAwait(false);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                return null;
            }

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }
    }

    public sealed class RefreshCacheRequest
    {
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
    }
}
