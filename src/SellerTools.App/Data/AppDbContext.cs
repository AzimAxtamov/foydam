using Microsoft.EntityFrameworkCore;
using SellerTools.Core.Entities;

namespace SellerTools.App.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShareEvent> ShareEvents => Set<ShareEvent>();
    public DbSet<SkuDailySnapshot> SkuDailySnapshots => Set<SkuDailySnapshot>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

    // Хранилище синхронизации (модель «синк → Postgres → отчёт из БД»).
    public DbSet<InvoiceRecord> Invoices => Set<InvoiceRecord>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<ReturnRecord> Returns => Set<ReturnRecord>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<SkuRecord> Skus => Set<SkuRecord>();
    public DbSet<ShopSyncState> ShopSyncStates => Set<ShopSyncState>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Seller>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // telegram user id
            e.Property(x => x.Language).HasMaxLength(2);

            // Реф-код: уникальный, nullable (генерируется лениво). Фильтрованный уникальный
            // индекс — чтобы множество NULL (код ещё не создан) не конфликтовало.
            e.Property(x => x.RefCode).HasMaxLength(8);
            e.HasIndex(x => x.RefCode).IsUnique().HasFilter("\"RefCode\" IS NOT NULL");

            // Кто привёл (self-FK). Без навигации/каскада: при удалении реферера
            // referred_by просто повисает (Restrict), удалять селлеров мы всё равно не удаляем.
            e.HasOne<Seller>()
                .WithMany()
                .HasForeignKey(x => x.ReferredBySellerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ShareEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SellerId);
            e.HasIndex(x => x.CreatedAt);
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

        // ---- Хранилище синка ----

        mb.Entity<InvoiceRecord>(e =>
        {
            e.HasKey(x => x.Id);                 // PK = id из API (идемпотентный upsert)
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasIndex(x => x.ShopId);
            e.HasIndex(x => new { x.ShopId, x.DateCreated });
            e.HasIndex(x => new { x.ShopId, x.IsTerminal });
            e.HasMany(x => x.Items)
                .WithOne(i => i.Invoice)
                .HasForeignKey(i => i.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<InvoiceItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SkuKeyNorm).HasMaxLength(512);
            e.HasIndex(x => x.SkuKeyNorm);
            e.HasIndex(x => x.InvoiceId);
        });

        mb.Entity<ReturnRecord>(e =>
        {
            e.HasKey(x => x.Id);                 // PK = id из API
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(64);
            e.HasIndex(x => x.ShopId);
            e.HasIndex(x => new { x.ShopId, x.DateCreated });
            e.HasIndex(x => new { x.ShopId, x.IsTerminal });
            e.HasMany(x => x.Items)
                .WithOne(i => i.Return)
                .HasForeignKey(i => i.ReturnId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ReturnItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SkuKeyNorm).HasMaxLength(512);
            e.HasIndex(x => x.SkuKeyNorm);
            e.HasIndex(x => x.ReturnId);
        });

        mb.Entity<SkuRecord>(e =>
        {
            // Одна строка на (магазин, SKU) — последний срез карточки.
            e.HasKey(x => new { x.ShopId, x.SkuId });
            e.Property(x => x.SkuKeyNorm).HasMaxLength(512);
            e.HasIndex(x => new { x.ShopId, x.SkuKeyNorm });
        });

        mb.Entity<ShopSyncState>(e =>
        {
            e.HasKey(x => x.ShopId);
            e.Property(x => x.ShopId).ValueGeneratedNever();
            e.Property(x => x.Status).HasConversion<int>();
        });
    }
}
