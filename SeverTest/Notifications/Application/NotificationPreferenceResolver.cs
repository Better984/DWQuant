using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Notifications.Contracts;
using ServerTest.Notifications.Infrastructure;

namespace ServerTest.Notifications.Application
{
    public sealed class NotificationPreferenceResolver : INotificationPreferenceResolver
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly IDbManager _db;
        private readonly NotificationPreferenceRepository _preferenceRepository;
        private readonly UserNotifyChannelRepository _channelRepository;
        private readonly ILogger<NotificationPreferenceResolver> _logger;

        public NotificationPreferenceResolver(
            IDbManager db,
            NotificationPreferenceRepository preferenceRepository,
            UserNotifyChannelRepository channelRepository,
            ILogger<NotificationPreferenceResolver> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _preferenceRepository = preferenceRepository ?? throw new ArgumentNullException(nameof(preferenceRepository));
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<NotificationPreferenceDecision> ResolveAsync(long userId, NotificationCategory category, CancellationToken ct = default)
        {
            if (userId <= 0)
            {
                return new NotificationPreferenceDecision
                {
                    Enabled = false,
                    Channel = NotificationChannel.InApp,
                    Reason = "invalid_user"
                };
            }

            if (!NotificationContractHelper.IsUserCategory(category))
            {
                return new NotificationPreferenceDecision
                {
                    Enabled = true,
                    Channel = NotificationChannel.InApp,
                    Reason = "system_default"
                };
            }

            var rules = await LoadRulesAsync(userId, ct).ConfigureAwait(false);
            if (rules.TryGetValue(category, out var rule) && rule != null)
            {
                if (!rule.Enabled)
                {
                    return new NotificationPreferenceDecision
                    {
                        Enabled = false,
                        Channel = NotificationChannel.InApp,
                        Reason = "disabled"
                    };
                }

                return await ResolveChannelAsync(userId, category, rule.Channel, ct).ConfigureAwait(false);
            }

            var defaultChannel = await ResolveDefaultChannelAsync(userId, ct).ConfigureAwait(false);
            return await ResolveChannelAsync(userId, category, defaultChannel, ct).ConfigureAwait(false);
        }

        private async Task<Dictionary<NotificationCategory, NotificationPreferenceRule>> LoadRulesAsync(long userId, CancellationToken ct)
        {
            var record = await _preferenceRepository.GetAsync(userId, ct).ConfigureAwait(false);
            if (record == null || string.IsNullOrWhiteSpace(record.RulesJson))
            {
                return new Dictionary<NotificationCategory, NotificationPreferenceRule>();
            }

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, NotificationPreferenceRule>>(record.RulesJson, JsonOptions)
                    ?? new Dictionary<string, NotificationPreferenceRule>();

                var map = new Dictionary<NotificationCategory, NotificationPreferenceRule>();
                foreach (var entry in raw)
                {
                    if (!NotificationContractHelper.TryParseCategory(entry.Key, out var category))
                    {
                        continue;
                    }

                    map[category] = entry.Value;
                }

                return map;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse notification preference rules: uid={Uid}", userId);
                return new Dictionary<NotificationCategory, NotificationPreferenceRule>();
            }
        }

        private async Task<NotificationPreferenceDecision> ResolveChannelAsync(
            long userId,
            NotificationCategory category,
            NotificationChannel desiredChannel,
            CancellationToken ct)
        {
            if (desiredChannel == NotificationChannel.InApp)
            {
                return new NotificationPreferenceDecision
                {
                    Enabled = true,
                    Channel = NotificationChannel.InApp
                };
            }

            var bound = await _channelRepository.IsChannelBoundAsync(userId, desiredChannel, ct).ConfigureAwait(false);
            if (bound)
            {
                return new NotificationPreferenceDecision
                {
                    Enabled = true,
                    Channel = desiredChannel
                };
            }

            var emailBound = await _channelRepository.IsChannelBoundAsync(userId, NotificationChannel.Email, ct).ConfigureAwait(false);
            var fallback = emailBound ? NotificationChannel.Email : NotificationChannel.InApp;

            _logger.LogWarning("Notification channel fallback: uid={Uid} category={Category} desired={Desired} fallback={Fallback}",
                userId, category, desiredChannel, fallback);

            return new NotificationPreferenceDecision
            {
                Enabled = true,
                Channel = fallback,
                Reason = "fallback"
            };
        }

        private async Task<NotificationChannel> ResolveDefaultChannelAsync(long userId, CancellationToken ct)
        {
            const string sql = @"
SELECT current_notification_platform
FROM account
WHERE uid = @uid AND deleted_at IS NULL
LIMIT 1;";

            var platform = await _db.QuerySingleOrDefaultAsync<string>(sql, new { uid = userId }, null, ct).ConfigureAwait(false);
            var mapped = NotificationContractHelper.TryMapChannelKey(platform);
            return mapped ?? NotificationChannel.InApp;
        }
    }
}
