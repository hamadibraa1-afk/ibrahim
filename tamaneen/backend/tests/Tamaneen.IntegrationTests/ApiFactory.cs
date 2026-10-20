using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Tamaneen.Api;

namespace Tamaneen.IntegrationTests;

/// <summary>Runs the real API host against the test database with test-only keys.</summary>
public sealed class ApiFactory : WebApplicationFactory<SharedResource>
{
    private const string Issuer = "https://tests.tamaneen.local";
    private const string Audience = "tamaneen-api";
    private readonly SqlServerFixture _sql;
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    public ApiFactory(SqlServerFixture sql) => _sql = sql;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _sql.ConnectionString);
        builder.UseSetting("Encryption:Provider", "Local");
        builder.UseSetting("Encryption:LocalKeyBase64", Convert.ToBase64String(_sql.LocalKek));
        builder.UseSetting("Pseudonymization:ContactPepperBase64", Convert.ToBase64String(_sql.Pepper));
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);
        builder.UseSetting("Auth:SigningKeyBase64", Convert.ToBase64String(_signingKey));
    }

    public HttpClient ClientFor(Guid userId, string role, string pseudonym)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId, role, pseudonym));
        return client;
    }

    public string Token(Guid userId, string role, string pseudonym, TimeSpan? lifetime = null)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10));
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            new[] { new Claim("sub", userId.ToString()), new Claim("role", role), new Claim("pid", pseudonym) },
            notBefore: expires.AddMinutes(-15),
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
