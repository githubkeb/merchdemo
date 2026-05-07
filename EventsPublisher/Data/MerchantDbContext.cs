using EventsPublisher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventsPublisher.Data;

public sealed class MerchantDbContext(DbContextOptions<MerchantDbContext> options) : DbContext(options)
{
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantCategory> MerchantCategories => Set<MerchantCategory>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();

            entity.HasMany(x => x.Categories)
                .WithOne(x => x.Merchant)
                .HasForeignKey(x => x.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Products)
                .WithOne(x => x.Merchant)
                .HasForeignKey(x => x.MerchantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantCategory>(entity =>
        {
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.SortOrder).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasOne(x => x.MerchantCategory)
                .WithMany(x => x.Products)
                .HasForeignKey(x => x.MerchantCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

