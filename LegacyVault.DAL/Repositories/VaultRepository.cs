using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
namespace LegacyVault.DAL.Repositories;
public interface IVaultRepository
{
    Task<User?> User(int id, CancellationToken ct);
    Task<User?> LoginUser(string email, CancellationToken ct);
    Task<User?> UserByEmail(string email, CancellationToken ct);
    Task<List<User>> UsersByRole(string role, CancellationToken ct);
    Task<Role?> Role(string name, CancellationToken ct);
    Task Register(User user, string defaultRole, CancellationToken ct);
    Task<List<DigitalVault>> OwnerVaults(int owner, CancellationToken ct);
    Task<DigitalVault?> Vault(int id, CancellationToken ct);
    Task<List<DigitalAsset>> OwnerAssets(int owner, CancellationToken ct);
    Task<DigitalAsset?> Asset(int id, CancellationToken ct);
    Task<List<HandoverRequest>> Requests(int executor, CancellationToken ct);
    Task<HandoverRequest?> Request(int id, CancellationToken ct);
    Task<List<HandoverCase>> Inherited(int beneficiary, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task Save(CancellationToken ct);
}
public sealed class VaultRepository(LegacyVaultDbContext db) : IVaultRepository
{
    public Task<User?> User(int id, CancellationToken ct) => db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.UserId == id, ct);
    public Task<User?> LoginUser(string email, CancellationToken ct) => db.Users.Include(x => x.Roles).Include(x => x.Authentications).SingleOrDefaultAsync(x => x.Email.ToUpper() == email.ToUpper(), ct);
    public Task<User?> UserByEmail(string email, CancellationToken ct) => db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Email.ToUpper() == email.ToUpper(), ct);

    public Task<List<User>> UsersByRole(string role, CancellationToken ct) =>
    db.Users
      .Include(x => x.Roles)
      .Where(x => x.Status == "Active" && x.Roles.Any(r => r.RoleName == role))
      .ToListAsync(ct);

    public Task<Role?> Role(string name, CancellationToken ct) => db.Roles.SingleOrDefaultAsync(x => x.RoleName == name, ct);
    public Task<List<DigitalVault>> OwnerVaults(int owner, CancellationToken ct) => db.DigitalVaults.AsNoTracking().Where(x => x.OwnerId == owner).OrderBy(x => x.VaultId).ToListAsync(ct);
    public Task<DigitalVault?> Vault(int id, CancellationToken ct) => db.DigitalVaults.SingleOrDefaultAsync(x => x.VaultId == id, ct);
    public async Task Register(User user, string defaultRole, CancellationToken ct)
    {
        // The scaffold maps User.Roles to USER_ROLES and User.Authentications to AUTHENTICATIONS.
        // Lock the SQL Server role range so simultaneous first registrations cannot create duplicate roles.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var role = db.Database.IsSqlServer()
                    ? await db.Roles.FromSqlInterpolated($"SELECT [RoleId], [RoleName] FROM [ROLES] WITH (UPDLOCK, HOLDLOCK) WHERE [RoleName] = {defaultRole}").SingleOrDefaultAsync(ct)
                    : await Role(defaultRole, ct);
                role ??= new Role { RoleName = defaultRole };
                user.Roles.Clear();
                user.Roles.Add(role);
                db.Users.Add(user);
                await Save(ct);
                await transaction.CommitAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 2 && ex.InnerException is SqlException { Number: 1205 })
            {
                ResetRegistrationForRetry(user);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
            }
            catch (SqlException ex) when (attempt < 2 && ex.Number == 1205)
            {
                ResetRegistrationForRetry(user);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
            }
        }
    }
    private void ResetRegistrationForRetry(User user)
    {
        db.ChangeTracker.Clear();
        user.UserId = 0;
        foreach (var auth in user.Authentications) { auth.AuthId = 0; auth.UserId = 0; }
        user.Roles.Clear();
    }
    private IQueryable<DigitalAsset> Assets => db.DigitalAssets.Include(x => x.Vault).Include(x => x.AssetDocuments).Include(x => x.BeneficiaryAssignments).ThenInclude(x => x.Beneficiary).AsSplitQuery();
    public Task<List<DigitalAsset>> OwnerAssets(int owner, CancellationToken ct) => Assets.Where(x => x.Vault.OwnerId == owner).OrderBy(x => x.AssetId).ToListAsync(ct);
    public Task<DigitalAsset?> Asset(int id, CancellationToken ct) => Assets.SingleOrDefaultAsync(x => x.AssetId == id, ct);
    private IQueryable<HandoverRequest> Handovers => db.HandoverRequests.Include(x => x.Vault).ThenInclude(x => x.ExecutorAssignments).Include(x => x.HandoverDocuments).Include(x => x.LegalVerifications).Include(x => x.HandoverCases).ThenInclude(x => x.Assignment).ThenInclude(x => x.Asset).AsSplitQuery();
    public Task<List<HandoverRequest>> Requests(int executor, CancellationToken ct) => Handovers.Where(x => x.ExecutorId == executor).OrderByDescending(x => x.InitiatedAt).ToListAsync(ct);
    public Task<HandoverRequest?> Request(int id, CancellationToken ct) => Handovers.SingleOrDefaultAsync(x => x.RequestId == id, ct);
    public Task<List<HandoverCase>> Inherited(int beneficiary, CancellationToken ct) => db.HandoverCases.Include(x => x.Request).ThenInclude(x => x.LegalVerifications).Include(x => x.Assignment).ThenInclude(x => x.Asset).ThenInclude(x => x.AssetDocuments).Where(x => x.Assignment.BeneficiaryId == beneficiary && x.Assignment.Status == "Active" && x.Request.LegalVerifications.Any(v => v.Status == "Approved") && (x.Status == "In_Progress" || x.Status == "Completed")).OrderBy(x => x.HandoverId).AsSplitQuery().ToListAsync(ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new RepositoryConflictException(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw new RepositoryDuplicateException(); }
    }
}
public sealed class RepositoryConflictException : Exception;
public sealed class RepositoryDuplicateException : Exception;
