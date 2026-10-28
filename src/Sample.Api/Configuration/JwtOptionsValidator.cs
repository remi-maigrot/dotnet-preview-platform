using Microsoft.Extensions.Options;

namespace Sample.Api.Configuration;

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        (string.IsNullOrWhiteSpace(options.Authority), string.IsNullOrWhiteSpace(options.SigningKey)) switch
        {
            (true, true) => ValidateOptionsResult.Fail(
                "Configure either Authentication:Jwt:Authority (OpenID Connect) or Authentication:Jwt:SigningKey (shared key)."),
            (false, false) => ValidateOptionsResult.Fail(
                "Authentication:Jwt:Authority and Authentication:Jwt:SigningKey are mutually exclusive."),
            _ => ValidateOptionsResult.Success,
        };
}
