using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class BeneficiaryAssignmentTests
{
    private static VaultService Service(LegacyVaultDbContext db) => new(new VaultRepository(db), new FakeStore(), new DocumentProtection(new SecurityOptions()));
    private static async Task<(int Owner, int Asset)> Seed(DbContextOptions<LegacyVaultDbContext> options)
    {
        await using var db = new LegacyVaultDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var identity = new IdentityService(new VaultRepository(db));
        var owner = 0;
        foreach (var email in new[] { "owner@example.test", "bob@example.test", "carol@example.test" })
        {
            var result = await identity.Register(new RegisterInput { FullName = email.Split('@')[0], Email = email, Password = "Registration123!", ConfirmPassword = "Registration123!" }, default);
            if (owner == 0) owner = result.UserId;
        }
        var vault = await Service(db).CreateVault(owner, new CreateVaultInput { Name = "Fixture" }, default);
        var asset = await Service(db).CreateAsset(owner, new CreateAssetInput { VaultId = vault.VaultId, Name = "Fixture", Type = "Other" }, default);
        return (owner, asset.AssetId);
    }
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var options = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;
        var fixture = await Seed(options);
        DateTime originalRecipientTimestamp;
        await using (var db = new LegacyVaultDbContext(options))
        {
            originalRecipientTimestamp = (await db.Users.SingleAsync(x => x.Email == "bob@example.test")).UpdatedAt;
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_assignment BEFORE INSERT ON BENEFICIARY_ASSIGNMENTS BEGIN SELECT RAISE(ABORT, 'Test assignment failure'); END;");
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            try { await Service(db).AssignBeneficiary(fixture.Owner, fixture.Asset, new AssignBeneficiaryInput { Email = "bob@example.test" }, default); throw new Exception("Expected database failure"); }
            catch (DbUpdateException) { }
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            var bob = await db.Users.Include(x => x.Roles).SingleAsync(x => x.Email == "bob@example.test");
            check(await db.BeneficiaryAssignments.CountAsync() == 0 && await db.AuditLogs.CountAsync() == 0, "failed beneficiary save rolls back assignment and audit together");
            check(bob.Roles.Single().RoleName == VaultService.OwnerRole && bob.UpdatedAt == originalRecipientTimestamp && !await db.Roles.AnyAsync(x => x.RoleName == VaultService.BeneficiaryRole), "failed assignment cannot partially grant beneficiary access or create its role");
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_assignment;");
            var beneficiaryRole = new Role { RoleName = VaultService.BeneficiaryRole };
            foreach (var recipient in await db.Users.Where(x => x.Email != "owner@example.test").ToListAsync()) recipient.Roles.Add(beneficiaryRole);
            await db.SaveChangesAsync();
        }
        var competingOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection)
            .AddInterceptors(new CompetingAssignment(options, fixture.Owner, fixture.Asset)).Options;
        await using (var db = new LegacyVaultDbContext(competingOptions))
        {
            try { await Service(db).AssignBeneficiary(fixture.Owner, fixture.Asset, new AssignBeneficiaryInput { Email = "bob@example.test", Allocation = 60 }, default); throw new Exception("Expected concurrency conflict"); }
            catch (RepositoryConflictException) { check(true, "simultaneous allocation updates are rejected by asset concurrency token"); }
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            var allocations = await db.BeneficiaryAssignments.Include(x => x.Beneficiary).ToListAsync();
            check(allocations.Count == 1 && allocations.Single().Beneficiary.Email == "carol@example.test" && allocations.Single().Allocation == 60 && await db.AuditLogs.CountAsync() == 1, "concurrent rejected assignment leaves total allocation within 100 percent and no partial audit");
        }
    }
    private sealed class CompetingAssignment(DbContextOptions<LegacyVaultDbContext> options, int owner, int asset) : SaveChangesInterceptor
    {
        private bool ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ran)
            {
                ran = true;
                // Simulate another request committing between the original request's read and save.
                await using var other = new LegacyVaultDbContext(options);
                await Service(other).AssignBeneficiary(owner, asset, new AssignBeneficiaryInput { Email = "carol@example.test", Allocation = 60 }, cancellationToken);
            }
            return result;
        }
    }
}
