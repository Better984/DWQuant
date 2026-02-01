using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

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
            _logger.LogInformation("ÂèëÈÄÅÈÇÆ‰ª∂Âà∞ {Email}. ‰∏ªÈ¢ò: {Subject}. ÂÜÖÂÆπ: {Body}", email, subject, body);
            return Task.CompletedTask;
        }
    }

    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(ILogger<SmtpEmailSender> logger)
        {
            _logger = logger;
        }

        public async Task SendAsync(string email, string subject, string body)
        {
            var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.qq.com";
            var portStr = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
            var user = Environment.GetEnvironmentVariable("SMTP_USER") ?? string.Empty;
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? string.Empty;
            var from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? user;
            var sslStr = Environment.GetEnvironmentVariable("SMTP_SSL") ?? "true";

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                throw new InvalidOperationException("SMTP_USER/SMTP_PASS is required for email sending.");
            }

            if (string.IsNullOrWhiteSpace(from))
            {
                throw new InvalidOperationException("SMTP_FROM is required for email sending.");
            }

            if (!int.TryParse(portStr, out var port))
            {
                port = 587;
            }

            var enableSsl = !string.Equals(sslStr, "false", StringComparison.OrdinalIgnoreCase);

            using var smtpClient = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = enableSsl
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            mailMessage.To.Add(email);

            _logger.LogInformation("SMTP∑¢ÀÕ” º˛: {Email} via {Host}:{Port} ssl={Ssl}", email, host, port, enableSsl);
            await smtpClient.SendMailAsync(mailMessage).ConfigureAwait(false);
        }
    }
}