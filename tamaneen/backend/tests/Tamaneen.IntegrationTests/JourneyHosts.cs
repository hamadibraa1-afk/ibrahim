using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tamaneen.AiGateway.Model;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Dev;
using Tamaneen.Api.Gate;
using Tamaneen.Infrastructure.Auth;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// The API and the crisis worker as one platform: same signing and server keys, the API's crisis gate and
/// console calls routed to the in-process worker, and a scripted language model. Sessions are enforced.
/// </summary>
public sealed partial class JourneyHosts : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiFactory _api;

    public JourneyHosts(SqlServerFixture sql)
    {
        var signing = RandomNumberGenerator.GetBytes(32);
        var serverKey = sql.ServerKey;
        var serviceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Worker = new WorkerFactory(sql, signing) { ServerKey = serverKey, ServiceKey = serviceKey, RequireSession = true };
        Worker.Clock.FollowRealTime();
        _api = new ApiFactory(sql, signing) { ServerKey = serverKey, GateServiceKey = serviceKey, RequireSession = true, GateBaseUrl = "http://localhost/" };
        Api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<ILanguageModel>(Model);
            s.AddHttpClient<CrisisGateClient>().ConfigurePrimaryHttpMessageHandler(() => Worker.Server.CreateHandler());
            s.AddHttpClient("crisis-console").ConfigurePrimaryHttpMessageHandler(() => Worker.Server.CreateHandler());
        }));
    }

    public WorkerFactory Worker { get; }

    public WebApplicationFactory<Tamaneen.Api.SharedResource> Api { get; }

    public ScriptedModel Model { get; } = new();

    public InMemoryMessageSender Outbox => (InMemoryMessageSender)Api.Services.GetRequiredService<IMessageSender>();

    public HttpClient ApiClient(string language, string? token = null)
    {
        var client = Api.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public HttpClient WorkerClient(string language, string token)
    {
        var client = Worker.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>The 6-digit code most recently "sent" to a contact.</summary>
    public string CodeSentTo(string contact) =>
        Digits().Match(Outbox.LastTo(contact)?.Text ?? throw new InvalidOperationException($"No message to {contact}")).Value;

    /// <summary>
    /// A fresh authenticator code for a seeded staff member. Each 30-second step is accepted once per account
    /// (replay protection) and the database outlives a single test, so steps are handed out in order across
    /// tests, starting one step back and waiting for the clock when the window is used up.
    /// </summary>
    public static string StaffTotp(string email)
    {
        lock (UsedSteps)
        {
            var step = Math.Max(Totp.StepAt(DateTimeOffset.UtcNow) - 1, UsedSteps.GetValueOrDefault(email, long.MinValue) + 1);
            while (step > Totp.StepAt(DateTimeOffset.UtcNow) + 1)
            {
                Thread.Sleep(500);
            }

            UsedSteps[email] = step;
            return Totp.Code(DevSeeder.StaffTotpSecret, step);
        }
    }

    private static readonly Dictionary<string, long> UsedSteps = [];

    /// <summary>Password + authenticator sign-in for a seeded staff member; returns the access token.</summary>
    public async Task<string> StaffSignInAsync(string email)
    {
        using var client = ApiClient("ar");
        var login = await Read(await client.PostAsJsonAsync("/v1/auth/login", new { contact = email, password = DevSeeder.StaffPassword, client = "staff" }));
        login.GetProperty("status").GetString().Should().Be("mfa");
        var mfa = await Read(await client.PostAsJsonAsync("/v1/auth/login/mfa", new { mfaToken = login.GetProperty("mfaToken").GetString(), method = "totp", code = StaffTotp(email), rememberDevice = false }));
        return mfa.GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    public static async Task<JsonElement> Problem(HttpResponseMessage response, int status)
    {
        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).Should().Be(status, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await _api.DisposeAsync();
        await Worker.DisposeAsync();
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex Digits();

    /// <summary>Answers in the language the system prompt demands; writes four paragraphs for summaries.</summary>
    public sealed class ScriptedModel : ILanguageModel
    {
        public List<LanguageModelRequest> Requests { get; } = [];

        public string ModelId => "scripted-model";

        public Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var arabic = request.System.Contains("in Arabic only", StringComparison.Ordinal) || request.System.Contains("Write in Arabic only", StringComparison.Ordinal);
            var summary = request.System.Contains("exactly four short paragraphs", StringComparison.Ordinal);
            var text = (summary, arabic) switch
            {
                (true, true) => "تشعر بضغط كبير في العمل منذ ثلاثة أشهر، ويصعب عليك النوم.\n---\nأن تتعلم طرقًا لتهدئة نفسك قبل النوم.\n---\nالحديث مع أختك والمشي في المساء.\n---\nلم تراجع مختصًا نفسيًا من قبل.",
                (true, false) => "You have felt a lot of pressure at work for about three months, and sleep is hard.\n---\nTo learn ways to calm yourself before bed.\n---\nTalking with your sister and walking in the evening.\n---\nYou have not seen a mental-health professional before.",
                (false, true) => "شكرًا لأنك شاركتني هذا. كيف يؤثر ذلك على نومك هذه الأيام؟",
                _ => "Thank you for sharing that. How has it been affecting your sleep lately?",
            };
            return Task.FromResult(new LanguageModelReply(text, LanguageModelStop.Completed, ModelId, 100, 40));
        }
    }
}
