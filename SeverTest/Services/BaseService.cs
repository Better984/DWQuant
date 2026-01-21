using Microsoft.Extensions.Logging;

namespace ServerTest.Services
{
    public abstract class BaseService
    {
        protected ILogger Logger { get; }

        protected BaseService(ILogger logger)
        {
            Logger = logger;
        }
    }
}
