using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Identity;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

/// <summary>P56 waitlist. Row-level security (WaitlistEntries migration): the patient, clinical staff and the scheduling service.</summary>
internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("WaitlistEntries", Schemas.Clinical);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Gender).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Language).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Times).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.Days).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.RiskLevel).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();

        // One active entry per patient; left entries stay as history.
        b.HasIndex(x => x.PatientId).IsUnique().HasFilter("[Status] <> 2").HasDatabaseName("UX_WaitlistEntries_OneActive");
        b.HasIndex(x => new { x.Status, x.RiskLevel, x.JoinedAtUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
