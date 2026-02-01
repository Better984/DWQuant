using System.Text.Json;

namespace ServerTest.Models.Indicator
{
    public sealed class TalibIndicatorCatalog
    {
        private readonly Dictionary<string, TalibIndicatorDefinition> _definitions;

        private TalibIndicatorCatalog(Dictionary<string, TalibIndicatorDefinition> definitions)
        {
            _definitions = definitions;
        }

        public static TalibIndicatorCatalog LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("talib indicator config not found", path);
            }

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var root = JsonSerializer.Deserialize<TalibIndicatorRoot>(json, options);
            var defs = root?.Indicators?
                .Where(i => !string.IsNullOrWhiteSpace(i.Code))
                .ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, TalibIndicatorDefinition>(StringComparer.OrdinalIgnoreCase);

            return new TalibIndicatorCatalog(defs);
        }

        public bool TryGet(string code, out TalibIndicatorDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                definition = null!;
                return false;
            }

            return _definitions.TryGetValue(code, out definition!);
        }

        private sealed class TalibIndicatorRoot
        {
            public List<TalibIndicatorDefinition> Indicators { get; set; } = new();
        }
    }

    public sealed class TalibIndicatorDefinition
    {
        public string Code { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public TalibIndicatorInputs Inputs { get; set; } = new();
        public List<TalibIndicatorOutput> Outputs { get; set; } = new();
    }

    public sealed class TalibIndicatorInputs
    {
        public string Shape { get; set; } = string.Empty;
        public List<string> Series { get; set; } = new();
    }

    public sealed class TalibIndicatorOutput
    {
        public string Key { get; set; } = string.Empty;
        public string Hint { get; set; } = string.Empty;
    }
}
