using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Identity;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

// Phase 5 care records (S15, S33, S35, S57, P44). Every table has a PatientId and sits under the row-level security
// policy (migration Phase5ClinicalCareRecords); free text is AES-GCM ciphertext under the patient's key.

internal sealed class TreatmentPlanVersionConfiguration : IEntityTypeConfiguration<TreatmentPlanVersion>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanVersion> b)
    {
        b.ToTable("TreatmentPlanVersions", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ContentCipher).HasMaxLength(40000).IsRequired();
        b.Property(x => x.Frequency).HasConversion<byte>();
        b.HasIndex(x => new { x.PatientId, x.Version }).IsUnique();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionNoteConfiguration : IEntityTypeConfiguration<SessionNote>
{
    public void Configure(EntityTypeBuilder<SessionNote> b)
    {
        b.ToTable("SessionNotes", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Template).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.FieldsCipher).HasMaxLength(40000).IsRequired();
        b.Property(x => x.SummaryCipher).HasMaxLength(4000);
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => x.AppointmentId).IsUnique();
        b.HasIndex(x => x.PatientId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionNoteAddendumConfiguration : IEntityTypeConfiguration<SessionNoteAddendum>
{
    public void Configure(EntityTypeBuilder<SessionNoteAddendum> b)
    {
        b.ToTable("SessionNoteAddenda", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TextCipher).HasMaxLength(8100).IsRequired();
        b.HasIndex(x => new { x.NoteId, x.CreatedAtUtc });
        b.HasIndex(x => x.PatientId);
        b.HasOne<SessionNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SessionSummaryConfiguration : IEntityTypeConfiguration<SessionSummary>
{
    public void Configure(EntityTypeBuilder<SessionSummary> b)
    {
        b.ToTable("SessionSummaries", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PointsCipher).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => x.AppointmentId).IsUnique();
        b.HasIndex(x => x.PatientId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SafetyPlanVersionConfiguration : IEntityTypeConfiguration<SafetyPlanVersion>
{
    public void Configure(EntityTypeBuilder<SafetyPlanVersion> b)
    {
        b.ToTable("SafetyPlanVersions", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SectionsCipher).HasMaxLength(16000).IsRequired();
        b.Property(x => x.AuthorRole).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.HasIndex(x => new { x.PatientId, x.Version }).IsUnique();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CareClosureConfiguration : IEntityTypeConfiguration<CareClosure>
{
    public void Configure(EntityTypeBuilder<CareClosure> b)
    {
        b.ToTable("CareClosures", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.ReasonCode).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.FollowUp).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Property(x => x.PatientSummaryCipher).HasMaxLength(8100);
        b.Property(x => x.RelapsePlanCipher).HasMaxLength(4000);
        b.Property(x => x.RejectionCipher).HasMaxLength(4000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Ignore(x => x.IsOpen);
        b.HasIndex(x => new { x.PatientId, x.Status });
        b.HasIndex(x => new { x.Status, x.SubmittedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChangeTherapistRequestConfiguration : IEntityTypeConfiguration<ChangeTherapistRequest>
{
    public void Configure(EntityTypeBuilder<ChangeTherapistRequest> b)
    {
        b.ToTable("ChangeTherapistRequests", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.ReasonCode).HasMaxLength(32).IsUnicode(false);
        b.HasIndex(x => x.PatientId).IsUnique().HasFilter("[Status] = 0").HasDatabaseName("UX_ChangeTherapistRequests_OneOpen");
        b.HasIndex(x => x.TriageCaseId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TriageCase>().WithMany().HasForeignKey(x => x.TriageCaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
