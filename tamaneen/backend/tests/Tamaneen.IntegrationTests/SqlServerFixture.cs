using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tamaneen.Clinical;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;
using Testcontainers.MsSql;

namespace Tamaneen.IntegrationTests;

/// <summary>
/// One SQL Server 2022 container per test run with all migrations applied, including the ledger table
/// and the row-level security policy. Set TAMANEEN_TEST_SQL to use an existing server instead.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public byte[] LocalKek { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>Server key (Auth:ServerKeyBase64) for every host on this database: seeded staff secrets are encrypted with it.</summary>
    public string ServerKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public byte[] Pepper { get; } = RandomNumberGenerator.GetBytes(32);

    public SystemClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("TAMANEEN_TEST_SQL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(external) { InitialCatalog = "TamaneenTest_" + Guid.NewGuid().ToString("N")[..8] };
            ConnectionString = builder.ConnectionString;
        }
        else
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "TamaneenTest" };
            ConnectionString = builder.ConnectionString;
        }

        await using var db = Context(StaticCurrentUser.Anonymous);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            await using var db = Context(StaticCurrentUser.Anonymous);
            await db.Database.EnsureDeletedAsync();
        }
    }

    public TamaneenDbContext Context(ICurrentUser user)
    {
        var options = new DbContextOptionsBuilder<TamaneenDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Security))
            .AddInterceptors(new SessionContextInterceptor(user))
            .Options;
        return new TamaneenDbContext(options);
    }

    public (IPatientKeyStore Keys, PatientDataProtector Protector) Crypto(TamaneenDbContext db) =>
        Crypto(db, new MemoryCache(new MemoryCacheOptions()));

    public (IPatientKeyStore Keys, PatientDataProtector Protector) Crypto(TamaneenDbContext db, IMemoryCache cache)
    {
        var keys = new PatientKeyStore(db, new LocalKeyEncryptionKeyProvider(LocalKek), cache, Clock, Options.Create(new EncryptionOptions { DataKeyCacheSeconds = 0 }));
        return (keys, new PatientDataProtector(keys));
    }

    /// <summary>Creates a patient with an encrypted profile. Runs as the data-protection service, the only identity allowed to write any patient's vault rows.</summary>
    public async Task<UserAccount> CreatePatientAsync(string name, string language = "ar")
    {
        var account = UserAccount.Register(AppRoles.Patient, new ContactHasher(Pepper).Hash($"{Guid.NewGuid():N}@example.com"), language, Clock);
        account.Activate();
        await using var db = Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.DataProtectionService));
        db.UserAccounts.Add(account);
        var profile = PatientProfile.Create(account, "RUH", Clock);
        var (_, protector) = Crypto(db);
        profile.SetEncryptedFields(
            await protector.EncryptAsync(account.Id, ProtectedFields.DisplayName, name, default),
            await protector.EncryptAsync(account.Id, ProtectedFields.DateOfBirth, "1995-04-02", default),
            await protector.EncryptAsync(account.Id, ProtectedFields.EmergencyContact, """{"contactPhone":null,"trustedName":"أختي","trustedPhone":"0551234567"}""", default));
        db.PatientProfiles.Add(profile);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<UserAccount> CreateStaffAsync(string role)
    {
        var account = UserAccount.Register(role, new ContactHasher(Pepper).Hash($"{Guid.NewGuid():N}@staff.example.com"), "ar", Clock);
        account.Activate();
        await using var db = Context(StaticCurrentUser.Anonymous);
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<CareAssignment> AssignAsync(Guid patientId, Guid therapistId)
    {
        await using var db = Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor));
        var assignment = CareAssignment.Start(patientId, therapistId, Clock);
        db.CareAssignments.Add(assignment);
        new AuditLog(db, StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor), Clock).Append("care.assigned", "CareAssignment", assignment.Id.ToString());
        await db.SaveChangesAsync();
        return assignment;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerGroup : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
