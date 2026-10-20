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
    private readonly byte[] _signingKey;

    public ApiFactory(SqlServerFixture sql, byte[]? signingKey = null)
    {
        _sql = sql;
        _signingKey = signingKey ?? RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>Base64 server key (sign-up payloads, authenticator secrets); shared with the worker in journey tests.</summary>
    public string ServerKey { get; init; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Where the crisis gate lives; journey tests point it at an in-process worker.</summary>
    public string GateBaseUrl { get; init; } = "http://crisis-worker.test/";

    public string GateServiceKey { get; init; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Tokens minted by <see cref="Token"/> have no session; real sign-ins are tested with this on.</summary>
    public bool RequireSession { get; init; }

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
        builder.UseSetting("Auth:ServerKeyBase64", ServerKey);
        builder.UseSetting("Auth:RequireSession", RequireSession ? "true" : "false");
        builder.UseSetting("Platform:EmergencyNumber", "911");
        builder.UseSetting("Platform:SupportLine", "920033360");
        builder.UseSetting("Platform:RegionNameAr", "منطقة التجربة");
        builder.UseSetting("Platform:RegionNameEn", "Pilot region");
        builder.UseSetting("Platform:Cities:0:Code", "RUH");
        builder.UseSetting("Platform:Cities:0:NameAr", "الرياض");
        builder.UseSetting("Platform:Cities:0:NameEn", "Riyadh");
        builder.UseSetting("Platform:VideoBaseUrl", "https://meet.tamaneen.test/room");
        builder.UseSetting("CrisisGate:BaseUrl", GateBaseUrl);
        builder.UseSetting("CrisisGate:ServiceKeyBase64", GateServiceKey);
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
        builder.UseSetting("RateLimits:AiPerMinute", "1000");
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
