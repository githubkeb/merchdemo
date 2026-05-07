using EventsConsumer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventsConsumer.Data;

public sealed class MerchantAggregatesDbContext(DbContextOptions<MerchantAggregatesDbContext> options) : DbContext(options)
{
    public DbSet<ProductEvent> ProductEvents => Set<ProductEvent>();
    public DbSet<MerchantCategoryEvent> MerchantCategoryEvents => Set<MerchantCategoryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductEvent>(entity =>
        {
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SortOrder).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.Property(x => x.ReceivedAtUtc).IsRequired();
        });

        modelBuilder.Entity<MerchantCategoryEvent>(entity =>
        {
            entity.HasKey(x => x.MessageId);
            entity.Property(x => x.MessageId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(32).IsRequired();
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.Property(x => x.ReceivedAtUtc).IsRequired();
        });
    }
}
