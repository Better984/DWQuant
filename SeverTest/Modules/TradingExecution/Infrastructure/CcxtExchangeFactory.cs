using ccxt;
using System.Collections.Generic;
using ServerTest.Modules.ExchangeApiKeys.Domain;

namespace ServerTest.Modules.TradingExecution.Infrastructure
{
    public static class CcxtExchangeFactory
    {
        public static Exchange Create(string exchangeType, UserExchangeApiKeyRecord apiKey)
        {
            var options = new Dictionary<string, object>
            {
                ["apiKey"] = apiKey.ApiKey,
                ["secret"] = apiKey.ApiSecret,
                ["enableRateLimit"] = true,
                ["options"] = new Dictionary<string, object>
                {
                    ["defaultType"] = "swap"
                }
            };

            if (!string.IsNullOrWhiteSpace(apiKey.ApiPassword))
            {
                options["password"] = apiKey.ApiPassword;
            }

            return exchangeType switch
            {
                "binance" => new binanceusdm(options),
                "okx" => new okx(options),
                "bitget" => new bitget(options),
                "bybit" => new bybit(options),
                "gate" => new gate(options),
                _ => throw new NotSupportedException($"Exchange not supported: {exchangeType}")
            };
        }

        public static void ConfigureSandbox(Exchange exchangeClient, string exchangeType, bool enableSandbox)
        {
            if (!enableSandbox)
            {
                return;
            }

            if (IsBitget(exchangeType))
            {
                EnableBitgetDemoTrading(exchangeClient);
                return;
            }

            exchangeClient.setSandboxMode(true);
        }

        public static bool IsBitget(string exchangeType)
        {
            return string.Equals(exchangeType, "bitget", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnableBitgetDemoTrading(Exchange exchangeClient)
        {
            try
            {
                var prop = exchangeClient.GetType().GetProperty("headers");
                if (prop == null)
                {
                    return;
                }

                var headersObj = prop.GetValue(exchangeClient);
                if (headersObj == null)
                {
                    headersObj = new Dictionary<string, object>();
                    prop.SetValue(exchangeClient, headersObj);
                }

                if (headersObj is IDictionary<string, string> strHeaders)
                {
                    strHeaders["paptrading"] = "1";
                    return;
                }

                if (headersObj is IDictionary<string, object> objHeaders)
                {
                    objHeaders["paptrading"] = "1";
                    return;
                }

                dynamic headers = headersObj;
                headers["paptrading"] = "1";
            }
            catch
            {
                // ignore
            }
        }
    }
}
