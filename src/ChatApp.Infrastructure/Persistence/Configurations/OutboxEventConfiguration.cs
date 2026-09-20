using ChatApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Infrastructure.Persistence.Configurations;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> b)
    {
        b.ToTable("OutboxEvents");
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasMaxLength(50).IsRequired();
        b.Property(x => x.ConversationId).IsRequired();
        b.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.ProcessedAt);
        b.Property(x => x.Attempts).IsRequired();
        b.Property(x => x.NextAttemptAt).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);

        // Dispatcher poll: pending, due, oldest first.
        b.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt, x.CreatedAt })
            .HasDatabaseName("IX_OutboxEvents_Pending");
    }
}
