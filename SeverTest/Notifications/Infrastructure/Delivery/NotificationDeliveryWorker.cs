using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Notifications.Application;
using ServerTest.Notifications.Contracts;
using ServerTest.Options;

namespace ServerTest.Notifications.Infrastructure.Delivery
{
    public sealed class NotificationDeliveryWorker : BackgroundService
    {
        private readonly NotificationRepository _repository;
        private readonly UserNotifyChannelRepository _channelRepository;
        private readonly INotificationTemplateRenderer _renderer;
        private readonly IReadOnlyDictionary<NotificationChannel, INotificationSender> _senders;
        private readonly NotificationOptions _options;
        private readonly ILogger<NotificationDeliveryWorker> _logger;

        public NotificationDeliveryWorker(
            NotificationRepository repository,
            UserNotifyChannelRepository channelRepository,
            INotificationTemplateRenderer renderer,
            IEnumerable<INotificationSender> senders,
            IOptions<NotificationOptions> options,
            ILogger<NotificationDeliveryWorker> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _channelRepository = channelRepository ?? throw new ArgumentNullException(nameof(channelRepository));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new NotificationOptions();

            _senders = senders?.ToDictionary(s => s.Channel)
                ?? throw new ArgumentNullException(nameof(senders));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var tasks = await _repository.GetPendingDeliveriesAsync(_options.DeliveryBatchSize, DateTime.UtcNow, stoppingToken)
                        .ConfigureAwait(false);

                    if (tasks.Count == 0)
                    {
                        await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var task in tasks)
                    {
                        await ProcessDeliveryAsync(task, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification delivery worker loop failed");
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ProcessDeliveryAsync(NotificationDeliveryTask task, CancellationToken ct)
        {
            var channel = NotificationContractHelper.TryMapChannelKey(task.Channel);
            if (channel == null)
            {
                await _repository.MarkDeliveryDeadAsync(task.Id, "unknown_channel", ct).ConfigureAwait(false);
                return;
            }

            var attempt = task.Attempt + 1;
            var locked = await _repository.MarkDeliverySendingAsync(task.Id, attempt, ct).ConfigureAwait(false);
            if (locked == 0)
            {
                return;
            }

            var notification = await _repository.GetNotificationAsync(task.NotificationId, ct).ConfigureAwait(false);
            if (notification == null)
            {
                await _repository.MarkDeliveryDeadAsync(task.Id, "notification_missing", ct).ConfigureAwait(false);
                return;
            }

            if (!_senders.TryGetValue(channel.Value, out var sender))
            {
                await _repository.MarkDeliveryDeadAsync(task.Id, "sender_not_found", ct).ConfigureAwait(false);
                return;
            }

            var binding = await _channelRepository.GetChannelAsync(task.UserId, channel.Value, ct).ConfigureAwait(false);
            if (binding == null)
            {
                await _repository.MarkDeliveryDeadAsync(task.Id, "channel_not_bound", ct).ConfigureAwait(false);
                return;
            }

            var rendered = _renderer.Render(notification.Template, notification.PayloadJson, notification.Category, notification.Severity);
            var context = new NotificationSendContext
            {
                NotificationId = task.NotificationId,
                UserId = task.UserId,
                Channel = channel.Value,
                Title = rendered.Title,
                Body = rendered.Body,
                ChannelBinding = binding
            };

            NotificationSendResult result;
            try
            {
                result = await sender.SendAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification send failed: deliveryId={DeliveryId}", task.Id);
                result = new NotificationSendResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }

            if (result.Success)
            {
                await _repository.MarkDeliverySuccessAsync(task.Id, ct).ConfigureAwait(false);
                return;
            }

            await HandleDeliveryFailureAsync(task.Id, attempt, result.ErrorMessage, ct).ConfigureAwait(false);
        }

        private async Task HandleDeliveryFailureAsync(long deliveryId, int attempt, string? error, CancellationToken ct)
        {
            var maxAttempts = Math.Max(1, _options.MaxAttempts);
            if (attempt >= maxAttempts)
            {
                await _repository.MarkDeliveryDeadAsync(deliveryId, error ?? "failed", ct).ConfigureAwait(false);
                return;
            }

            var delays = _options.RetryDelaysSeconds ?? Array.Empty<int>();
            var index = Math.Min(Math.Max(0, attempt - 1), Math.Max(0, delays.Length - 1));
            var seconds = delays.Length == 0 ? 60 : Math.Max(10, delays[index]);
            var nextRetryAt = DateTime.UtcNow.AddSeconds(seconds);

            await _repository.MarkDeliveryRetryAsync(deliveryId, nextRetryAt, error ?? "retry", ct).ConfigureAwait(false);
        }
    }
}
