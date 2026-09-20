using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("Messages");
        b.HasKey(x => x.Id);

        b.Property(x => x.ConversationId).IsRequired();
        b.Property(x => x.SenderId).IsRequired();
        b.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        b.Property(x => x.MessageType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.EditedAt);
        b.Property(x => x.DeletedAt);
        b.Property(x => x.ReplyToMessageId);

        b.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-reference for replies; never cascade (deleting one message must not delete its replies).
        b.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.ReplyToMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // PLAN §7: history query is the hot path — keyset on (ConversationId, CreatedAt).
        b.HasIndex(x => new { x.ConversationId, x.CreatedAt })
            .HasDatabaseName("IX_Messages_ConversationId_CreatedAt");
        b.HasIndex(x => new { x.SenderId, x.CreatedAt })
            .HasDatabaseName("IX_Messages_SenderId_CreatedAt");
    }
}
