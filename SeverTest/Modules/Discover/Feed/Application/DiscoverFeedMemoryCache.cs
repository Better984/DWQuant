using ServerTest.Modules.Discover.Domain;

namespace ServerTest.Modules.Discover.Application
{
    /// <summary>
    /// 发现页资讯内存缓存（新闻 + 快讯）。
    /// 说明：数据库全量保留，内存仅保留最近 N 条。
    /// </summary>
    public sealed class DiscoverFeedMemoryCache
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private List<DiscoverFeedItem> _articleDesc = new();
        private List<DiscoverFeedItem> _newsflashDesc = new();

        public void Replace(DiscoverFeedKind kind, IReadOnlyList<DiscoverFeedItem> itemsDesc)
        {
            _lock.EnterWriteLock();
            try
            {
                var copy = itemsDesc.Select(Clone).ToList();
                if (kind == DiscoverFeedKind.Article)
                {
                    _articleDesc = copy;
                    return;
                }

                _newsflashDesc = copy;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<DiscoverFeedItem> GetLatestDesc(DiscoverFeedKind kind, int limit)
        {
            _lock.EnterReadLock();
            try
            {
                var source = kind == DiscoverFeedKind.Article ? _articleDesc : _newsflashDesc;
                return source.Take(limit).Select(Clone).ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IReadOnlyList<DiscoverFeedItem> GetAfterIdAsc(DiscoverFeedKind kind, long latestId, int limit)
        {
            _lock.EnterReadLock();
            try
            {
                var source = kind == DiscoverFeedKind.Article ? _articleDesc : _newsflashDesc;
                return source
                    .Where(item => item.Id > latestId)
                    .OrderBy(item => item.Id)
                    .Take(limit)
                    .Select(Clone)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IReadOnlyList<DiscoverFeedItem> GetBeforeIdDesc(DiscoverFeedKind kind, long beforeId, int limit)
        {
            _lock.EnterReadLock();
            try
            {
                var source = kind == DiscoverFeedKind.Article ? _articleDesc : _newsflashDesc;
                return source
                    .Where(item => item.Id < beforeId)
                    .OrderByDescending(item => item.Id)
                    .Take(limit)
                    .Select(Clone)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public (long LatestId, long OldestId) GetIdBounds(DiscoverFeedKind kind)
        {
            _lock.EnterReadLock();
            try
            {
                var source = kind == DiscoverFeedKind.Article ? _articleDesc : _newsflashDesc;
                if (source.Count == 0)
                {
                    return (0, 0);
                }

                return (source[0].Id, source[^1].Id);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        private static DiscoverFeedItem Clone(DiscoverFeedItem item)
        {
            return new DiscoverFeedItem
            {
                Id = item.Id,
                DedupeKey = item.DedupeKey,
                Title = item.Title,
                Summary = item.Summary,
                ContentHtml = item.ContentHtml,
                SourceName = item.SourceName,
                SourceLogo = item.SourceLogo,
                PictureUrl = item.PictureUrl,
                ReleaseTime = item.ReleaseTime,
                RawPayloadJson = item.RawPayloadJson,
                CreatedAt = item.CreatedAt
            };
        }
    }
}
