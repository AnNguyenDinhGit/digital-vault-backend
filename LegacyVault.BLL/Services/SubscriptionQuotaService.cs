using System.Collections.Concurrent;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;

namespace LegacyVault.BLL.Services;

public sealed class SubscriptionQuotaOptions
{
    // Match the existing schema defaults for an Owner without a paid subscription.
    public int FreeMaxVaults { get; set; } = 1;
    public long FreeMaxStorageBytes { get; set; } = 1_073_741_824;
}

public sealed class SubscriptionQuotaService(
    IPaymentRepository payments,
    IVaultRepository vaults,
    IDocumentStore store,
    SubscriptionQuotaOptions options)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UserLocks = new();

    public async Task<IDisposable> LockUser(int userId, CancellationToken ct)
    {
        var gate = UserLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new GateLease(gate);
    }

    public async Task EnsureCanCreateVault(int userId, CancellationToken ct)
    {
        var limits = await Limits(userId, ct);
        var activeCount = (await vaults.OwnerVaults(userId, ct)).Count(x => x.Status == "Active");
        if (activeCount >= limits.MaxVaults)
            throw new WorkflowException(409, $"Vault limit reached ({limits.MaxVaults}). Upgrade your plan or close an active vault.");
    }

    public async Task EnsureStorageAvailable(int userId, long additionalBytes, CancellationToken ct)
    {
        var limits = await Limits(userId, ct);
        var paths = await payments.StorageDocumentPaths(userId, ct);
        long used = 0;
        foreach (var path in paths)
        {
            try { used = checked(used + await store.Length(path, ct)); }
            catch (OverflowException) { throw new WorkflowException(409, "Stored data size exceeds the supported quota range."); }
        }
        if (additionalBytes < 0 || used > limits.MaxStorageBytes - additionalBytes)
            throw new WorkflowException(409, $"Storage limit exceeded. Used {used} bytes; limit is {limits.MaxStorageBytes} bytes.");
    }

    private async Task<(int MaxVaults, long MaxStorageBytes)> Limits(int userId, CancellationToken ct)
    {
        var subscription = await payments.CurrentSubscription(userId, DateTime.UtcNow, ct);
        if (subscription is not null) return (subscription.Plan.MaxVaults, subscription.Plan.MaxStorageBytes);
        return (options.FreeMaxVaults, options.FreeMaxStorageBytes);
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
