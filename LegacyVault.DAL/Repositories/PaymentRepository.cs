using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LegacyVault.DAL.Repositories;

public sealed record PaymentOrder(string TransactionCode, decimal Amount, string PlanName);
public sealed record PlanUsageStatsRow(
    int PlanId,
    int ActiveUsersCount,
    int ScheduledUsersCount,
    int ExpiredUsersCount,
    int TotalSubscribersCount,
    decimal TotalRevenue);
public sealed record PlanMonthlyStatsRow(
    int PlanId,
    string PlanName,
    int SoldCount,
    decimal Revenue,
    int ActiveUsers);

public interface IPaymentRepository
{
    Task<List<SubscriptionPlan>> ActivePlans(CancellationToken ct);
    Task<List<SubscriptionPlan>> AllPlans(CancellationToken ct);
    Task<Dictionary<int, PlanUsageStatsRow>> PlanUsageStatistics(DateTime now, CancellationToken ct);
    Task<List<PlanMonthlyStatsRow>> PlanMonthlyStatistics(DateTime monthStart, DateTime monthEnd, DateTime now, CancellationToken ct);
    Task<SubscriptionPlan?> Plan(int planId, CancellationToken ct);
    Task<bool> PlanExistsAny(int planId, CancellationToken ct);
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
    Task<(int Total, List<UserSubscription> Items)> AdminUserSubscriptions(string? search, string? status, int? planId, DateTime now, int skip, int take, CancellationToken ct);
    Task<(int Total, List<UserSubscription> Items)> PlanSubscribers(int planId, string? status, DateTime now, int skip, int take, CancellationToken ct);
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
        .Where(x => x.IsActive && !db.AuditLogs.Any(a => a.Action == "DeleteSubscriptionPlan" && a.EntityType == "SubscriptionPlan" && a.EntityId == x.PlanId))
        .OrderBy(x => x.Price).ToListAsync(ct);

    public Task<List<SubscriptionPlan>> AllPlans(CancellationToken ct) => db.SubscriptionPlans.AsNoTracking()
        .Where(x => !db.AuditLogs.Any(a => a.Action == "DeleteSubscriptionPlan" && a.EntityType == "SubscriptionPlan" && a.EntityId == x.PlanId))
        .OrderBy(x => x.Price).ThenBy(x => x.PlanId).ToListAsync(ct);

    public async Task<Dictionary<int, PlanUsageStatsRow>> PlanUsageStatistics(DateTime now, CancellationToken ct)
    {
        var subs = await db.UserSubscriptions.AsNoTracking()
            .Where(x => x.Status == "Active" || x.Status == "Scheduled" || x.Status == "Expired")
            .Select(x => new { x.PlanId, x.UserId, x.Status, x.EndDate })
            .ToListAsync(ct);

        var revenues = await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.Status == "Success" && x.SubscriptionId != null && x.Subscription != null)
            .GroupBy(x => x.Subscription!.PlanId)
            .Select(g => new { PlanId = g.Key, TotalRevenue = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.PlanId, x => x.TotalRevenue, ct);

        var result = new Dictionary<int, PlanUsageStatsRow>();
        foreach (var group in subs.GroupBy(x => x.PlanId))
        {
            var activeUsers = group.Where(x => x.Status == "Active" && x.EndDate > now).Select(x => x.UserId).Distinct().Count();
            var scheduledUsers = group.Where(x => x.Status == "Scheduled").Select(x => x.UserId).Distinct().Count();
            var expiredUsers = group.Where(x => x.Status == "Expired" || (x.Status == "Active" && x.EndDate <= now)).Select(x => x.UserId).Distinct().Count();
            var totalSubscribers = group.Select(x => x.UserId).Distinct().Count();
            revenues.TryGetValue(group.Key, out var revenue);
            result[group.Key] = new PlanUsageStatsRow(group.Key, activeUsers, scheduledUsers, expiredUsers, totalSubscribers, revenue);
        }

        foreach (var (planId, revenue) in revenues)
        {
            if (!result.ContainsKey(planId))
            {
                result[planId] = new PlanUsageStatsRow(planId, 0, 0, 0, 0, revenue);
            }
        }

        return result;
    }

