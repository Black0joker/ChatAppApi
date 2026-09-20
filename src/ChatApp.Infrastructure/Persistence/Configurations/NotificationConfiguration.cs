using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(500).IsRequired();
        b.Property(x => x.ConversationId);
        b.Property(x => x.MessageId);
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.ReadAt);

        // Inbox query: newest-first per user.
        b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("IX_Notifications_UserId_CreatedAt");
    }
}
