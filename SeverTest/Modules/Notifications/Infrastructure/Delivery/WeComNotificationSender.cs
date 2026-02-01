using Microsoft.Extensions.Logging;
using ServerTest.Modules.Notifications.Domain;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ServerTest.Modules.Notifications.Infrastructure.Delivery
{
    public sealed class WeComNotificationSender : INotificationSender
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly ILogger<WeComNotificationSender> _logger;

        public WeComNotificationSender(ILogger<WeComNotificationSender> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public NotificationChannel Channel => NotificationChannel.WeCom;

        public async Task<NotificationSendResult> SendAsync(NotificationSendContext context, CancellationToken ct = default)
        {
            var webhook = context.ChannelBinding?.Address;
            if (string.IsNullOrWhiteSpace(webhook))
            {
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = "wecom_not_bound"
                };
            }

            var content = BuildContent(context);
            var payload = JsonSerializer.Serialize(new
            {
                msgtype = "text",
                text = new { content }
            });

            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(webhook, body, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationSendResult { Success = true };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("WeCom通知发送失败: status={Status} body={Body}", response.StatusCode, responseText);
            return new NotificationSendResult
            {
                Success = false,
                ErrorMessage = responseText
            };
        }

        private static string BuildContent(NotificationSendContext context)
        {
            if (string.IsNullOrWhiteSpace(context.Body))
            {
                return context.Title;
            }

            return $"{context.Title}\n{context.Body}";
        }
    }
}
