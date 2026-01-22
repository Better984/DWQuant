namespace ServerTest.Services
{
    internal readonly record struct IndicatorPoint(long Timestamp, double Value);

    internal sealed class IndicatorSeries
    {
        private readonly object _lock = new();
        private readonly List<IndicatorPoint> _points = new();

        public IndicatorSeries(int capacity)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Capacity { get; private set; }

        public void EnsureCapacity(int capacity)
        {
            if (capacity <= Capacity)
            {
                return;
            }

            Capacity = capacity;
        }

        public void AddPoints(IReadOnlyList<IndicatorPoint> points)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                foreach (var point in points)
                {
                    if (_points.Count > 0 && _points[^1].Timestamp == point.Timestamp)
                    {
                        _points[^1] = point;
                        continue;
                    }

                    if (_points.Count == 0 || _points[^1].Timestamp < point.Timestamp)
                    {
                        _points.Add(point);
                    }
                }

                TrimExcess();
            }
        }

        public bool TryGetValue(int offset, out double value)
        {
            value = double.NaN;
            if (offset < 0)
            {
                return false;
            }

            lock (_lock)
            {
                var index = _points.Count - 1 - offset;
                if (index < 0 || index >= _points.Count)
                {
                    return false;
                }

                value = _points[index].Value;
                return !double.IsNaN(value);
            }
        }

        private void TrimExcess()
        {
            var overflow = _points.Count - Capacity;
            if (overflow <= 0)
            {
                return;
            }

            _points.RemoveRange(0, overflow);
        }
    }
}
