using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sample.Api.Configuration;

namespace Sample.Api.Security;

internal static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.Authority = jwt.Authority;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    NameClaimType = Claims.Subject,
                    RoleClaimType = Claims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    IssuerSigningKey = jwt.SigningKey is null
                        ? null
                        : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Manager, policy => policy.RequireAuthenticatedUser().RequireRole(Claims.ManagerRole));

        return services;
    }
}
