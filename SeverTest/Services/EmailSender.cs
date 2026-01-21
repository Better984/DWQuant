using Microsoft.Extensions.Logging;

namespace ServerTest.Services
{
    public interface IEmailSender
    {
        Task SendAsync(string email, string subject, string body);
    }

    public class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;

        public LogEmailSender(ILogger<LogEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string email, string subject, string body)
        {
            _logger.LogInformation("Send email to {Email}. Subject: {Subject}. Body: {Body}", email, subject, body);
            return Task.CompletedTask;
        }
    }
}
