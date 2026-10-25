using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tamaneen.Identity;
using Tamaneen.Identity.Notifications;

namespace Tamaneen.Infrastructure.Persistence.Configurations;

/// <summary>In-app inbox (P46, S41). Row-level security: <c>security.NotificationPolicy</c> on RecipientUserId.</summary>
internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("Notifications", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.Category).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Property(x => x.DataJson).HasMaxLength(UserNotification.MaxDataLength).IsRequired();
        b.Property(x => x.Link).HasMaxLength(UserNotification.MaxLinkLength);
        b.Property(x => x.DedupeKey).HasMaxLength(200).IsUnicode(false);
        b.Property(x => x.External).HasConversion<byte>();
        b.Property(x => x.ExternalChannel).HasMaxLength(8).IsUnicode(false);
        b.Ignore(x => x.IsRead);
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.RecipientUserId, x.DedupeKey }).IsUnique().HasFilter("[DedupeKey] IS NOT NULL").HasDatabaseName("UX_Notifications_Dedupe");
        b.HasIndex(x => new { x.External, x.ExternalNotBeforeUtc });
        b.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
