using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> b)
    {
        b.ToTable("ConversationMembers");
        b.HasKey(x => new { x.ConversationId, x.UserId });

        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.JoinedAt).IsRequired();
        b.Property(x => x.LeftAt);
        b.Property(x => x.LastReadMessageId);

        // PLAN §7: membership lookups in both directions.
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_ConversationMembers_UserId");
        b.HasIndex(x => x.ConversationId).HasDatabaseName("IX_ConversationMembers_ConversationId");
    }
}