    public async Task<List<PlanMonthlyStatsRow>> PlanMonthlyStatistics(DateTime monthStart, DateTime monthEnd, DateTime now, CancellationToken ct)
    {
        var allPlans = await db.SubscriptionPlans.AsNoTracking()
            .OrderBy(x => x.Price)
            .ThenBy(x => x.PlanId)
            .ToListAsync(ct);

        var deletedPlanIds = (await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "DeleteSubscriptionPlan" && a.EntityType == "SubscriptionPlan" && a.EntityId != null)
            .Select(a => a.EntityId!.Value)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var monthlySales = await db.PaymentTransactions.AsNoTracking()
            .Where(x => x.Status == "Success" && x.CreatedAt >= monthStart && x.CreatedAt < monthEnd && x.SubscriptionId != null && x.Subscription != null)
            .GroupBy(x => x.Subscription!.PlanId)
            .Select(g => new
            {
                PlanId = g.Key,
                SoldCount = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .ToDictionaryAsync(x => x.PlanId, ct);

        var activeSubRows = await db.UserSubscriptions.AsNoTracking()
            .Where(x => (x.Status == "Active" && x.EndDate > now) || (x.Status == "Scheduled" && x.StartDate <= now && x.EndDate > now))
            .Select(x => new { x.PlanId, x.UserId })
            .ToListAsync(ct);

        var activeUsersByPlan = activeSubRows
            .GroupBy(x => x.PlanId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().Count());

        var rows = new List<PlanMonthlyStatsRow>();
        foreach (var plan in allPlans)
        {
            monthlySales.TryGetValue(plan.PlanId, out var sale);
            activeUsersByPlan.TryGetValue(plan.PlanId, out var activeUsers);
            var soldCount = sale?.SoldCount ?? 0;
            var revenue = sale?.Revenue ?? 0m;

            // Các gói đã bị xóa khỏi giao diện nhưng có phát sinh giao dịch trong tháng hoặc còn người đang dùng vẫn trả về để khớp tổng doanh thu
            if (deletedPlanIds.Contains(plan.PlanId) && soldCount == 0 && activeUsers == 0)
            {
                continue;
            }

            rows.Add(new PlanMonthlyStatsRow(
                PlanId: plan.PlanId,
                PlanName: plan.PlanName,
                SoldCount: soldCount,
                Revenue: revenue,
                ActiveUsers: activeUsers));
        }

        return rows;
    }

    public Task<SubscriptionPlan?> Plan(int planId, CancellationToken ct) => db.SubscriptionPlans
        .SingleOrDefaultAsync(x => x.PlanId == planId && !db.AuditLogs.Any(a => a.Action == "DeleteSubscriptionPlan" && a.EntityType == "SubscriptionPlan" && a.EntityId == x.PlanId), ct);

    public Task<bool> PlanExistsAny(int planId, CancellationToken ct) => db.SubscriptionPlans
        .AnyAsync(x => x.PlanId == planId, ct);
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
        plan.IsActive = false;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "DeleteSubscriptionPlan",
            EntityType = "SubscriptionPlan",
            EntityId = plan.PlanId,
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<(List<int> DeletedPlanIds, List<int> SkippedPlanIds)> DeleteAllInactivePlans(int adminUserId, DateTime now, CancellationToken ct)
    {
        var inactivePlans = await db.SubscriptionPlans
            .Where(x => !x.IsActive && !db.AuditLogs.Any(a => a.Action == "DeleteSubscriptionPlan" && a.EntityType == "SubscriptionPlan" && a.EntityId == x.PlanId))
            .OrderBy(x => x.PlanId)
            .ToListAsync(ct);
        var deletedIds = new List<int>();
        var skippedIds = new List<int>();

        foreach (var plan in inactivePlans)
        {
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

    public async Task<(int Total, List<UserSubscription> Items)> AdminUserSubscriptions(
        string? search,
        string? status,
        int? planId,
        DateTime now,
        int skip,
        int take,
        CancellationToken ct)
    {
        var query = db.UserSubscriptions.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Plan)
            .Where(x => x.Status == "Active" || x.Status == "Scheduled" || x.Status == "Expired");

        if (planId.HasValue)
        {
            query = query.Where(x => x.PlanId == planId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status switch
            {
                "Active" => query.Where(x => (x.Status == "Active" && x.EndDate > now) || (x.Status == "Scheduled" && x.StartDate <= now && x.EndDate > now)),
                "Scheduled" => query.Where(x => x.Status == "Scheduled" && x.StartDate > now),
                "Expired" => query.Where(x => x.Status == "Expired" || ((x.Status == "Active" || x.Status == "Scheduled") && x.EndDate <= now)),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                (x.User != null && x.User.Email.ToLower().Contains(term)) ||
                (x.User != null && x.User.FullName.ToLower().Contains(term)) ||
                (x.Plan != null && x.Plan.PlanName.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.SubscriptionId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (total, items);
    }

    public async Task<(int Total, List<UserSubscription> Items)> PlanSubscribers(
        int planId,
        string? status,
        DateTime now,
        int skip,
        int take,
        CancellationToken ct)
    {
        var query = db.UserSubscriptions.AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Plan)
            .Where(x => x.PlanId == planId && x.Status != "Pending" && x.Status != "Failed");

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status switch
            {
                "Active" => query.Where(x => (x.Status == "Active" && x.EndDate > now) || (x.Status == "Scheduled" && x.StartDate <= now && x.EndDate > now)),
                "Scheduled" => query.Where(x => x.Status == "Scheduled" && x.StartDate > now),
                "Expired" => query.Where(x => x.Status == "Expired" || x.Status == "Replaced" || ((x.Status == "Active" || x.Status == "Scheduled") && x.EndDate <= now)),
                "Replaced" => query.Where(x => x.Status == "Replaced"),
                _ => query
            };
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.SubscriptionId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (total, items);
    }

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
