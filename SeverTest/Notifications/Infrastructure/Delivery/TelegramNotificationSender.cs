using Microsoft.Extensions.Logging;
using ServerTest.Notifications.Contracts;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ServerTest.Notifications.Infrastructure.Delivery
{
    public sealed class TelegramNotificationSender : INotificationSender
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly ILogger<TelegramNotificationSender> _logger;

        public TelegramNotificationSender(ILogger<TelegramNotificationSender> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public NotificationChannel Channel => NotificationChannel.Telegram;

        public async Task<NotificationSendResult> SendAsync(NotificationSendContext context, CancellationToken ct = default)
        {
            var chatId = context.ChannelBinding?.Address;
            var botToken = context.ChannelBinding?.Secret;

            if (string.IsNullOrWhiteSpace(chatId) || string.IsNullOrWhiteSpace(botToken))
            {
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = "telegram_not_bound"
                };
            }

            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = JsonSerializer.Serialize(new
            {
                chat_id = chatId,
                text = BuildContent(context)
            });

            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(url, body, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationSendResult { Success = true };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("TelegramÍ¨Öª·¢ËÍÊ§°Ü: status={Status} body={Body}", response.StatusCode, responseText);
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
