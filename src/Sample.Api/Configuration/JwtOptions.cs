using System.ComponentModel.DataAnnotations;

namespace Sample.Api.Configuration;

/// <summary>
/// Token validation. Two modes: <see cref="Authority"/> (OpenID Connect discovery, asymmetric keys, the production
/// setup) or <see cref="SigningKey"/> (shared HMAC key, used by local clusters and preview environments).
/// </summary>
internal sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    [Url]
    public string? Authority { get; set; }

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [MinLength(32, ErrorMessage = "Authentication:Jwt:SigningKey must be at least 32 characters (256 bits).")]
    public string? SigningKey { get; set; }
}
