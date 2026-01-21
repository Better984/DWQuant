using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PriceController : BaseController
    {
        private readonly ExchangePriceService _priceService;

        public PriceController(ILogger<PriceController> logger, ExchangePriceService priceService) 
            : base(logger)
        {
            _priceService = priceService;
        }

        [HttpGet("all")]
        public IActionResult GetAllPrices()
        {
            var prices = _priceService.GetAllPrices();
            return Ok(ApiResponse<Dictionary<string, PriceData>>.Ok(prices));
        }

        [HttpGet("{exchange}")]
        public IActionResult GetPrice(string exchange)
        {
            var price = _priceService.GetPrice(exchange);
            if (price == null)
            {
                return NotFound(ApiResponse<PriceData>.Error($"未找到 {exchange} 的价格数据"));
            }
            return Ok(ApiResponse<PriceData>.Ok(price));
        }
    }
}
