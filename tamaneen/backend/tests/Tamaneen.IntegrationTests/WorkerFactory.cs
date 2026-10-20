using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Tamaneen.Crisis.Worker;
using Tamaneen.Crisis.Worker.Hubs;
using Tamaneen.Crisis.Worker.Paging;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

/// <summary>Runs the real Crisis.Worker host on the test database, with a movable clock and a captured paging channel.</summary>
public sealed class WorkerFactory : WebApplicationFactory<CrisisResource>
{
    private const string Issuer = "https://tests.tamaneen.local";
    private const string Audience = "tamaneen-api";
    private readonly SqlServerFixture _sql;
    private readonly byte[] _signingKey;

    public WorkerFactory(SqlServerFixture sql, byte[]? signingKey = null)
    {
        _sql = sql;
        _signingKey = signingKey ?? RandomNumberGenerator.GetBytes(32);
    }

    public string ServerKey { get; init; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public bool RequireSession { get; init; }

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero));

    public CapturingHandler Pager { get; } = new();

    public string ServiceKey { get; init; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public byte[] PagingSecret { get; } = RandomNumberGenerator.GetBytes(32);

    public Guid ServiceUserId { get; } = Guid.NewGuid();

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
        builder.UseSetting("Crisis:Contacts:EmergencyNumber", "911");
        builder.UseSetting("Crisis:Contacts:SupportLine", "920033360");
        builder.UseSetting("Crisis:Worker:ServiceUserId", ServiceUserId.ToString());
        builder.UseSetting("Crisis:Worker:ServiceKeyBase64", ServiceKey);
        builder.UseSetting("Crisis:Worker:RunBackgroundLoops", "false");
        builder.UseSetting("Crisis:Worker:ConsoleBaseUrl", "https://staff.tamaneen.test");
        builder.UseSetting("Crisis:Paging:WebhookUrl", "https://pager.tamaneen.test/hooks/page");
        builder.UseSetting("Crisis:Paging:WebhookSecretBase64", Convert.ToBase64String(PagingSecret));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.AddHttpClient<IPagerChannel, WebhookPagerChannel>().ConfigurePrimaryHttpMessageHandler(() => Pager);
        });
    }

    public HttpClient ServiceClient(string? key = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tamaneen-Service-Key", key ?? ServiceKey);
        return client;
    }

    public HttpClient StaffClient(Guid userId, string role, string language)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId, role));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        return client;
    }

    public async Task<(HubConnection Connection, AlertInbox Inbox)> ConnectConsoleAsync(Guid userId, string role, string language)
    {
        var inbox = new AlertInbox();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, $"{CrisisHub.Path.TrimStart('/')}?lang={language}"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(Token(userId, role));
            })
            .Build();
        connection.On<CrisisAlertMessage>(SignalRCrisisAlertPublisher.AlertMethod, inbox.Add);
        await connection.StartAsync();
        return (connection, inbox);
    }

    public string Token(Guid userId, string role)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Audience, new[] { new Claim("sub", userId.ToString()), new Claim("role", role) },
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class MutableClock : IClock
{
    private DateTimeOffset _now;
    private TimeSpan? _realOffset;

    public MutableClock(DateTimeOffset start) => _now = start;

    public DateTimeOffset UtcNow => _realOffset is { } offset ? DateTimeOffset.UtcNow + offset : _now;

    public void Advance(TimeSpan by)
    {
        if (_realOffset is { } offset)
        {
            _realOffset = offset + by;
        }
        else
        {
            _now += by;
        }
    }

    /// <summary>Journey tests: run on real time (authenticator codes depend on it); Advance still moves it.</summary>
    public void FollowRealTime() => _realOffset = TimeSpan.Zero;
}

/// <summary>Stands in for the network: records each page and answers with the configured status.</summary>
public sealed class CapturingHandler : HttpMessageHandler
{
    private readonly List<(HttpRequestMessage Request, string Body)> _requests = new();

    public HttpStatusCode Answer { get; set; } = HttpStatusCode.Accepted;

    public IReadOnlyList<(HttpRequestMessage Request, string Body)> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add((request, body));
        }

        return new HttpResponseMessage(Answer);
    }
}

/// <summary>Collects console messages and lets a test wait for one.</summary>
public sealed class AlertInbox
{
    private readonly List<CrisisAlertMessage> _messages = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Add(CrisisAlertMessage message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        _signal.Release();
    }

    public async Task<CrisisAlertMessage> WaitForAsync(Func<CrisisAlertMessage, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            lock (_messages)
            {
                var found = _messages.FirstOrDefault(match);
                if (found is not null)
                {
                    return found;
                }
            }

            await _signal.WaitAsync(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException("Expected console message did not arrive.");
    }
}
