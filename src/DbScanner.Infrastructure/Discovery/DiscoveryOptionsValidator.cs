using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryOptionsValidator : IValidateOptions<DiscoveryOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscoveryOptions options)
    {
        var failures = DiscoveryOptions.Validate(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
