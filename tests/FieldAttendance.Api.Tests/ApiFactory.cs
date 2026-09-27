using System.Net.Http.Headers;
using FieldAttendance.Api.Auth;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Api.Features.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// The real API over a real SQL Server database holding the demo data. SQL Server rather than
/// an in-memory provider because the behaviour under test runs through EF translations, row
/// versions and the UTC converter, none of which an in-memory store reproduces.
///
/// Set FIELDATTENDANCE_TEST_DB to point elsewhere; the default is the README's SQL Express with
/// a database of its own, so running the tests never touches the development database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Tuesday 22 September 2026, 09:30 in Dubai: inside the office shift (08:00–16:00, Sun–Thu),
    /// so a check-in test always has a shift to aim at, whatever time the suite runs.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 22, 5, 30, 0, TimeSpan.Zero);

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("FIELDATTENDANCE_TEST_DB")
        ?? @"Server=localhost\SQLEXPRESS;Database=FieldAttendance_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("ResetDatabaseOnStart", "true");
        builder.UseSetting("SeedDemoData", "true");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FrozenTime(Now));
            // The workers would close and re-mark records on their own timer while a test reads them.
            foreach (var worker in services.Where(d => d.ImplementationType == typeof(AttendanceWorker)
                                                       || d.ImplementationType == typeof(NotificationWorker)).ToList())
                services.Remove(worker);
        });
    }

    /// <summary>
    /// An account with no HR record. New accounts are always created with one; this stands for data
    /// from before that rule, which the system must still handle without an error.
    /// </summary>
    public const string LegacyAccount = "9999";

    public async Task InitializeAsync()
    {
        // Creating the server runs Program: reset, schema, demo seed. Today's records are normally
        // created by the worker removed above, so materialise them once here.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AttendanceJobs>().MaterializeAsync(CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Domain.Entities.User("حساب قديم بلا سجل", null, "0500009999", Domain.Enums.UserRole.Employee, LegacyAccount, "ar"));
        await db.SaveChangesAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public async Task<Guid> UserIdAsync(string employeeNumber)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.EmployeeNumber == employeeNumber).Select(u => u.Id).SingleAsync();
    }

    /// <summary>A client signed in as the given account. Tokens are issued directly so the login rate limit never interferes.</summary>
    public async Task<HttpClient> ClientForAsync(string employeeNumber)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.EmployeeNumber == employeeNumber);
        // Issued on real time, not the frozen clock: token validation checks expiry against the
        // wall clock, so a token stamped with the frozen date would arrive already expired.
        var issuer = new TokenService(scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>(), new SystemClock(TimeProvider.System));
        var (token, _) = issuer.Issue(user);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private sealed class FrozenTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
