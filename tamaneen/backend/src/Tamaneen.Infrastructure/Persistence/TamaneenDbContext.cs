using Microsoft.EntityFrameworkCore;
using Tamaneen.AiGateway;
using Tamaneen.Clinical;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;

namespace Tamaneen.Infrastructure.Persistence;

/// <summary>
/// One database, one schema per module. Modules never query another module's schema directly;
/// this context exists so they can share a transaction and a connection whose SESSION_CONTEXT
/// drives row-level security.
/// </summary>
public sealed class TamaneenDbContext : DbContext
{
    public TamaneenDbContext(DbContextOptions<TamaneenDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<PatientKey> PatientKeys => Set<PatientKey>();

    public DbSet<CareAssignment> CareAssignments => Set<CareAssignment>();

    public DbSet<CrisisEvent> CrisisEvents => Set<CrisisEvent>();

    public DbSet<AiCallRecord> AiCallRecords => Set<AiCallRecord>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TamaneenDbContext).Assembly);
    }
}
