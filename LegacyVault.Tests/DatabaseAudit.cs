using LegacyVault.DAL.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Repositories;

internal static class DatabaseAudit
{
    public static async Task Run()
    {
        var root = Directory.GetCurrentDirectory();
        var api = Path.Combine(root, "LegacyVault.API");
        if (!Directory.Exists(api)) throw new InvalidOperationException("Run database audit from the solution directory.");
        var configuration = new ConfigurationBuilder().SetBasePath(api)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets("02a585f2-4bc3-4545-bc61-169f5637f022")
            .AddEnvironmentVariables().Build();
        var configured = configuration.GetConnectionString("LegacyVault");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("Database audit unavailable: ConnectionStrings:LegacyVault is not configured for this process. No SQL executed.");
            Environment.ExitCode = 2; return;
        }
        // Read-only SELECTs; never print the connection string, users, password hashes or secrets.
        var connectionString = new SqlConnectionStringBuilder(configured) { ConnectTimeout = 5 }.ConnectionString;
        await using var db = new LegacyVaultDbContext(new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlServer(connectionString, sql => sql.CommandTimeout(10)).Options);
        try
        {
            var roles = await db.Roles.AsNoTracking().OrderBy(x => x.RoleId).Select(x => new { x.RoleId, x.RoleName }).ToListAsync();
            Console.WriteLine($"Read-only database audit: {roles.Count} roles.");
            foreach (var role in roles) Console.WriteLine($"Role {role.RoleId}: {role.RoleName}");
            var ownerRole = await new VaultRepository(db).Role(VaultService.OwnerRole, default);
            Console.WriteLine(ownerRole is null ? "Owner role is missing; registration will initialize it." : $"Registration role lookup passed: {ownerRole.RoleName} (RoleId {ownerRole.RoleId}).");
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync()) columns.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}");
            var missing = new List<string>();
            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName()!;
                var schema = entity.GetSchema() ?? "dbo";
                var identifier = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var column = $"{schema}.{table}.{property.GetColumnName(identifier)}";
                    if (!columns.Contains(column)) missing.Add(column);
                }
            }
            Console.WriteLine($"Scaffold column comparison: {db.Model.GetEntityTypes().Count()} tables; {missing.Count} missing columns.");
            foreach (var column in missing) Console.WriteLine("Missing: " + column);
            await reader.CloseAsync();
            command.CommandText = "SELECT OBJECT_NAME(parent_object_id), definition FROM sys.check_constraints ORDER BY OBJECT_NAME(parent_object_id), name";
            await using var checks = await command.ExecuteReaderAsync();
            while (await checks.ReadAsync()) Console.WriteLine($"Check {checks.GetString(0)}: {checks.GetString(1)}");
            if (missing.Count != 0) Environment.ExitCode = 1;
        }
        catch (SqlException ex)
        {
            Console.WriteLine($"Database audit could not connect/query (SQL error {ex.Number}). No database data was modified.");
            Environment.ExitCode = 2;
        }
    }
}
