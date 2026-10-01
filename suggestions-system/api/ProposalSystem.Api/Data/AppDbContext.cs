using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<ProposalFieldValue> FieldValues => Set<ProposalFieldValue>();
    public DbSet<CommitteeVote> CommitteeVotes => Set<CommitteeVote>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ImpactAssessment> ImpactAssessments => Set<ImpactAssessment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums travel as readable strings in both providers (and in the SQL scripts).
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        builder.Properties<decimal>().HavePrecision(18, 2);
        // Every timestamp is UTC; SQLite forgets the Kind, so restore it on the way out or the
        // SPA would read "2026-10-01T09:00:00" as local time.
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserCode).IsUnique();
            e.Property(x => x.UserCode).HasMaxLength(30);
            e.Property(x => x.ArabicName).HasMaxLength(150);
            e.Property(x => x.EnglishName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.PhoneNumber).HasMaxLength(30);
            e.Property(x => x.Department).HasMaxLength(150);
            e.Property(x => x.JobTitle).HasMaxLength(150);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.CsrfToken).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Proposal>(e =>
        {
            e.HasIndex(x => x.ProposalCode).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SlaDueAt);
            e.Property(x => x.ProposalCode).HasMaxLength(30);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Department).HasMaxLength(150);
            e.HasOne(x => x.Submitter).WithMany().HasForeignKey(x => x.SubmitterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExecutiveDecisionBy).WithMany().HasForeignKey(x => x.ExecutiveDecisionById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EscalatedTo).WithMany().HasForeignKey(x => x.EscalatedToId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.FieldValues).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Votes).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Impact).WithOne(x => x.Proposal).HasForeignKey<ImpactAssessment>(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FormField>(e =>
        {
            e.HasIndex(x => x.FieldKey).IsUnique();
            e.Property(x => x.FieldKey).HasMaxLength(60);
            e.Property(x => x.LabelAr).HasMaxLength(200);
            e.Property(x => x.LabelEn).HasMaxLength(200);
            e.Property(x => x.Placeholder).HasMaxLength(300);
            e.Property(x => x.Section).HasMaxLength(40);
        });

        b.Entity<ProposalFieldValue>(e =>
        {
            e.HasIndex(x => new { x.ProposalId, x.FieldId }).IsUnique();
            e.HasOne(x => x.Field).WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CommitteeVote>(e =>
        {
            e.HasIndex(x => new { x.ProposalId, x.MemberId }).IsUnique();
            e.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.StoredName).HasMaxLength(100);
            e.Property(x => x.ContentType).HasMaxLength(150);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.ProposalId);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.ProposalCode).HasMaxLength(30);
            e.Property(x => x.ActorName).HasMaxLength(150);
            e.Property(x => x.ActorRole).HasMaxLength(40);
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.FromStatus).HasMaxLength(40);
            e.Property(x => x.ToStatus).HasMaxLength(40);
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(600);
            e.HasOne(x => x.Proposal).WithMany().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ImpactAssessment>(e =>
        {
            e.HasIndex(x => x.ProposalId).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.EvidenceReference).HasMaxLength(500);
            e.HasOne(x => x.MeasuredBy).WithMany().HasForeignKey(x => x.MeasuredById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VerifiedBy).WithMany().HasForeignKey(x => x.VerifiedById).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.TotalAnnualBenefit);
            e.Ignore(x => x.RoiPercent);
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
