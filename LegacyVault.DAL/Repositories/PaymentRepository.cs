using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LegacyVault.DAL.Repositories;

public sealed record PaymentOrder(string TransactionCode, decimal Amount, string PlanName);

public interface IPaymentRepository
{
    Task<List<SubscriptionPlan>> ActivePlans(CancellationToken ct);
    Task<List<SubscriptionPlan>> AllPlans(CancellationToken ct);
    Task<SubscriptionPlan?> Plan(int planId, CancellationToken ct);
    Task AddPlan(SubscriptionPlan plan, int adminUserId, CancellationToken ct);
    Task<UserSubscription?> CurrentSubscription(int userId, DateTime now, CancellationToken ct);
    Task<int> CountUserTransactions(int userId, string? status, CancellationToken ct);
    Task<List<PaymentTransaction>> UserTransactions(int userId, string? status, int skip, int take, CancellationToken ct);
    Task<int> CountTransactions(int? userId, string? status, CancellationToken ct);
    Task<List<PaymentTransaction>> Transactions(int? userId, string? status, int skip, int take, CancellationToken ct);
    Task<PaymentTransaction?> Transaction(long transactionId, CancellationToken ct);
    Task<List<string>> StorageDocumentPaths(int ownerId, CancellationToken ct);
    void AuditPlanChange(int adminUserId, string action, int planId);
    Task Save(CancellationToken ct);
    Task<PaymentOrder> CreateOrder(int userId, int planId, string transactionCode, CancellationToken ct);
    Task<PaymentTransaction?> Transaction(string transactionCode, CancellationToken ct);
    Task<bool> ApplyGatewayResult(string transactionCode, bool success, CancellationToken ct);
}

public sealed class PaymentRepository(LegacyVaultDbContext db) : IPaymentRepository
{
    public Task<List<SubscriptionPlan>> ActivePlans(CancellationToken ct) => db.SubscriptionPlans.AsNoTracking()
        .Where(x => x.IsActive).OrderBy(x => x.Price).ToListAsync(ct);

    public Task<List<SubscriptionPlan>> AllPlans(CancellationToken ct) => db.SubscriptionPlans.AsNoTracking()
        .OrderBy(x => x.Price).ThenBy(x => x.PlanId).ToListAsync(ct);

    public Task<SubscriptionPlan?> Plan(int planId, CancellationToken ct) => db.SubscriptionPlans.SingleOrDefaultAsync(x => x.PlanId == planId, ct);
    public async Task AddPlan(SubscriptionPlan plan, int adminUserId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { UserId = adminUserId, Action = "CreateSubscriptionPlan", EntityType = "SubscriptionPlan", EntityId = plan.PlanId, Result = "Success", Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public Task<UserSubscription?> CurrentSubscription(int userId, DateTime now, CancellationToken ct) => db.UserSubscriptions.AsNoTracking()
        .Include(x => x.Plan).Where(x => x.UserId == userId && x.Status == "Active" && x.EndDate > now)
        .OrderByDescending(x => x.EndDate).FirstOrDefaultAsync(ct);

    private IQueryable<PaymentTransaction> TransactionQuery() => db.PaymentTransactions
        .Include(x => x.User).Include(x => x.Subscription).ThenInclude(x => x!.Plan).AsNoTracking();

    public Task<int> CountUserTransactions(int userId, string? status, CancellationToken ct) => db.PaymentTransactions
        .CountAsync(x => x.UserId == userId && (status == null || x.Status == status), ct);

    public Task<List<PaymentTransaction>> UserTransactions(int userId, string? status, int skip, int take, CancellationToken ct) =>
        TransactionQuery().Where(x => x.UserId == userId && (status == null || x.Status == status))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TransactionId).Skip(skip).Take(take).ToListAsync(ct);

    public Task<int> CountTransactions(int? userId, string? status, CancellationToken ct) => db.PaymentTransactions
        .CountAsync(x => (!userId.HasValue || x.UserId == userId) && (status == null || x.Status == status), ct);

    public Task<List<PaymentTransaction>> Transactions(int? userId, string? status, int skip, int take, CancellationToken ct) =>
        TransactionQuery().Where(x => (!userId.HasValue || x.UserId == userId) && (status == null || x.Status == status))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TransactionId).Skip(skip).Take(take).ToListAsync(ct);

