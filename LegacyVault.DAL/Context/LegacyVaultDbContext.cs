using Microsoft.EntityFrameworkCore;

namespace LegacyVault.DAL.Context;

// Setup placeholder only. Replace with Database First output after the
// authorized development/test connection and real schema are available.
public partial class LegacyVaultDbContext : DbContext
{
    public LegacyVaultDbContext(DbContextOptions<LegacyVaultDbContext> options)
        : base(options)
    {
    }
}
