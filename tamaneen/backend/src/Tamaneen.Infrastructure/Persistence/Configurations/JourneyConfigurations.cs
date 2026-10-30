using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Idempotency;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

// Patient-journey tables (phase 4). Every table with a PatientId is covered by the row-level security
// policy in the PatientJourney migration; ciphertext columns hold AES-GCM values from PatientDataProtector.

internal sealed class CredentialConfiguration : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> b)
    {
        b.ToTable("Credentials", Schemas.Identity);
        b.HasKey(x => x.UserId);
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Property(x => x.TotpSecretCipher).HasMaxLength(128);
        b.HasOne<UserAccount>().WithOne().HasForeignKey<Credential>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> b)
    {
        b.ToTable("RecoveryCodes", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CodeHash).HasColumnType("binary(32)").IsRequired();
        b.HasIndex(x => new { x.UserId, x.CodeHash }).IsUnique();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthChallengeConfiguration : IEntityTypeConfiguration<AuthChallenge>
{
    public void Configure(EntityTypeBuilder<AuthChallenge> b)
    {
        b.ToTable("AuthChallenges", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Purpose).HasConversion<byte>();
        b.Property(x => x.CodeHash).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.ContactCipher).HasMaxLength(512).IsRequired();
        b.Property(x => x.PayloadCipher).HasMaxLength(2048);
        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> b)
    {
        b.ToTable("AuthSessions", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Client).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.DeviceLabel).HasMaxLength(64).IsRequired();
        b.Property(x => x.RefreshTokenHash).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.PreviousRefreshTokenHash).HasColumnType("binary(32)");
        b.HasIndex(x => x.RefreshTokenHash).IsUnique();
        b.HasIndex(x => x.PreviousRefreshTokenHash).HasFilter("[PreviousRefreshTokenHash] IS NOT NULL");
        b.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> b)
    {
        b.ToTable("TrustedDevices", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasColumnType("binary(32)").IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> b)
    {
        b.ToTable("Consents", Schemas.Identity);
        b.HasKey(x => new { x.PatientId, x.Key });
        b.Property(x => x.Key).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Version).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> b)
    {
        b.ToTable("NotificationPreferences", Schemas.Identity);
        b.HasKey(x => x.UserId);
        b.Property(x => x.HomeworkReminder).HasConversion<byte>();
        b.HasOne<UserAccount>().WithOne().HasForeignKey<NotificationPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> b)
    {
        b.ToTable("StaffProfiles", Schemas.Identity);
        b.HasKey(x => x.UserId);
        b.Property(x => x.DisplayNameAr).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayNameEn).HasMaxLength(100).IsRequired();
        b.HasOne<UserAccount>().WithOne().HasForeignKey<StaffProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TherapistProfileConfiguration : IEntityTypeConfiguration<TherapistProfile>
{
    public void Configure(EntityTypeBuilder<TherapistProfile> b)
    {
        b.ToTable("TherapistProfiles", Schemas.Clinical);
        b.HasKey(x => x.UserId);
        b.Property(x => x.TitleAr).HasMaxLength(120).IsRequired();
        b.Property(x => x.TitleEn).HasMaxLength(120).IsRequired();
        b.Property(x => x.BioAr).HasMaxLength(2000).IsRequired();
        b.Property(x => x.BioEn).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Gender).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Specialties).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Property(x => x.Languages).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Ignore(x => x.SpecialtyList);
        b.Ignore(x => x.LanguageList);
        b.HasOne<StaffProfile>().WithOne().HasForeignKey<TherapistProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> b)
    {
        b.ToTable("AvailabilitySlots", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Source).HasConversion<byte>();
        b.Ignore(x => x.EndsAtUtc);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TherapistUserId, x.StartsAtUtc }).IsUnique();
        b.HasIndex(x => x.HoldId).HasFilter("[HoldId] IS NOT NULL");
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TherapistAvailabilityConfiguration : IEntityTypeConfiguration<TherapistAvailability>
{
    public void Configure(EntityTypeBuilder<TherapistAvailability> b)
    {
        b.ToTable("TherapistAvailabilities", Schemas.Clinical);
        b.HasKey(x => x.TherapistUserId);
        b.Property(x => x.WeeklyJson).HasMaxLength(4000).IsUnicode(false).IsRequired();
        b.Property(x => x.ExceptionsJson).HasMaxLength(8000).IsUnicode(false).IsRequired();
        b.Ignore(x => x.Weekly);
        b.Ignore(x => x.Exceptions);
        b.HasOne<TherapistProfile>().WithOne().HasForeignKey<TherapistAvailability>(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("Appointments", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RoomId).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.CancelReason).HasMaxLength(CancelReasons.MaxLength).IsUnicode(false);
        b.Property(x => x.TherapistCancelNoteCipher).HasMaxLength(4096);
        b.Property(x => x.MissedCheckIn).HasMaxLength(8).IsUnicode(false);
        b.Ignore(x => x.CancelledBy);
        b.Ignore(x => x.Missed);
        b.HasIndex(x => new { x.PatientId, x.StartsAtUtc });
        b.HasIndex(x => new { x.TherapistUserId, x.StartsAtUtc });
        b.HasIndex(x => x.SlotId).IsUnique().HasFilter("[Status] = 0").HasDatabaseName("UX_Appointments_OneConfirmedPerSlot");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AvailabilitySlot>().WithMany().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MatchSuggestionConfiguration : IEntityTypeConfiguration<MatchSuggestion>
{
    public void Configure(EntityTypeBuilder<MatchSuggestion> b)
    {
        b.ToTable("MatchSuggestions", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SharedSpecialties).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Property(x => x.DismissReason).HasMaxLength(16).IsUnicode(false);
        b.HasIndex(x => new { x.PatientId, x.TherapistUserId }).IsUnique();
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeSessionConfiguration : IEntityTypeConfiguration<IntakeSession>
{
    public void Configure(EntityTypeBuilder<IntakeSession> b)
    {
        b.ToTable("IntakeSessions", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.Phq9).HasColumnType("char(9)").IsRequired();
        b.Property(x => x.Gad7).HasColumnType("char(7)").IsRequired();
        b.Property(x => x.SummaryCipher).HasMaxLength(16000);
        b.Ignore(x => x.ExpiresAtUtc);
        b.Ignore(x => x.AnsweredOrSkipped);
        b.Ignore(x => x.CanFinish);
        b.Ignore(x => x.Progress);
        b.HasIndex(x => new { x.PatientId, x.StartedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IntakeMessageConfiguration : IEntityTypeConfiguration<IntakeMessage>
{
    public void Configure(EntityTypeBuilder<IntakeMessage> b)
    {
        b.ToTable("IntakeMessages", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TextCipher).HasMaxLength(20000).IsRequired();
        b.HasIndex(x => new { x.IntakeId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.IntakeId, x.ClientMessageId }).IsUnique().HasFilter("[ClientMessageId] IS NOT NULL");
        b.HasOne<IntakeSession>().WithMany().HasForeignKey(x => x.IntakeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TriageCaseConfiguration : IEntityTypeConfiguration<TriageCase>
{
    public void Configure(EntityTypeBuilder<TriageCase> b)
    {
        b.ToTable("TriageCases", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Path).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.RiskLevel).HasConversion<byte>();
        b.Property(x => x.RuleRiskLevel).HasConversion<byte>();
        b.Property(x => x.PendingRiskLevel).HasConversion<byte?>();
        b.Property(x => x.Referrals).HasMaxLength(200).IsUnicode(false);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.AddendaCipher).HasMaxLength(16000);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.IsChangeRequest);
        b.HasIndex(x => new { x.Status, x.DueAtUtc });
        b.HasIndex(x => x.PatientId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<IntakeSession>().WithMany().HasForeignKey(x => x.IntakeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FileReviewConfiguration : IEntityTypeConfiguration<FileReview>
{
    public void Configure(EntityTypeBuilder<FileReview> b)
    {
        b.ToTable("FileReviews", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EditsCipher).HasMaxLength(16000);
        b.Property(x => x.DismissedKeys).HasMaxLength(400).IsUnicode(false).IsRequired();
        b.Property(x => x.RatingCommentCipher).HasMaxLength(2000);
        b.Ignore(x => x.Dismissed);
        b.HasIndex(x => x.TriageCaseId).IsUnique();
        b.HasIndex(x => x.PatientId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TriageCase>().WithMany().HasForeignKey(x => x.TriageCaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScreeningConfiguration : IEntityTypeConfiguration<Screening>
{
    public void Configure(EntityTypeBuilder<Screening> b)
    {
        b.ToTable("Screenings", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Instrument).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Property(x => x.Version).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.Level).HasConversion<byte>();
        b.Property(x => x.AnswersCipher).HasMaxLength(512).IsRequired();
        b.HasIndex(x => new { x.PatientId, x.CompletedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MoodEntryConfiguration : IEntityTypeConfiguration<MoodEntry>
{
    public void Configure(EntityTypeBuilder<MoodEntry> b)
    {
        b.ToTable("MoodEntries", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Tags).HasMaxLength(128).IsUnicode(false).IsRequired();
        b.Property(x => x.NoteCipher).HasMaxLength(8100);
        b.Ignore(x => x.TagList);
        b.HasIndex(x => new { x.PatientId, x.RecordedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_MoodEntries_Score", "[Score] BETWEEN 0 AND 10"));
    }
}

internal sealed class HomeworkAssignmentConfiguration : IEntityTypeConfiguration<HomeworkAssignment>
{
    public void Configure(EntityTypeBuilder<HomeworkAssignment> b)
    {
        b.ToTable("HomeworkAssignments", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TitleCipher).HasMaxLength(1024).IsRequired();
        b.Property(x => x.InstructionsCipher).HasMaxLength(8100).IsRequired();
        b.Property(x => x.Frequency).HasConversion<byte>();
        b.Property(x => x.TemplateCode).HasMaxLength(32).IsUnicode(false);
        b.Ignore(x => x.TotalDays);
        b.Ignore(x => x.IsCancelled);
        b.HasIndex(x => new { x.PatientId, x.EndsOn });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HomeworkCheckinConfiguration : IEntityTypeConfiguration<HomeworkCheckin>
{
    public void Configure(EntityTypeBuilder<HomeworkCheckin> b)
    {
        b.ToTable("HomeworkCheckins", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.NoteCipher).HasMaxLength(8100);
        b.Property(x => x.FeedbackCipher).HasMaxLength(8100);
        b.Property(x => x.GateLevel).HasConversion<byte?>();
        b.HasIndex(x => new { x.AssignmentId, x.Day }).IsUnique();
        b.HasIndex(x => x.PatientId);
        b.HasOne<HomeworkAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SafetyPlanConfiguration : IEntityTypeConfiguration<SafetyPlan>
{
    public void Configure(EntityTypeBuilder<SafetyPlan> b)
    {
        b.ToTable("SafetyPlans", Schemas.Clinical);
        b.HasKey(x => x.PatientId);
        b.Property(x => x.SectionsCipher).HasMaxLength(16000).IsRequired();
        b.Property(x => x.UpdatedByRole).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<UserAccount>().WithOne().HasForeignKey<SafetyPlan>(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CareRequestConfiguration : IEntityTypeConfiguration<CareRequest>
{
    public void Configure(EntityTypeBuilder<CareRequest> b)
    {
        b.ToTable("CareRequests", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Source).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.HasIndex(x => new { x.HandledAtUtc, x.RequestedAtUtc });
        b.HasIndex(x => x.PatientId);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DataRequestConfiguration : IEntityTypeConfiguration<DataRequest>
{
    public void Configure(EntityTypeBuilder<DataRequest> b)
    {
        b.ToTable("DataRequests", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<byte>();
        b.HasIndex(x => new { x.PatientId, x.Kind });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CrisisActionConfiguration : IEntityTypeConfiguration<CrisisAction>
{
    public void Configure(EntityTypeBuilder<CrisisAction> b)
    {
        b.ToTable("CrisisActions", Schemas.Crisis);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.ActorRole).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.TextCipher).HasMaxLength(8100);
        b.Property(x => x.Detail).HasMaxLength(32).IsUnicode(false);
        b.HasIndex(x => new { x.CrisisEventId, x.OccurredAtUtc });
        b.HasOne<CrisisEvent>().WithMany().HasForeignKey(x => x.CrisisEventId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Method).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Path).HasMaxLength(256).IsUnicode(false).IsRequired();
        b.Ignore(x => x.IsCompleted);
        b.HasIndex(x => new { x.PatientId, x.Key }).IsUnique().HasDatabaseName("UX_IdempotencyRecords_PatientKey");
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}
