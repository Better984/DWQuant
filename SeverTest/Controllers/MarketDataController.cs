using System;
using System.Linq;
using System.Threading.Tasks;
using ccxt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MarketDataController : BaseController
    {
        private sealed class MarketKlineDto
        {
            public long? Timestamp { get; set; }
            public double? Open { get; set; }
            public double? High { get; set; }
            public double? Low { get; set; }
            public double? Close { get; set; }
            public double? Volume { get; set; }
        }

        private readonly MarketDataEngine _marketDataEngine;
        private readonly HistoricalMarketDataCache _historicalCache;
        private readonly HistoricalMarketDataOptions _historyOptions;

        public MarketDataController(
            ILogger<MarketDataController> logger,
            MarketDataEngine marketDataEngine,
            HistoricalMarketDataCache historicalCache,
            IOptions<HistoricalMarketDataOptions> historyOptions) 
            : base(logger)
        {
            _marketDataEngine = marketDataEngine;
            _historicalCache = historicalCache;
            _historyOptions = historyOptions?.Value ?? new HistoricalMarketDataOptions();
        }

        /// <summary>
        /// 获取实时数据：返回最新的1根K线
        /// </summary>
        /// <param name="exchange">交易所枚举</param>
        /// <param name="timeframe">周期枚举</param>
        /// <param name="symbol">交易对枚举</param>
        [HttpGet("latest")]
        public IActionResult GetLatestKline(
            [FromQuery] MarketDataConfig.ExchangeEnum exchange,
            [FromQuery] MarketDataConfig.TimeframeEnum timeframe,
            [FromQuery] MarketDataConfig.SymbolEnum symbol)
        {
            try
            {
                var kline = _marketDataEngine.GetLatestKline(exchange, timeframe, symbol);
                
                if (!kline.HasValue)
                {
                    return NotFound(ApiResponse<object>.Error("未找到K线数据"));
                }

                var dto = ToDto(kline.Value);
                return Ok(ApiResponse<MarketKlineDto>.Ok(dto));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取最新K线失败");
                return BadRequest(ApiResponse<object>.Error($"获取最新K线失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 获取历史数据：返回最新的n根K线数据
        /// </summary>
        /// <param name="exchange">交易所枚举</param>
        /// <param name="timeframe">周期枚举</param>
        /// <param name="symbol">交易对枚举</param>
        /// <param name="startTime">开始时间（可选，格式：yyyy-MM-dd HH:mm:ss）</param>
        /// <param name="endTime">结束时间（可选，格式：yyyy-MM-dd HH:mm:ss）</param>
        /// <param name="count">需求K线数量</param>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistoryKlines(
            [FromQuery] MarketDataConfig.ExchangeEnum exchange,
            [FromQuery] MarketDataConfig.TimeframeEnum timeframe,
            [FromQuery] MarketDataConfig.SymbolEnum symbol,
            [FromQuery] string? startTime = null,
            [FromQuery] string? endTime = null,
            [FromQuery] int count = 100)
        {
            try
            {
                Logger.LogInformation(
                    "历史K线请求: exchange={Exchange} timeframe={Timeframe} symbol={Symbol} start={Start} end={End} count={Count}",
                    exchange,
                    timeframe,
                    symbol,
                    startTime ?? "(null)",
                    endTime ?? "(null)",
                    count);

                DateTime? startDateTime = null;
                if (!string.IsNullOrEmpty(startTime))
                {
                    if (DateTime.TryParse(startTime, out var parsedStart))
                    {
                        startDateTime = parsedStart;
                    }
                    else
                    {
                        return BadRequest(ApiResponse<object>.Error("开始时间格式错误，请使用 yyyy-MM-dd HH:mm:ss"));
                    }
                }

                DateTime? endDateTime = null;
                if (!string.IsNullOrEmpty(endTime))
                {
                    if (DateTime.TryParse(endTime, out var parsed))
                    {
                        endDateTime = parsed;
                    }
                    else
                    {
                        return BadRequest(ApiResponse<object>.Error("结束时间格式错误，请使用 yyyy-MM-dd HH:mm:ss"));
                    }
                }

                if (count <= 0 || count > _historyOptions.MaxQueryBars)
                {
                    return BadRequest(ApiResponse<object>.Error($"K线数量必须在1-{_historyOptions.MaxQueryBars}之间"));
                }

                var klines = await _historicalCache.GetHistoryAsync(
                    exchange,
                    timeframe,
                    symbol,
                    startDateTime,
                    endDateTime,
                    count,
                    HttpContext.RequestAborted);
                Logger.LogInformation(
                    "历史K线返回: exchange={Exchange} timeframe={Timeframe} symbol={Symbol} count={Count}",
                    exchange,
                    timeframe,
                    symbol,
                    klines.Count);

                var payload = klines.Select(ToDto).ToList();
                return Ok(ApiResponse<List<MarketKlineDto>>.Ok(payload));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取历史K线失败");
                return BadRequest(ApiResponse<object>.Error($"获取历史K线失败: {ex.Message}"));
            }
        }

        private static MarketKlineDto ToDto(OHLCV candle)
        {
            return new MarketKlineDto
            {
                Timestamp = candle.timestamp,
                Open = candle.open,
                High = candle.high,
                Low = candle.low,
                Close = candle.close,
                Volume = candle.volume
            };
        }
    }
}
