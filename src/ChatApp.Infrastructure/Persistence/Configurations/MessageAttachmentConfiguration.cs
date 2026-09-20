using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> b)
    {
        b.ToTable("MessageAttachments");
        b.HasKey(x => x.Id);

        b.Property(x => x.MessageId);
        b.Property(x => x.UploadedByUserId).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(127).IsRequired();
        b.Property(x => x.Size).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        // Messages are soft-deleted, never hard-deleted: attachments must survive.
        b.HasOne(x => x.Message)
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => x.MessageId).HasDatabaseName("IX_MessageAttachments_MessageId");
        b.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("IX_MessageAttachments_StorageKey");
        b.HasIndex(x => x.UploadedByUserId).HasDatabaseName("IX_MessageAttachments_UploadedByUserId");
    }
}
