using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Clinical.Sessions;
using Tamaneen.Identity;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Session room tables (P41, P42, P43). Row-level security (migration Phase5SessionsVideo): presence and the
/// pre-session note follow the appointment (the patient and the session's therapist); feedback is the patient's and
/// the supervisors' only, never the therapist's.
/// </summary>
internal sealed class SessionPresenceConfiguration : IEntityTypeConfiguration<SessionPresence>
{
    public void Configure(EntityTypeBuilder<SessionPresence> b)
    {
        b.ToTable("SessionPresence", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Participant).HasConversion<byte>();
        b.HasIndex(x => new { x.AppointmentId, x.Participant }).IsUnique().HasDatabaseName("UX_SessionPresence_AppointmentParticipant");
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionFeedbackConfiguration : IEntityTypeConfiguration<SessionFeedback>
{
    public void Configure(EntityTypeBuilder<SessionFeedback> b)
    {
        b.ToTable("SessionFeedback", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.RatingCode);
        b.HasIndex(x => x.AppointmentId).IsUnique().HasDatabaseName("UX_SessionFeedback_Appointment");
        b.HasIndex(x => new { x.TherapistUserId, x.CreatedAtUtc });
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionPrepConfiguration : IEntityTypeConfiguration<SessionPrep>
{
    public void Configure(EntityTypeBuilder<SessionPrep> b)
    {
        b.ToTable("SessionPrep", Schemas.Clinical);
        b.HasKey(x => x.AppointmentId);
        b.Property(x => x.AppointmentId).ValueGeneratedNever();
        b.Property(x => x.NoteCipher).IsRequired();
        b.HasIndex(x => x.PatientId);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
