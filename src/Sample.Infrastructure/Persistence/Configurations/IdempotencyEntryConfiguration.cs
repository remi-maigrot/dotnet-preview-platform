using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Infrastructure.Idempotency;

namespace Sample.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyEntryConfiguration : IEntityTypeConfiguration<IdempotencyEntry>
{
    public const int MaxKeyLength = 100;

    public void Configure(EntityTypeBuilder<IdempotencyEntry> builder)
    {
        builder.ToTable("idempotency_entries");
        builder.HasKey(e => new { e.CallerId, e.Key });
        builder.Property(e => e.Key).HasMaxLength(MaxKeyLength);
        builder.Property(e => e.RequestHash).HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.Location).HasMaxLength(2048);
        builder.Property(e => e.ResponseBody).HasColumnType("jsonb");
        builder.HasIndex(e => e.CreatedAt);
    }
}
