using ccxt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MarketDataController : BaseController
    {
        public sealed class MarketLatestRequest
        {
            public MarketDataConfig.ExchangeEnum Exchange { get; set; }
            public MarketDataConfig.TimeframeEnum Timeframe { get; set; }
            public MarketDataConfig.SymbolEnum Symbol { get; set; }
        }

        public sealed class MarketHistoryRequest
        {
            public MarketDataConfig.ExchangeEnum Exchange { get; set; }
            public MarketDataConfig.TimeframeEnum Timeframe { get; set; }
            public MarketDataConfig.SymbolEnum Symbol { get; set; }
            public string? StartTime { get; set; }
            public string? EndTime { get; set; }
            public int Count { get; set; } = 100;
        }

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
        /// 获取实时数据：返回最新的 1 根 K 线
        /// </summary>
        /// <param name="exchange">交易所枚举</param>
        /// <param name="timeframe">周期枚举</param>
        /// <param name="symbol">交易对枚举</param>
        [ProtocolType("marketdata.kline.latest")]
        [HttpPost("latest")]
        public IActionResult GetLatestKline([FromBody] ProtocolRequest<MarketLatestRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                var kline = _marketDataEngine.GetLatestKline(payload.Exchange, payload.Timeframe, payload.Symbol);

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
        /// 获取历史数据：返回最近的 n 根 K 线
        /// </summary>
        /// <param name="exchange">交易所枚举</param>
        /// <param name="timeframe">周期枚举</param>
        /// <param name="symbol">交易对枚举</param>
        /// <param name="startTime">开始时间（可选，格式：yyyy-MM-dd HH:mm:ss）</param>
        /// <param name="endTime">结束时间（可选，格式：yyyy-MM-dd HH:mm:ss）</param>
        /// <param name="count">请求的 K 线数量</param>
        [ProtocolType("marketdata.kline.history")]
        [HttpPost("history")]
        public async Task<IActionResult> GetHistoryKlines([FromBody] ProtocolRequest<MarketHistoryRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                Logger.LogInformation(
                    "历史K线请求: exchange={Exchange} timeframe={Timeframe} symbol={Symbol} start={Start} end={End} count={Count}",
                    payload.Exchange,
                    payload.Timeframe,
                    payload.Symbol,
                    payload.StartTime ?? "(null)",
                    payload.EndTime ?? "(null)",
                    payload.Count);

                DateTime? startDateTime = null;
                if (!string.IsNullOrEmpty(payload.StartTime))
                {
                    if (DateTime.TryParse(payload.StartTime, out var parsedStart))
                    {
                        startDateTime = parsedStart;
                    }
                    else
                    {
                        return BadRequest(ApiResponse<object>.Error("开始时间格式错误，请使用 yyyy-MM-dd HH:mm:ss"));
                    }
                }

                DateTime? endDateTime = null;
                if (!string.IsNullOrEmpty(payload.EndTime))
                {
                    if (DateTime.TryParse(payload.EndTime, out var parsed))
                    {
                        endDateTime = parsed;
                    }
                    else
                    {
                        return BadRequest(ApiResponse<object>.Error("结束时间格式错误，请使用 yyyy-MM-dd HH:mm:ss"));
                    }
                }

                if (payload.Count <= 0 || payload.Count > _historyOptions.MaxQueryBars)
                {
                    return BadRequest(ApiResponse<object>.Error($"K线数量必须在 1-{_historyOptions.MaxQueryBars} 之间"));
                }

                var klines = await _historicalCache.GetHistoryAsync(
                    payload.Exchange,
                    payload.Timeframe,
                    payload.Symbol,
                    startDateTime,
                    endDateTime,
                    payload.Count,
                    HttpContext.RequestAborted);
                Logger.LogInformation(
                    "历史K线返回: exchange={Exchange} timeframe={Timeframe} symbol={Symbol} count={Count}",
                    payload.Exchange,
                    payload.Timeframe,
                    payload.Symbol,
                    klines.Count);

                var responsePayload = klines.Select(ToDto).ToList();
                return Ok(ApiResponse<List<MarketKlineDto>>.Ok(responsePayload));
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
