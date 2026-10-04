using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class RelationalRegistrationTests
{
    public static async Task Run(Action<bool, string> check)
    {
        using (var modelContext = new LegacyVaultDbContext(new DbContextOptionsBuilder<LegacyVaultDbContext>()
            .UseSqlServer("Server=localhost;Database=MetadataOnly;Integrated Security=True;Encrypt=True").Options))
        {
            var model = modelContext.Model;
            var user = model.FindEntityType(typeof(User))!;
            var auth = model.FindEntityType(typeof(Authentication))!;
            var role = model.FindEntityType(typeof(Role))!;
            check(user.GetTableName() == "USERS" && auth.GetTableName() == "AUTHENTICATIONS" && role.GetTableName() == "ROLES", "scaffold maps registration to the existing tables");
            check(user.FindProperty(nameof(User.Email))!.GetMaxLength() == 150 && user.FindProperty(nameof(User.FullName))!.GetMaxLength() == 100 && user.FindProperty(nameof(User.Phone))!.GetMaxLength() == 20, "registration input capacities match scaffolded column lengths");
            check(user.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == nameof(User.Email)) && role.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == nameof(Role.RoleName)), "scaffold enforces unique email and role name");
            var join = model.FindEntityType("UserRole")!;
            check(join.GetTableName() == "USER_ROLES" && join.FindPrimaryKey()!.Properties.Select(x => x.Name).SequenceEqual(new[] { "UserId", "RoleId" }) && join.GetForeignKeys().Count() == 2, "scaffold defines role join composite key and both foreign keys");
            check(auth.GetForeignKeys().Single().Properties.Single().Name == nameof(Authentication.UserId), "authentication references actual user primary key");
            check(model.FindEntityType(typeof(AuditLog))!.FindProperty(nameof(AuditLog.Result))!.GetMaxLength() == 20 && "Pending_Legal".Length <= 20, "deceased upload audit result fits scaffolded length");
            Console.WriteLine($"Scaffold model: {model.GetEntityTypes().Count()} mapped tables, including USER_ROLES.");
        }

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var options = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;
        await using (var setup = new LegacyVaultDbContext(options))
        {
            // Only this isolated in-memory database is created; never the configured SQL Server database.
            await setup.Database.EnsureCreatedAsync();
            setup.Roles.Add(new Role { RoleName = VaultService.AdminRole });
            await setup.SaveChangesAsync();
        }
        RegisterInput Input(string email) => new() { FullName = "Relational Test", Email = email, Password = "Registration123!", ConfirmPassword = "Registration123!", Phone = "0901234567" };
        RegistrationDto first;
        await using (var db = new LegacyVaultDbContext(options))
        {
            first = await new IdentityService(new VaultRepository(db)).Register(Input(" First@Example.test "), default);
            check(first.UserId > 0 && first.Roles.SequenceEqual(new[] { VaultService.OwnerRole }), "real EF repository registers when owner role is missing");
        }
        int ownerRoleId;
        await using (var db = new LegacyVaultDbContext(options))
        {
            var user = await db.Users.Include(x => x.Authentications).Include(x => x.Roles).SingleAsync();
            ownerRoleId = user.Roles.Single().RoleId;
            check(user.UserId == first.UserId && user.Email == "first@example.test" && user.Status == "Active", "registration persists and reloads scaffolded user fields");
            var authentication = user.Authentications.Single();
            check(authentication.UserId == first.UserId && authentication.AuthId > 0 && authentication.PasswordHash.Length <= 255 && IdentityService.VerifyPassword("Registration123!", authentication.PasswordHash), "real database authentication FK and password hash are correct");
            check(await db.Set<Dictionary<string, object>>("UserRole").CountAsync() == 1 && user.Roles.Single().RoleName == VaultService.OwnerRole, "actual USER_ROLES row grants only owner access");
            check(await db.Roles.CountAsync() == 2, "registration preserves preexisting administrator role without granting it");
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            await new IdentityService(new VaultRepository(db)).Register(Input("second@example.test"), default);
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            check(await db.Roles.CountAsync() == 2 && await db.Set<Dictionary<string, object>>("UserRole").CountAsync() == 2 && await db.Roles.Where(x => x.RoleId == ownerRoleId).SelectMany(x => x.Users).CountAsync() == 2, "second registration reuses existing role and persists a second join");
            var login = await new IdentityService(new VaultRepository(db)).Login("FIRST@EXAMPLE.TEST", "Registration123!", default);
            check(login.UserId == first.UserId, "registered account logs in through real EF query");
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            try { await new IdentityService(new VaultRepository(db)).Register(Input("FIRST@EXAMPLE.TEST"), default); throw new Exception("Expected duplicate email rejection"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 409 && await db.Users.CountAsync() == 2, "case-insensitive duplicate registration leaves database unchanged"); }
        }

        await using var rollbackConnection = new SqliteConnection("Data Source=:memory:");
        await rollbackConnection.OpenAsync();
        rollbackConnection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var rollbackOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(rollbackConnection).Options;
        await using (var setup = new LegacyVaultDbContext(rollbackOptions)) { await setup.Database.EnsureCreatedAsync(); }
        var failingOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(rollbackConnection).AddInterceptors(new FailAfterDatabaseSave()).Options;
        await using (var db = new LegacyVaultDbContext(failingOptions))
        {
            try { await new IdentityService(new VaultRepository(db)).Register(Input("rollback@example.test"), default); throw new Exception("Expected injected failure"); }
            catch (DbUpdateException) { }
        }
        await using (var db = new LegacyVaultDbContext(rollbackOptions))
        {
            check(await db.Users.CountAsync() == 0 && await db.Authentications.CountAsync() == 0 && await db.Roles.CountAsync() == 0 && await db.Set<Dictionary<string, object>>("UserRole").CountAsync() == 0, "failed registration rolls back user, authentication, newly created role and join after SQL writes");
        }
    }
    private sealed class FailAfterDatabaseSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Injected failure before registration transaction commit.");
    }
}
