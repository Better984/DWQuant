using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Notifications.Contracts;
using ServerTest.Notifications.Infrastructure;

namespace ServerTest.Notifications.Application
{
    public sealed class NotificationPreferenceService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly IDbManager _db;
        private readonly NotificationPreferenceRepository _repository;
        private readonly UserNotifyChannelRepository _channelRepository;
        private readonly ILogger<NotificationPreferenceService> _logger;

        public NotificationPreferenceService(
            IDbManager db,
            NotificationPreferenceRepository repository,
            UserNotifyChannelRepository channelRepository,
            ILogger<NotificationPreferenceService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<NotificationPreferenceDto> GetRulesAsync(long userId, CancellationToken ct = default)
        {
            var record = await _repository.GetAsync(userId, ct).ConfigureAwait(false);
            if (record == null || string.IsNullOrWhiteSpace(record.RulesJson))
            {
                return await BuildDefaultRulesAsync(userId, ct).ConfigureAwait(false);
            }

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, NotificationPreferenceRuleDto>>(record.RulesJson, JsonOptions)
                    ?? new Dictionary<string, NotificationPreferenceRuleDto>();
                return new NotificationPreferenceDto { Rules = raw };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse notification preferences: uid={Uid}", userId);
                return await BuildDefaultRulesAsync(userId, ct).ConfigureAwait(false);
            }
        }

        public async Task<(bool Ok, string? Error)> UpdateRulesAsync(long userId, Dictionary<string, NotificationPreferenceRuleDto> rules, CancellationToken ct = default)
        {
            if (rules == null || rules.Count == 0)
            {
                return (false, "Invalid rules");
            }

            rules = new Dictionary<string, NotificationPreferenceRuleDto>(rules, StringComparer.OrdinalIgnoreCase);

            foreach (var category in NotificationContractHelper.UserCategories)
            {
                if (!rules.TryGetValue(category.ToString(), out var rule))
                {
                    return (false, $"Missing rule for {category}");
                }

                if (rule.Channel != NotificationChannel.InApp)
                {
                    var bound = await _channelRepository.IsChannelBoundAsync(userId, rule.Channel, ct).ConfigureAwait(false);
                    if (!bound)
                    {
                        return (false, $"Channel not bound: {rule.Channel}");
                    }
                }
            }

            var json = JsonSerializer.Serialize(rules, JsonOptions);
            await _repository.UpsertAsync(userId, json, ct).ConfigureAwait(false);
            return (true, null);
        }

        private async Task<NotificationPreferenceDto> BuildDefaultRulesAsync(long userId, CancellationToken ct)
        {
            var defaultChannel = await ResolveDefaultChannelAsync(userId, ct).ConfigureAwait(false);
            var rules = new Dictionary<string, NotificationPreferenceRuleDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in NotificationContractHelper.UserCategories)
            {
                rules[category.ToString()] = new NotificationPreferenceRuleDto
                {
                    Enabled = true,
                    Channel = defaultChannel
                };
            }

            return new NotificationPreferenceDto { Rules = rules };
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
