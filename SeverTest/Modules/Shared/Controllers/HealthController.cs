using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Services;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : BaseController
    {
        private readonly SystemStartupManager _startupManager;

        public HealthController(
            ILogger<HealthController> logger,
            SystemStartupManager startupManager) : base(logger)
        {
            _startupManager = startupManager;
        }

        [ProtocolType("system.health")]
        [HttpPost("get")]
        public IActionResult Get([FromBody] ProtocolRequest<object> request)
        {
            var allStatuses = _startupManager.GetAllStatuses();
            var infrastructureReady = _startupManager.IsReady(SystemModule.Infrastructure);
            var tradingSystemReady = _startupManager.IsReady(SystemModule.TradingSystem);
            var networkReady = _startupManager.IsReady(SystemModule.Network);

            // 整体健康状态：所有关键系统均就绪
            var overallHealthy = infrastructureReady && tradingSystemReady && networkReady;

            var statusDetails = new Dictionary<string, object>();
            foreach (var (module, (status, error)) in allStatuses)
            {
                statusDetails[module.ToString()] = new
                {
                    status = status.ToString(),
                    ready = status == SystemStatus.Ready,
                    error = error
                };
            }

            return Ok(new
            {
                status = overallHealthy ? "healthy" : "degraded",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                systems = statusDetails,
                critical = new
                {
                    infrastructure = infrastructureReady,
                    tradingSystem = tradingSystemReady,
                    network = networkReady
                }
            });
        }
    }
}
