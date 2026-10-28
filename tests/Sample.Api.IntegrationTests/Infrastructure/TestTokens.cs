using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Sample.Api.IntegrationTests.Infrastructure;

internal static class TestTokens
{
    public static string For(Guid employeeId, bool manager = false, string signingKey = ApiFactory.SigningKey)
    {
        var claims = new List<Claim> { new("sub", employeeId.ToString()) };
        if (manager)
        {
            claims.Add(new Claim("role", "manager"));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "preview-platform",
            Audience = "sample-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
