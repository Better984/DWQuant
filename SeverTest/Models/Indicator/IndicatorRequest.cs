using System;

namespace ServerTest.Models.Indicator
{
    public sealed class IndicatorRequest
    {
        public IndicatorRequest(IndicatorKey key, double[] parameters, int maxOffset)
        {
            Key = key;
            Parameters = parameters ?? Array.Empty<double>();
            MaxOffset = Math.Max(0, maxOffset);
        }

        public IndicatorKey Key { get; }
        public double[] Parameters { get; }
        public int MaxOffset { get; }

        public IndicatorRequest WithMaxOffset(int maxOffset)
        {
            return maxOffset <= MaxOffset ? this : new IndicatorRequest(Key, Parameters, maxOffset);
        }
    }
}
