using Microsoft.EntityFrameworkCore;
using LegacyVault.DAL.Entities; // Import namespace Entities

namespace LegacyVault.DAL.Context;

public partial class LegacyVaultDbContext : DbContext
{
    public LegacyVaultDbContext(DbContextOptions<LegacyVaultDbContext> options)
        : base(options)
    {
    }

    // Khai báo các DbSet tương ứng với các bảng trong Database
    public DbSet<User> Users { get; set; }
    public DbSet<DigitalAsset> DigitalAssets { get; set; }
    public DbSet<AssetDocument> AssetDocuments { get; set; }
    public DbSet<BeneficiaryReceipt> BeneficiaryReceipts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cấu hình bổ sung nếu cần (ví dụ Primary Keys / Foreign Keys)
        modelBuilder.Entity<User>().HasKey(u => u.UserId);
        modelBuilder.Entity<DigitalAsset>().HasKey(a => a.AssetId);
        modelBuilder.Entity<AssetDocument>().HasKey(d => d.DocumentId);
        modelBuilder.Entity<BeneficiaryReceipt>().HasKey(r => r.ReceiptId);
    }
}