using Microsoft.EntityFrameworkCore;
using Tamaneen.AiGateway;
using Tamaneen.Clinical;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
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

    public DbSet<ChatLock> ChatLocks => Set<ChatLock>();

    public DbSet<PageRequest> PageRequests => Set<PageRequest>();

    public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();

    public DbSet<AiCallRecord> AiCallRecords => Set<AiCallRecord>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Credential> Credentials => Set<Credential>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<AuthChallenge> AuthChallenges => Set<AuthChallenge>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();

    public DbSet<DataRequest> DataRequests => Set<DataRequest>();

    public DbSet<TherapistProfile> TherapistProfiles => Set<TherapistProfile>();

    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<MatchSuggestion> MatchSuggestions => Set<MatchSuggestion>();

    public DbSet<IntakeSession> IntakeSessions => Set<IntakeSession>();

    public DbSet<IntakeMessage> IntakeMessages => Set<IntakeMessage>();

    public DbSet<TriageCase> TriageCases => Set<TriageCase>();

    public DbSet<Screening> Screenings => Set<Screening>();

    public DbSet<MoodEntry> MoodEntries => Set<MoodEntry>();

    public DbSet<HomeworkAssignment> HomeworkAssignments => Set<HomeworkAssignment>();

    public DbSet<HomeworkCheckin> HomeworkCheckins => Set<HomeworkCheckin>();

    public DbSet<SafetyPlan> SafetyPlans => Set<SafetyPlan>();

    public DbSet<CareRequest> CareRequests => Set<CareRequest>();

    public DbSet<CrisisAction> CrisisActions => Set<CrisisAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<int>("CrisisEventNumbers", Schemas.Crisis).StartsAt(1001);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TamaneenDbContext).Assembly);
    }
}
