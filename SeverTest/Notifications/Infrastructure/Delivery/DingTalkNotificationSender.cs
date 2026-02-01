using Microsoft.Extensions.Logging;
using ServerTest.Notifications.Contracts;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServerTest.Notifications.Infrastructure.Delivery
{
    public sealed class DingTalkNotificationSender : INotificationSender
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly ILogger<DingTalkNotificationSender> _logger;

        public DingTalkNotificationSender(ILogger<DingTalkNotificationSender> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public NotificationChannel Channel => NotificationChannel.DingTalk;

        public async Task<NotificationSendResult> SendAsync(NotificationSendContext context, CancellationToken ct = default)
        {
            var webhook = context.ChannelBinding?.Address;
            if (string.IsNullOrWhiteSpace(webhook))
            {
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = "dingtalk_not_bound"
                };
            }

            var signedWebhook = BuildWebhook(webhook, context.ChannelBinding?.Secret, out var error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                return new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = error
                };
            }

            var content = BuildContent(context);
            var payload = JsonSerializer.Serialize(new
            {
                msgtype = "text",
                text = new { content }
            });

            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(signedWebhook, body, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationSendResult { Success = true };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("DingTalkÍ¨Öª·¢ËÍÊ§°Ü: status={Status} body={Body}", response.StatusCode, responseText);
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

        private static string BuildWebhook(string webhook, string? secret, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return webhook;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var stringToSign = $"{timestamp}\n{secret}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            var sign = WebUtility.UrlEncode(Convert.ToBase64String(hash));

            return AppendQuery(webhook, $"timestamp={timestamp}&sign={sign}");
        }

        private static string AppendQuery(string url, string query)
        {
            if (url.Contains("?", StringComparison.Ordinal))
            {
                return url + "&" + query;
            }

            return url + "?" + query;
        }
    }
}
