using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Tests;

/// <summary>The real pipeline over a throwaway SQLite file, with a clock the test controls.</summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"proposals-test-{Guid.NewGuid():N}.db");

    private readonly Dictionary<string, string> _settings = [];

    public TestHost(params (string Key, string Value)[] settings)
    {
        foreach (var (key, value) in settings)
            _settings[key] = value;
    }

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string AttachmentDir { get; } = Path.Combine(Path.GetTempPath(), $"att-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("Attachments:StoragePath", AttachmentDir);
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            // Tests drive SLA passes explicitly.
            var monitor = services.Single(d => d.ImplementationType == typeof(SlaMonitorService));
            services.Remove(monitor);
        });
    }

    public async Task<ApiClient> LoginAsync(string userCode, string password = DbSeeder.DemoPassword)
    {
        var client = new ApiClient(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") }));
        var res = await client.PostAsync("/api/auth/login", new { userCode, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    public async Task<SlaRunResult> RunSlaAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SlaEscalationService>().RunAsync(CancellationToken.None);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
        try { if (Directory.Exists(AttachmentDir)) Directory.Delete(AttachmentDir, true); } catch (IOException) { }
    }
}

/// <summary>Plays the browser: keeps cookies and echoes the CSRF cookie in X-XSRF-TOKEN like Angular does.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Dictionary<string, string> Cookies { get; } = [];
    public bool SendCsrf { get; set; } = true;
    public HttpResponseMessage? Last { get; private set; }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url, null);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body ?? new { });
    public Task<HttpResponseMessage> PutAsync(string url, object body) => SendAsync(HttpMethod.Put, url, body);
    public Task<HttpResponseMessage> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url, null);

    /// <summary>Uploads files as multipart/form-data under the "files" field, like the SPA does.</summary>
    public Task<HttpResponseMessage> UploadAsync(string url, params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, content) in files)
            form.Add(new ByteArrayContent(content), "files", name);
        return SendAsync(HttpMethod.Post, url, form);
    }

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var res = await GetAsync(url);
        Assert.True(res.IsSuccessStatusCode, $"GET {url} -> {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<JsonElement> PostJsonAsync(string url, object? body = null)
    {
        var res = await PostAsync(url, body);
        Assert.True(res.IsSuccessStatusCode, $"POST {url} -> {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is HttpContent content)
            req.Content = content;
        else if (body is not null)
            req.Content = JsonContent.Create(body, options: Json);
        if (Cookies.Count > 0)
            req.Headers.Add("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
        if (SendCsrf && Cookies.TryGetValue("XSRF-TOKEN", out var csrf))
            req.Headers.Add("X-XSRF-TOKEN", csrf);

        var res = await http.SendAsync(req);
        if (res.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var pair = sc.Split(';')[0];
                var eq = pair.IndexOf('=', StringComparison.Ordinal);
                var (name, value) = (pair[..eq], pair[(eq + 1)..]);
                if (value.Length == 0 || sc.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                    Cookies.Remove(name);
                else
                    Cookies[name] = value;
            }
        }
        Last = res;
        return res;
    }
}
