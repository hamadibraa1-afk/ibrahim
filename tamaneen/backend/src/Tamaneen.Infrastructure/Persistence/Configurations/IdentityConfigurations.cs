using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Identity;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> b)
    {
        b.ToTable("UserAccounts", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PseudonymId).HasMaxLength(11).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.PseudonymId).IsUnique();
        b.Property(x => x.Role).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.ContactLookupHash).HasColumnType("binary(32)").IsRequired();
        b.HasIndex(x => x.ContactLookupHash).IsUnique();
        b.Property(x => x.PreferredLanguage).HasMaxLength(2).IsUnicode(false).IsRequired();
        b.Property(x => x.Status).HasConversion<byte>();
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_UserAccounts_Language", "[PreferredLanguage] IN ('ar','en')");
            t.HasCheckConstraint("CK_UserAccounts_Role", "[Role] IN ('Patient','Therapist','Supervisor','ClinicalLead','Admin')");
        });
    }
}

internal sealed class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> b)
    {
        b.ToTable("PatientProfiles", Schemas.Identity);
        b.HasKey(x => x.PatientId);
        b.Property(x => x.PatientId).ValueGeneratedNever();
        b.Property(x => x.PseudonymId).HasMaxLength(11).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.PseudonymId).IsUnique();
        b.Property(x => x.DisplayNameCipher).HasMaxLength(512);
        b.Property(x => x.DateOfBirthCipher).HasMaxLength(128);
        b.Property(x => x.EmergencyContactCipher).HasMaxLength(1024);
        b.Property(x => x.CityCode).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Ignore(x => x.IsShredded);
        b.HasOne<UserAccount>().WithOne().HasForeignKey<PatientProfile>(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}
