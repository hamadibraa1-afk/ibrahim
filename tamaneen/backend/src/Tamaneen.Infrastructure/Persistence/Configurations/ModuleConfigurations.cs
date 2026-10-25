using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.AiGateway;
using Tamaneen.Clinical;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

internal sealed class PatientKeyConfiguration : IEntityTypeConfiguration<PatientKey>
{
    public void Configure(EntityTypeBuilder<PatientKey> b)
    {
        b.ToTable("PatientKeys", Schemas.Vault);
        b.HasKey(x => x.PatientId);
        b.Property(x => x.PatientId).ValueGeneratedNever();
        b.Property(x => x.WrappedKey).HasMaxLength(1024);
        b.Property(x => x.KeyEncryptionKeyId).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Ignore(x => x.IsShredded);
        b.ToTable(t => t.HasCheckConstraint("CK_PatientKeys_Shredded", "([ShreddedAtUtc] IS NULL AND [WrappedKey] IS NOT NULL) OR ([ShreddedAtUtc] IS NOT NULL AND [WrappedKey] IS NULL)"));
        b.HasOne<UserAccount>().WithOne().HasForeignKey<PatientKey>(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CareAssignmentConfiguration : IEntityTypeConfiguration<CareAssignment>
{
    public void Configure(EntityTypeBuilder<CareAssignment> b)
    {
        b.ToTable("CareAssignments", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => new { x.TherapistUserId, x.PatientId }).HasFilter("[EndedAtUtc] IS NULL").HasDatabaseName("IX_CareAssignments_Active");
        b.HasIndex(x => x.PatientId).IsUnique().HasFilter("[EndedAtUtc] IS NULL").HasDatabaseName("UX_CareAssignments_OneActivePerPatient");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CrisisEventConfiguration : IEntityTypeConfiguration<CrisisEvent>
{
    public void Configure(EntityTypeBuilder<CrisisEvent> b)
    {
        b.ToTable("CrisisEvents", Schemas.Crisis);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PseudonymId).HasMaxLength(11).IsUnicode(false).IsRequired();
        b.Property(x => x.Level).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Language).HasMaxLength(2).IsUnicode(false).IsRequired();
        b.Property(x => x.RulePackVersion).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.CloseReason).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.TriggerRuleIds).HasMaxLength(1000).IsUnicode(false).IsRequired();
        b.Property(x => x.Source).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.Number).HasDefaultValueSql("NEXT VALUE FOR [crisis].[CrisisEventNumbers]").ValueGeneratedOnAdd();
        b.Property(x => x.ExcerptCipher).HasMaxLength(8100);
        b.Property(x => x.CloseOutcome).HasMaxLength(32).IsUnicode(false);
        b.HasIndex(x => new { x.Status, x.AckDeadlineUtc }).HasDatabaseName("IX_CrisisEvents_OpenByDeadline");
        b.HasIndex(x => x.PatientId).IsUnique().HasFilter("[Status] <> 2").HasDatabaseName("UX_CrisisEvents_OneOpenPerPatient");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_CrisisEvents_Level", "[Level] IN (3,4)"));
    }
}

internal sealed class ChatLockConfiguration : IEntityTypeConfiguration<ChatLock>
{
    public void Configure(EntityTypeBuilder<ChatLock> b)
    {
        b.ToTable("ChatLocks", Schemas.Crisis);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.PatientId).IsUnique().HasFilter("[ReleasedAtUtc] IS NULL").HasDatabaseName("UX_ChatLocks_OneActivePerPatient");
        b.HasOne<CrisisEvent>().WithMany().HasForeignKey(x => x.CrisisEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HelpRequestConfiguration : IEntityTypeConfiguration<HelpRequest>
{
    public void Configure(EntityTypeBuilder<HelpRequest> b)
    {
        b.ToTable("HelpRequests", Schemas.Crisis);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.PatientId, x.RequestKey }).IsUnique().HasDatabaseName("UX_HelpRequests_PatientKey");
        b.HasOne<CrisisEvent>().WithMany().HasForeignKey(x => x.CrisisEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PageRequestConfiguration : IEntityTypeConfiguration<PageRequest>
{
    public void Configure(EntityTypeBuilder<PageRequest> b)
    {
        b.ToTable("PageRequests", Schemas.Crisis);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PseudonymId).HasMaxLength(11).IsUnicode(false).IsRequired();
        b.Property(x => x.Level).HasConversion<byte>();
        b.Property(x => x.PatientLanguage).HasMaxLength(2).IsUnicode(false).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc }).HasDatabaseName("IX_PageRequests_Due");
        b.HasOne<CrisisEvent>().WithMany().HasForeignKey(x => x.CrisisEventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AiCallRecordConfiguration : IEntityTypeConfiguration<AiCallRecord>
{
    public void Configure(EntityTypeBuilder<AiCallRecord> b)
    {
        b.ToTable("AiCallRecords", Schemas.Ai);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PseudonymId).HasMaxLength(11).IsUnicode(false).IsRequired();
        b.Property(x => x.Purpose).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.ModelId).HasMaxLength(128).IsUnicode(false).IsRequired();
        b.Property(x => x.Language).HasMaxLength(2).IsUnicode(false).IsRequired();
        b.Property(x => x.Outcome).HasConversion<byte>();
        b.HasIndex(x => x.CreatedAtUtc);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        // Created by the SecurityAndLedger migration as an append-only ledger table, which EF cannot express.
        // Ids come from a sequence (HiLo) rather than IDENTITY: with store-generated keys EF batches several
        // inserts into one MERGE, which ledger tables reject (found when two crisis events escalated together).
        b.ToTable("AuditEntries", Schemas.Audit, t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseHiLo("AuditEntryIds", Schemas.Audit);
        b.Property(x => x.ActorRole).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Action).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64).IsUnicode(false);
        b.Property(x => x.PatientPseudonymId).HasMaxLength(11).IsUnicode(false);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.CorrelationId).HasMaxLength(64).IsUnicode(false);
    }
}
