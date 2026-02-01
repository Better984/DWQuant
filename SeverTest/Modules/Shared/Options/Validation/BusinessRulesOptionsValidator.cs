using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class BusinessRulesOptionsValidator : IValidateOptions<BusinessRulesOptions>
    {
        public ValidateOptionsResult Validate(string? name, BusinessRulesOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("BusinessRules 配置不能为空");
            }

            var failures = new List<string>();

            if (options.SuperAdminRole < 0)
            {
                failures.Add("BusinessRules.SuperAdminRole 不能小于 0");
            }

            if (options.MaxKeysPerExchange <= 0)
            {
                failures.Add("BusinessRules.MaxKeysPerExchange 必须大于 0");
            }

            if (options.ShareCodeLength <= 0)
            {
                failures.Add("BusinessRules.ShareCodeLength 必须大于 0");
            }
            else if (options.ShareCodeLength < 2 || options.ShareCodeLength % 2 != 0)
            {
                failures.Add("BusinessRules.ShareCodeLength 必须为大于等于 2 的偶数");
            }

            if (string.IsNullOrWhiteSpace(options.ShareCodeAlphabet))
            {
                failures.Add("BusinessRules.ShareCodeAlphabet 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
