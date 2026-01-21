using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseController : ControllerBase
    {
        protected ILogger Logger { get; }

        protected BaseController(ILogger logger)
        {
            Logger = logger;
        }
    }
}
