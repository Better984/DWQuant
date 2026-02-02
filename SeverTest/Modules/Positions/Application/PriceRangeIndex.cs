using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerTest.Modules.Positions.Application
{
    internal sealed class PriceRangeIndex
    {
        // 价格区间索引：按数量级分桶（粗/中/细三层），用于快速定位落在区间内的阈值价格
        private readonly Dictionary<int, ScaleBuckets> _scales = new();
        private readonly Dictionary<long, IndexLocation> _locations = new();

        public IndexTreeSnapshot BuildSnapshot(string indexType)
        {
            var snapshot = new IndexTreeSnapshot
            {
                IndexType = indexType,
            };

            var count = 0;
            foreach (var scale in _scales.OrderBy(item => item.Key))
            {
                var scaleSnapshot = scale.Value.BuildSnapshot(scale.Key);
                snapshot.Scales.Add(scaleSnapshot);
                count = SafeAdd(count, scaleSnapshot.Count);
            }

            snapshot.Count = count;
            return snapshot;
        }

        public void Upsert(long positionId, decimal price)
        {
            if (positionId <= 0 || price <= 0)
            {
                return;
            }

            Remove(positionId);

            var scale = GetScale(price);
            if (!_scales.TryGetValue(scale, out var buckets))
            {
                buckets = new ScaleBuckets(scale);
                _scales[scale] = buckets;
            }

            var location = buckets.Add(positionId, price);
            _locations[positionId] = location;
        }

        public void Remove(long positionId)
        {
            if (!_locations.TryGetValue(positionId, out var location))
            {
                return;
            }

            if (_scales.TryGetValue(location.Scale, out var buckets))
            {
                buckets.Remove(location, positionId);
                if (buckets.IsEmpty)
                {
                    _scales.Remove(location.Scale);
                }
            }

            _locations.Remove(positionId);
        }

        public void Query(decimal low, decimal high, ISet<long> results)
        {
            if (results == null)
            {
                return;
            }

            if (high <= 0)
            {
                return;
            }

            if (low > high)
            {
                (low, high) = (high, low);
            }

            if (low < 0)
            {
                low = 0;
            }

            foreach (var buckets in _scales.Values)
            {
                buckets.Query(low, high, results);
            }
        }

        private static int GetScale(decimal price)
        {
            if (price <= 0)
            {
                return 0;
            }

            var abs = Math.Abs((double)price);
            if (abs <= 0)
            {
                return 0;
            }

            return (int)Math.Floor(Math.Log10(abs));
        }

        private readonly struct IndexLocation
        {
            public IndexLocation(int scale, long level1Key, long level2Key, long level3Key)
            {
                Scale = scale;
                Level1Key = level1Key;
                Level2Key = level2Key;
                Level3Key = level3Key;
            }

            public int Scale { get; }
            public long Level1Key { get; }
            public long Level2Key { get; }
            public long Level3Key { get; }
        }

        private sealed class ScaleBuckets
        {
            private readonly Dictionary<long, Level1Bucket> _level1 = new();
            private readonly decimal _step1;
            private readonly decimal _step2;
            private readonly decimal _step3;

            public ScaleBuckets(int scale)
            {
                _step1 = Pow10(scale - 1);
                if (_step1 <= 0)
                {
                    _step1 = 1m;
                }

                _step2 = _step1 / 10m;
                if (_step2 <= 0)
                {
                    _step2 = _step1;
                }

                _step3 = _step2 / 10m;
                if (_step3 <= 0)
                {
                    _step3 = _step2;
                }

                Scale = scale;
            }

            public int Scale { get; }

            public bool IsEmpty => _level1.Count == 0;

            public IndexLocation Add(long positionId, decimal price)
            {
                var level1Key = GetBucketKey(price, _step1);
                var level1Base = level1Key * _step1;
                var level2Key = GetBucketKey(price - level1Base, _step2);
                var level2Base = level1Base + level2Key * _step2;
                var level3Key = GetBucketKey(price - level2Base, _step3);

                if (!_level1.TryGetValue(level1Key, out var bucket1))
                {
                    bucket1 = new Level1Bucket();
                    _level1[level1Key] = bucket1;
                }

                bucket1.Add(level2Key, level3Key, positionId);
                return new IndexLocation(Scale, level1Key, level2Key, level3Key);
            }

            public void Remove(IndexLocation location, long positionId)
            {
                if (!_level1.TryGetValue(location.Level1Key, out var bucket1))
                {
                    return;
                }

                if (!bucket1.Remove(location.Level2Key, location.Level3Key, positionId, out var level1Empty))
                {
                    return;
                }

                if (level1Empty)
                {
                    _level1.Remove(location.Level1Key);
                }
            }

            public void Query(decimal low, decimal high, ISet<long> results)
            {
                if (_level1.Count == 0)
                {
                    return;
                }

                var level1Start = GetBucketKey(low, _step1);
                var level1End = GetBucketKeyForHigh(low, high, _step1);

                // 只遍历命中的桶范围，避免全量扫描
                for (var level1Key = level1Start; level1Key <= level1End; level1Key++)
                {
                    if (!_level1.TryGetValue(level1Key, out var bucket1))
                    {
                        continue;
                    }

                    var level1Base = level1Key * _step1;
                    var level1Low = Math.Max(low, level1Base);
                    var level1High = Math.Min(high, level1Base + _step1);
                    var level2Start = GetBucketKey(level1Low - level1Base, _step2);
                    var level2End = GetBucketKeyForHigh(level1Low - level1Base, level1High - level1Base, _step2);

                    bucket1.Query(level2Start, level2End, level1Base, level1Low, level1High, _step2, _step3, results);
                }
            }

            public ScaleTreeSnapshot BuildSnapshot(int scale)
            {
                var snapshot = new ScaleTreeSnapshot
                {
                    Scale = scale,
                    Step1 = _step1,
                    Step2 = _step2,
                    Step3 = _step3
                };

                var count = 0;
                foreach (var level1 in _level1.OrderBy(item => item.Key))
                {
                    var level1Key = level1.Key;
                    var level1Base = level1Key * _step1;
                    var level1Snapshot = level1.Value.BuildSnapshot(level1Key, level1Base, _step1, _step2, _step3);
                    snapshot.Level1.Add(level1Snapshot);
                    count = SafeAdd(count, level1Snapshot.Count);
                }

                snapshot.Count = count;
                return snapshot;
            }

            private static decimal Pow10(int exponent)
            {
                var result = 1m;
                if (exponent >= 0)
                {
                    for (var i = 0; i < exponent; i++)
                    {
                        result *= 10m;
                    }

                    return result;
                }

                for (var i = 0; i < -exponent; i++)
                {
                    result /= 10m;
                }

                return result;
            }
        }

        private sealed class Level1Bucket
        {
            private readonly Dictionary<long, Level2Bucket> _level2 = new();

            public void Add(long level2Key, long level3Key, long positionId)
            {
                if (!_level2.TryGetValue(level2Key, out var bucket2))
                {
                    bucket2 = new Level2Bucket();
                    _level2[level2Key] = bucket2;
                }

                bucket2.Add(level3Key, positionId);
            }

            public bool Remove(long level2Key, long level3Key, long positionId, out bool level1Empty)
            {
                level1Empty = false;
                if (!_level2.TryGetValue(level2Key, out var bucket2))
                {
                    return false;
                }

                if (!bucket2.Remove(level3Key, positionId, out var level2Empty))
                {
                    return false;
                }

                if (level2Empty)
                {
                    _level2.Remove(level2Key);
                }

                level1Empty = _level2.Count == 0;
                return true;
            }

            public void Query(
                long level2Start,
                long level2End,
                decimal level1Base,
                decimal level1Low,
                decimal level1High,
                decimal step2,
                decimal step3,
                ISet<long> results)
            {
                for (var level2Key = level2Start; level2Key <= level2End; level2Key++)
                {
                    if (!_level2.TryGetValue(level2Key, out var bucket2))
                    {
                        continue;
                    }

                    var level2Base = level1Base + level2Key * step2;
                    var level2Low = Math.Max(level1Low, level2Base);
                    var level2High = Math.Min(level1High, level2Base + step2);
                    var level3Start = GetBucketKey(level2Low - level2Base, step3);
                    var level3End = GetBucketKeyForHigh(level2Low - level2Base, level2High - level2Base, step3);

                    bucket2.Query(level3Start, level3End, results);
                }
            }

            public Level1TreeSnapshot BuildSnapshot(
                long level1Key,
                decimal level1Base,
                decimal step1,
                decimal step2,
                decimal step3)
            {
                var snapshot = new Level1TreeSnapshot
                {
                    Key = level1Key,
                    Low = level1Base,
                    High = level1Base + step1
                };

                var count = 0;
                foreach (var level2 in _level2.OrderBy(item => item.Key))
                {
                    var level2Key = level2.Key;
                    var level2Base = level1Base + level2Key * step2;
                    var level2Snapshot = level2.Value.BuildSnapshot(level2Key, level2Base, step2, step3);
                    snapshot.Level2.Add(level2Snapshot);
                    count = SafeAdd(count, level2Snapshot.Count);
                }

                snapshot.Count = count;
                return snapshot;
            }
        }

        private sealed class Level2Bucket
        {
            private readonly Dictionary<long, HashSet<long>> _level3 = new();

            public void Add(long level3Key, long positionId)
            {
                if (!_level3.TryGetValue(level3Key, out var bucket3))
                {
                    bucket3 = new HashSet<long>();
                    _level3[level3Key] = bucket3;
                }

                bucket3.Add(positionId);
            }

            public bool Remove(long level3Key, long positionId, out bool level2Empty)
            {
                level2Empty = false;
                if (!_level3.TryGetValue(level3Key, out var bucket3))
                {
                    return false;
                }

                bucket3.Remove(positionId);
                if (bucket3.Count == 0)
                {
                    _level3.Remove(level3Key);
                }
                level2Empty = _level3.Count == 0;
                return true;
            }

            public void Query(long level3Start, long level3End, ISet<long> results)
            {
                for (var level3Key = level3Start; level3Key <= level3End; level3Key++)
                {
                    if (!_level3.TryGetValue(level3Key, out var bucket3))
                    {
                        continue;
                    }

                    foreach (var positionId in bucket3)
                    {
                        results.Add(positionId);
                    }
                }
            }

            public Level2TreeSnapshot BuildSnapshot(long level2Key, decimal level2Base, decimal step2, decimal step3)
            {
                var snapshot = new Level2TreeSnapshot
                {
                    Key = level2Key,
                    Low = level2Base,
                    High = level2Base + step2
                };

                var count = 0;
                foreach (var level3 in _level3.OrderBy(item => item.Key))
                {
                    var level3Key = level3.Key;
                    var level3Base = level2Base + level3Key * step3;
                    var ids = level3.Value.OrderBy(id => id).ToList();
                    var level3Snapshot = new Level3TreeSnapshot
                    {
                        Key = level3Key,
                        Low = level3Base,
                        High = level3Base + step3,
                        PositionIds = ids,
                        Count = ids.Count
                    };
                    snapshot.Level3.Add(level3Snapshot);
                    count = SafeAdd(count, level3Snapshot.Count);
                }

                snapshot.Count = count;
                return snapshot;
            }
        }

        private static long GetBucketKey(decimal value, decimal step)
        {
            if (step <= 0)
            {
                return 0;
            }

            if (value < 0)
            {
                value = 0;
            }

            return (long)decimal.Floor(value / step);
        }

        private static long GetBucketKeyForHigh(decimal low, decimal high, decimal step)
        {
            if (step <= 0)
            {
                return 0;
            }

            return GetBucketKey(high, step);
        }

        private static int SafeAdd(int left, int right)
        {
            var sum = left + (long)right;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }
    }
}
