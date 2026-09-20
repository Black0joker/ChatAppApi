using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class MessageReadReceiptConfiguration : IEntityTypeConfiguration<MessageReadReceipt>
{
    public void Configure(EntityTypeBuilder<MessageReadReceipt> b)
    {
        b.ToTable("MessageReadReceipts");
        b.HasKey(x => new { x.MessageId, x.UserId });

        b.Property(x => x.ReadAt).IsRequired();

        b.HasOne(x => x.Message)
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // PLAN §7: per-user read-state lookups.
        b.HasIndex(x => new { x.UserId, x.MessageId })
            .HasDatabaseName("IX_MessageReadReceipts_UserId_MessageId");
    }
}
