using ServerTest.Modules.Discover.Domain;

namespace ServerTest.Modules.Discover.Application
{
    /// <summary>
    /// Discover 日历内存缓存（央行活动 / 财经事件 / 经济数据）。
    /// 说明：数据库全量保留，内存仅保留最近 N 条。
    /// </summary>
    public sealed class DiscoverCalendarMemoryCache
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private List<DiscoverCalendarItem> _centralBankDesc = new();
        private List<DiscoverCalendarItem> _financialDesc = new();
        private List<DiscoverCalendarItem> _economicDesc = new();

        public void Replace(DiscoverCalendarKind kind, IReadOnlyList<DiscoverCalendarItem> itemsDesc)
        {
            _lock.EnterWriteLock();
            try
            {
                var copy = itemsDesc.Select(Clone).ToList();
                switch (kind)
                {
                    case DiscoverCalendarKind.CentralBankActivities:
                        _centralBankDesc = copy;
                        break;
                    case DiscoverCalendarKind.FinancialEvents:
                        _financialDesc = copy;
                        break;
                    case DiscoverCalendarKind.EconomicData:
                        _economicDesc = copy;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知日历类型");
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<DiscoverCalendarItem> GetLatestDesc(DiscoverCalendarKind kind, int limit)
        {
            _lock.EnterReadLock();
            try
            {
                return ResolveList(kind)
                    .Take(limit)
                    .Select(Clone)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private IReadOnlyList<DiscoverCalendarItem> ResolveList(DiscoverCalendarKind kind)
        {
            return kind switch
            {
                DiscoverCalendarKind.CentralBankActivities => _centralBankDesc,
                DiscoverCalendarKind.FinancialEvents => _financialDesc,
                DiscoverCalendarKind.EconomicData => _economicDesc,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知日历类型")
            };
        }

        private static DiscoverCalendarItem Clone(DiscoverCalendarItem item)
        {
            return new DiscoverCalendarItem
            {
                Id = item.Id,
                DedupeKey = item.DedupeKey,
                CalendarName = item.CalendarName,
                CountryCode = item.CountryCode,
                CountryName = item.CountryName,
                PublishTimestamp = item.PublishTimestamp,
                ImportanceLevel = item.ImportanceLevel,
                HasExactPublishTime = item.HasExactPublishTime,
                DataEffect = item.DataEffect,
                ForecastValue = item.ForecastValue,
                PreviousValue = item.PreviousValue,
                RevisedPreviousValue = item.RevisedPreviousValue,
                PublishedValue = item.PublishedValue,
                RawPayloadJson = item.RawPayloadJson,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            };
        }
    }
}
