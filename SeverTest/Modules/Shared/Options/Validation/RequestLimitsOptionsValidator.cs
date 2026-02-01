using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>
    {
        public ValidateOptionsResult Validate(string? name, RequestLimitsOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("RequestLimits 配置不能为空");
            }

            if (options.DefaultMaxBodyBytes <= 0)
            {
                return ValidateOptionsResult.Fail("RequestLimits.DefaultMaxBodyBytes 必须大于 0");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
