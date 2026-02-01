using Microsoft.Extensions.Logging;
using ServerTest.Notifications.Contracts;
using ServerTest.Services;

namespace ServerTest.Notifications.Infrastructure.Delivery
{
    public sealed class EmailNotificationSender : INotificationSender
    {
        private readonly IEmailSender _emailSender;
        private readonly ILogger<EmailNotificationSender> _logger;

        public EmailNotificationSender(IEmailSender emailSender, ILogger<EmailNotificationSender> logger)
        {
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public NotificationChannel Channel => NotificationChannel.Email;

        public async Task<NotificationSendResult> SendAsync(NotificationSendContext context, CancellationToken ct = default)
        {
            var address = context.ChannelBinding?.Address;
            if (string.IsNullOrWhiteSpace(address))
            {
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = "email_not_bound"
                };
            }

            try
            {
                await _emailSender.SendAsync(address, context.Title, context.Body).ConfigureAwait(false);
                return new NotificationSendResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email notification failed: uid={Uid} notificationId={NotificationId}", context.UserId, context.NotificationId);
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