    public Task<PaymentTransaction?> Transaction(long transactionId, CancellationToken ct) =>
        TransactionQuery().SingleOrDefaultAsync(x => x.TransactionId == transactionId, ct);

    public async Task<List<string>> StorageDocumentPaths(int ownerId, CancellationToken ct)
    {
        var assetPaths = await db.AssetDocuments.Where(x => x.Asset.Vault.OwnerId == ownerId).Select(x => x.StoragePath).ToListAsync(ct);
        var handoverPaths = await db.HandoverDocuments.Where(x => x.Request.Vault.OwnerId == ownerId).Select(x => x.StoragePath).ToListAsync(ct);
        assetPaths.AddRange(handoverPaths);
        return assetPaths;
    }

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    public void AuditPlanChange(int adminUserId, string action, int planId) => db.AuditLogs.Add(new AuditLog
    {
        UserId = adminUserId, Action = action, EntityType = "SubscriptionPlan", EntityId = planId,
        Result = "Success", Timestamp = DateTime.UtcNow
    });

    public async Task<PaymentOrder> CreateOrder(int userId, int planId, string transactionCode, CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.PlanId == planId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Subscription plan not found or inactive.");

        var userExists = await db.Users.AnyAsync(x => x.UserId == userId && x.Status == "Active", ct);
        if (!userExists) throw new KeyNotFoundException("User not found or inactive.");

        // Kiểm tra giá: Cho phép giá = 0 (Gói Free) HOẶC nằm trong khoảng 5.000 đến 1.000.000.000 (Gói VNPay)
        if (plan.Price != 0m && (plan.Price is < 5000m or > 1_000_000_000m || plan.Price != decimal.Truncate(plan.Price)))
            throw new InvalidOperationException("VNPay plan price must be a whole-VND amount from 5,000 to 1,000,000,000.");

        var now = DateTime.UtcNow;
        var subscription = new UserSubscription
        {
            UserId = userId,
            PlanId = plan.PlanId,
            StartDate = now,
            EndDate = now.AddDays(plan.DurationDays),
            Status = "Pending"
        };

        db.UserSubscriptions.Add(subscription);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            UserId = userId,
            Subscription = subscription,
            Amount = plan.Price,
            PaymentMethod = plan.Price == 0m ? "Internal" : "VNPay", // Đánh dấu phương thức cho gói Free
            TransactionCode = transactionCode,
            Status = "Pending",
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        return new(transactionCode, plan.Price, plan.PlanName);
    }

    public Task<PaymentTransaction?> Transaction(string transactionCode, CancellationToken ct) =>
        db.PaymentTransactions.Include(x => x.Subscription).ThenInclude(x => x!.Plan)
            .SingleOrDefaultAsync(x => x.TransactionCode == transactionCode, ct);

    public async Task<bool> ApplyGatewayResult(string transactionCode, bool success, CancellationToken ct)
    {
        await using var dbTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var payment = await db.PaymentTransactions.Include(x => x.Subscription)
            .SingleOrDefaultAsync(x => x.TransactionCode == transactionCode, ct);
        if (payment is null) throw new KeyNotFoundException("Payment transaction not found.");
        if (payment.Status != "Pending")
        {
            var expectedStatus = success ? "Success" : "Failed";
            if (payment.Status != expectedStatus) throw new InvalidOperationException("Payment already has a different final status.");
            await dbTransaction.CommitAsync(ct);
            return true;
        }

        payment.Status = success ? "Success" : "Failed";
        if (payment.Subscription is not null)
        {
            if (success)
            {
                var now = DateTime.UtcNow;
                var existing = await db.UserSubscriptions.Where(x => x.UserId == payment.UserId && x.Status == "Active").ToListAsync(ct);
                foreach (var subscription in existing) { subscription.Status = "Expired"; subscription.EndDate = now; }
                payment.Subscription.StartDate = now;
                payment.Subscription.EndDate = now.AddDays((await db.SubscriptionPlans.SingleAsync(x => x.PlanId == payment.Subscription.PlanId, ct)).DurationDays);
                payment.Subscription.Status = "Active";
            }
            else payment.Subscription.Status = "Failed";
        }
        await db.SaveChangesAsync(ct);
        await dbTransaction.CommitAsync(ct);
        return false;
    }
}
