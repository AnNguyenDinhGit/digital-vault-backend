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
    Task<bool> HasActiveOrScheduledSubscribers(int planId, DateTime now, CancellationToken ct);
    Task DeleteInactivePlan(SubscriptionPlan plan, int adminUserId, CancellationToken ct);
    Task<(List<int> DeletedPlanIds, List<int> SkippedPlanIds)> DeleteAllInactivePlans(int adminUserId, DateTime now, CancellationToken ct);
    Task<UserSubscription?> CurrentSubscription(int userId, DateTime now, CancellationToken ct);
    Task<UserSubscription?> CurrentPaidSubscription(int userId, DateTime now, CancellationToken ct);
    Task<UserSubscription?> ScheduledSubscription(int userId, CancellationToken ct);
    Task<UserSubscription?> LastSubscription(int userId, CancellationToken ct);
    Task<bool> IsReadOnly(int userId, DateTime now, CancellationToken ct);
    Task ActivateScheduledSubscriptionsIfDue(int userId, DateTime now, CancellationToken ct);
    Task<int> CountUserTransactions(int userId, string? status, CancellationToken ct);
    Task<List<PaymentTransaction>> UserTransactions(int userId, string? status, int skip, int take, CancellationToken ct);
    Task<int> CountTransactions(int? userId, string? status, CancellationToken ct);
    Task<List<PaymentTransaction>> Transactions(int? userId, string? status, int skip, int take, CancellationToken ct);
    Task<PaymentTransaction?> Transaction(long transactionId, CancellationToken ct);
    Task<List<string>> StorageDocumentPaths(int ownerId, CancellationToken ct);
    void AuditPlanChange(int adminUserId, string action, int planId);
    Task Save(CancellationToken ct);
    Task<PaymentOrder> CreateOrder(int userId, int planId, string transactionCode, CancellationToken ct);
    Task<PaymentOrder> CreateOrder(int userId, int planId, string transactionCode, decimal amount, string? changeType, DateTime? startsAt, DateTime? endsAt, CancellationToken ct);
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

    public Task<bool> HasActiveOrScheduledSubscribers(int planId, DateTime now, CancellationToken ct) =>
        db.UserSubscriptions.AnyAsync(
            x => x.PlanId == planId && ((x.Status == "Active" && x.EndDate > now) || x.Status == "Scheduled"),
            ct);

    public async Task DeleteInactivePlan(SubscriptionPlan plan, int adminUserId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var subs = await db.UserSubscriptions.Where(x => x.PlanId == plan.PlanId).ToListAsync(ct);
        if (subs.Count > 0)
        {
            var subIds = subs.Select(x => (int?)x.SubscriptionId).ToList();
            var txs = await db.PaymentTransactions.Where(x => subIds.Contains(x.SubscriptionId)).ToListAsync(ct);
            foreach (var tx in txs)
            {
                tx.SubscriptionId = null;
            }
            db.UserSubscriptions.RemoveRange(subs);
        }

        var planId = plan.PlanId;
        db.SubscriptionPlans.Remove(plan);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "DeleteSubscriptionPlan",
            EntityType = "SubscriptionPlan",
            EntityId = planId,
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<(List<int> DeletedPlanIds, List<int> SkippedPlanIds)> DeleteAllInactivePlans(int adminUserId, DateTime now, CancellationToken ct)
    {
        var inactivePlans = await db.SubscriptionPlans.Where(x => !x.IsActive).OrderBy(x => x.PlanId).ToListAsync(ct);
        var deletedIds = new List<int>();
        var skippedIds = new List<int>();

        foreach (var plan in inactivePlans)
        {
            if (await HasActiveOrScheduledSubscribers(plan.PlanId, now, ct))
            {
                skippedIds.Add(plan.PlanId);
                continue;
            }
            await DeleteInactivePlan(plan, adminUserId, ct);
            deletedIds.Add(plan.PlanId);
        }

        return (deletedIds, skippedIds);
    }

    public async Task ActivateScheduledSubscriptionsIfDue(int userId, DateTime now, CancellationToken ct)
    {
        var dueScheduled = await db.UserSubscriptions
            .Include(x => x.Plan)
            .Where(x => x.UserId == userId && x.Status == "Scheduled" && x.StartDate <= now)
            .OrderBy(x => x.StartDate)
            .ToListAsync(ct);

        if (dueScheduled.Count > 0)
        {
            var existingActive = await db.UserSubscriptions
                .Where(x => x.UserId == userId && x.Status == "Active")
                .ToListAsync(ct);

            foreach (var active in existingActive)
            {
                active.Status = "Expired";
                if (active.EndDate > now) active.EndDate = now;
            }

            foreach (var sub in dueScheduled)
            {
                sub.Status = "Active";
            }

            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserSubscription?> CurrentSubscription(int userId, DateTime now, CancellationToken ct)
    {
        await ActivateScheduledSubscriptionsIfDue(userId, now, ct);
        var subs = await db.UserSubscriptions.AsNoTracking()
            .Include(x => x.Plan).Where(x => x.UserId == userId && x.Status == "Active" && x.EndDate > now)
            .OrderByDescending(x => x.EndDate)
            .ToListAsync(ct);
        return subs.OrderByDescending(x => x.Plan.Price).ThenByDescending(x => x.EndDate).FirstOrDefault();
    }

    public async Task<UserSubscription?> CurrentPaidSubscription(int userId, DateTime now, CancellationToken ct)
    {
        await ActivateScheduledSubscriptionsIfDue(userId, now, ct);
        return await db.UserSubscriptions.AsNoTracking()
            .Include(x => x.Plan).Where(x => x.UserId == userId && x.Status == "Active" && x.EndDate > now && x.Plan.Price > 0)
            .OrderByDescending(x => x.EndDate).FirstOrDefaultAsync(ct);
    }

    public Task<UserSubscription?> ScheduledSubscription(int userId, CancellationToken ct) => db.UserSubscriptions.AsNoTracking()
        .Include(x => x.Plan).Where(x => x.UserId == userId && x.Status == "Scheduled")
        .OrderBy(x => x.StartDate).FirstOrDefaultAsync(ct);

    public Task<UserSubscription?> LastSubscription(int userId, CancellationToken ct) => db.UserSubscriptions.AsNoTracking()
        .Include(x => x.Plan).Where(x => x.UserId == userId && x.Status != "Pending" && x.Status != "Failed")
        .OrderByDescending(x => x.EndDate).FirstOrDefaultAsync(ct);

    public async Task<bool> IsReadOnly(int userId, DateTime now, CancellationToken ct)
    {
        await ActivateScheduledSubscriptionsIfDue(userId, now, ct);

        var hasActive = await db.UserSubscriptions
            .AnyAsync(x => x.UserId == userId && x.Status == "Active" && x.EndDate > now, ct);
        if (hasActive) return false;

        var hasScheduled = await db.UserSubscriptions
            .AnyAsync(x => x.UserId == userId && x.Status == "Scheduled", ct);
        if (hasScheduled) return false;

        var hadPaid = await db.UserSubscriptions
            .Include(x => x.Plan)
            .AnyAsync(x => x.UserId == userId && x.Plan.Price > 0, ct);

        return hadPaid;
    }

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

    public Task<PaymentOrder> CreateOrder(int userId, int planId, string transactionCode, CancellationToken ct)
    {
        var plan = db.SubscriptionPlans.SingleOrDefault(x => x.PlanId == planId && x.IsActive);
        var amount = plan?.Price ?? 0m;
        return CreateOrder(userId, planId, transactionCode, amount, null, null, null, ct);
    }

    public async Task<PaymentOrder> CreateOrder(
        int userId,
        int planId,
        string transactionCode,
        decimal amount,
        string? changeType,
        DateTime? startsAt,
        DateTime? endsAt,
        CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.PlanId == planId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Subscription plan not found or inactive.");

        var userExists = await db.Users.AnyAsync(x => x.UserId == userId && x.Status == "Active", ct);
        if (!userExists) throw new KeyNotFoundException("User not found or inactive.");

        // Kiểm tra giá: Cho phép giá = 0 (Gói Free) HOẶC nằm trong khoảng 5.000 đến 1.000.000.000 (Gói VNPay)
        if (amount != 0m && (amount is < 5000m or > 1_000_000_000m || amount != decimal.Truncate(amount)))
            throw new InvalidOperationException("VNPay plan price must be a whole-VND amount from 5,000 to 1,000,000,000.");

        var now = DateTime.UtcNow;
        var subStart = startsAt ?? now;
        var duration = plan.DurationDays > 0 ? plan.DurationDays : 30;
        var subEnd = endsAt ?? (plan.Price == 0 ? DateTime.UtcNow.AddYears(100) : subStart.AddDays(duration));

        var subscription = new UserSubscription
        {
            UserId = userId,
            PlanId = plan.PlanId,
            StartDate = subStart,
            EndDate = subEnd,
            Status = "Pending"
        };

        db.UserSubscriptions.Add(subscription);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            UserId = userId,
            Subscription = subscription,
            Amount = amount,
            PaymentMethod = amount == 0m ? "Internal" : "VNPay", // Đánh dấu phương thức cho gói Free
            TransactionCode = transactionCode,
            Status = "Pending",
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        return new(transactionCode, amount, plan.PlanName);
    }

    public Task<PaymentTransaction?> Transaction(string transactionCode, CancellationToken ct) =>
        db.PaymentTransactions.Include(x => x.Subscription).ThenInclude(x => x!.Plan)
            .SingleOrDefaultAsync(x => x.TransactionCode == transactionCode, ct);

    public async Task<bool> ApplyGatewayResult(string transactionCode, bool success, CancellationToken ct)
    {
        await using var dbTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var payment = await db.PaymentTransactions.Include(x => x.Subscription).ThenInclude(x => x!.Plan)
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
                var sub = payment.Subscription;
                var isFree = sub.Plan.Price == 0;

                if (sub.StartDate > now)
                {
                    // Scheduled package waiting to start
                    sub.Status = "Scheduled";
                }
                else
                {
                    var existing = await db.UserSubscriptions
                        .Include(x => x.Plan)
                        .Where(x => x.UserId == payment.UserId && x.Status == "Active" && x.SubscriptionId != sub.SubscriptionId)
                        .ToListAsync(ct);

                    foreach (var s in existing)
                    {
                        if (s.Plan.Price > 0 && s.EndDate > now)
                        {
                            s.Status = "Replaced";
                        }
                        else
                        {
                            s.Status = "Expired";
                        }
                        s.EndDate = now;
                    }

                    sub.StartDate = now;
                    sub.EndDate = isFree ? DateTime.UtcNow.AddYears(100) : now.AddDays(sub.Plan.DurationDays > 0 ? sub.Plan.DurationDays : 30);
                    sub.Status = "Active";
                }
            }
            else payment.Subscription.Status = "Failed";
        }
        await db.SaveChangesAsync(ct);
        await dbTransaction.CommitAsync(ct);
        return false;
    }
}
