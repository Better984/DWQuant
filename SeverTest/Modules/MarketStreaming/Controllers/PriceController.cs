using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PriceController : BaseController
    {
        public sealed class PriceQueryRequest
        {
            public string Exchange { get; set; } = string.Empty;
        }

        private readonly ExchangePriceService _priceService;

        public PriceController(ILogger<PriceController> logger, ExchangePriceService priceService)
            : base(logger)
        {
            _priceService = priceService;
        }

        [ProtocolType("market.price.list")]
        [HttpPost("all")]
        public IActionResult GetAllPrices([FromBody] ProtocolRequest<object> request)
        {
            var prices = _priceService.GetAllPrices();
            return Ok(ApiResponse<Dictionary<string, PriceData>>.Ok(prices));
        }

        [ProtocolType("market.price.get")]
        [HttpPost("get")]
        public IActionResult GetPrice([FromBody] ProtocolRequest<PriceQueryRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Exchange))
            {
                return BadRequest(ApiResponse<object>.Error("缺少交易所参数"));
            }

            var price = _priceService.GetPrice(payload.Exchange);
            if (price == null)
            {
                return NotFound(ApiResponse<PriceData>.Error($"未找到 {payload.Exchange} 的价格数据"));
            }
            return Ok(ApiResponse<PriceData>.Ok(price));
        }
    }
}
