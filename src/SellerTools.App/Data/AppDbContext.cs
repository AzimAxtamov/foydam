using Microsoft.EntityFrameworkCore;
using SellerTools.Core.Entities;

namespace SellerTools.App.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<SkuDailySnapshot> SkuDailySnapshots => Set<SkuDailySnapshot>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Seller>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // telegram user id
            e.Property(x => x.Language).HasMaxLength(2);
        });

        mb.Entity<Shop>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // uzum shopId
            e.HasOne(x => x.Seller)
                .WithMany(s => s.Shops)
                .HasForeignKey(x => x.SellerId);
        });

        mb.Entity<SkuDailySnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            // Уникальный срез на (магазин, дата, sku) — «ров данных».
            e.HasIndex(x => new { x.ShopId, x.Date, x.SkuId }).IsUnique();
        });

        mb.Entity<SyncCursor>(e =>
        {
            // Составной ключ ShopId + Kind.
            e.HasKey(x => new { x.ShopId, x.Kind });
        });
    }
}
