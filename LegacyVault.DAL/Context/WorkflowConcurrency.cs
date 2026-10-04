using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
namespace LegacyVault.DAL.Context;
public partial class LegacyVaultDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HandoverRequest>().Property(x => x.Status).IsConcurrencyToken();
        modelBuilder.Entity<DigitalAsset>().Property(x => x.UpdatedAt).IsConcurrencyToken();
        modelBuilder.Entity<Authentication>().Property(x => x.UpdatedAt).IsConcurrencyToken();
        modelBuilder.Entity<User>().Property(x => x.UpdatedAt).IsConcurrencyToken();
    }
}
