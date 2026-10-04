using System;
using System.Collections.Generic;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using LegacyVault.DAL.Entities; // Import namespace Entities

namespace LegacyVault.DAL.Context;

public partial class LegacyVaultDbContext : DbContext
{
    public LegacyVaultDbContext(DbContextOptions<LegacyVaultDbContext> options)
        : base(options)
    {
    }

<<<<<<< HEAD
    // Khai báo các DbSet tương ứng với các bảng trong Database
    public DbSet<User> Users { get; set; }
    public DbSet<DigitalAsset> DigitalAssets { get; set; }
    public DbSet<AssetDocument> AssetDocuments { get; set; }
    public DbSet<BeneficiaryReceipt> BeneficiaryReceipts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cấu hình bổ sung nếu cần (ví dụ Primary Keys / Foreign Keys)
        modelBuilder.Entity<User>().HasKey(u => u.UserId);
        modelBuilder.Entity<DigitalAsset>().HasKey(a => a.AssetId);
        modelBuilder.Entity<AssetDocument>().HasKey(d => d.DocumentId);
        modelBuilder.Entity<BeneficiaryReceipt>().HasKey(r => r.ReceiptId);
    }
}
=======
    public virtual DbSet<AssetDocument> AssetDocuments { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Authentication> Authentications { get; set; }

    public virtual DbSet<BeneficiaryAssignment> BeneficiaryAssignments { get; set; }

    public virtual DbSet<BeneficiaryReceipt> BeneficiaryReceipts { get; set; }

    public virtual DbSet<Conversation> Conversations { get; set; }

    public virtual DbSet<DigitalAsset> DigitalAssets { get; set; }

    public virtual DbSet<DigitalSignature> DigitalSignatures { get; set; }

    public virtual DbSet<DigitalVault> DigitalVaults { get; set; }

    public virtual DbSet<EncryptionKey> EncryptionKeys { get; set; }

    public virtual DbSet<ExecutorAssignment> ExecutorAssignments { get; set; }

    public virtual DbSet<HandoverCase> HandoverCases { get; set; }

    public virtual DbSet<HandoverDocument> HandoverDocuments { get; set; }

    public virtual DbSet<HandoverRequest> HandoverRequests { get; set; }

    public virtual DbSet<LegalVerification> LegalVerifications { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<PaymentTransaction> PaymentTransactions { get; set; }

    public virtual DbSet<ProofOfLife> ProofOfLives { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<TwoFactorAuth> TwoFactorAuths { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserSubscription> UserSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetDocument>(entity =>
        {
            entity.HasKey(e => e.DocumentId);

            entity.ToTable("ASSET_DOCUMENTS");

            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FileType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.IsEncrypted).HasDefaultValue(true);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Asset).WithMany(p => p.AssetDocuments)
                .HasForeignKey(d => d.AssetId)
                .HasConstraintName("FK_AssetDocs_Assets");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.AssetDocuments)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AssetDocs_Users");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.LogId);

            entity.ToTable("AUDIT_LOGS");

            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.EntityType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(45)
                .IsUnicode(false)
                .HasColumnName("IPAddress");
            entity.Property(e => e.Result)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Success");
            entity.Property(e => e.Timestamp).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_AuditLogs_Users");
        });

        modelBuilder.Entity<Authentication>(entity =>
        {
            entity.HasKey(e => e.AuthId);

            entity.ToTable("AUTHENTICATIONS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.Authentications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Auth_Users");
        });

        modelBuilder.Entity<BeneficiaryAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId);

            entity.ToTable("BENEFICIARY_ASSIGNMENTS");

            entity.Property(e => e.Allocation)
                .HasDefaultValue(100.00m)
                .HasColumnType("decimal(5, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Asset).WithMany(p => p.BeneficiaryAssignments)
                .HasForeignKey(d => d.AssetId)
                .HasConstraintName("FK_BenAssign_Assets");

            entity.HasOne(d => d.Beneficiary).WithMany(p => p.BeneficiaryAssignments)
                .HasForeignKey(d => d.BeneficiaryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BenAssign_Users");
        });

        modelBuilder.Entity<BeneficiaryReceipt>(entity =>
        {
            entity.HasKey(e => e.ReceiptId);

            entity.ToTable("BENEFICIARY_RECEIPTS");

            entity.Property(e => e.ConfirmedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Confirmed");

            entity.HasOne(d => d.Beneficiary).WithMany(p => p.BeneficiaryReceipts)
                .HasForeignKey(d => d.BeneficiaryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Receipts_Users");

            entity.HasOne(d => d.Handover).WithMany(p => p.BeneficiaryReceipts)
                .HasForeignKey(d => d.HandoverId)
                .HasConstraintName("FK_Receipts_HandoverCases");
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("CONVERSATIONS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Title).HasMaxLength(100);
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Direct");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");
        });

        modelBuilder.Entity<DigitalAsset>(entity =>
        {
            entity.HasKey(e => e.AssetId);

            entity.ToTable("DIGITAL_ASSETS");

            entity.Property(e => e.AssetName).HasMaxLength(100);
            entity.Property(e => e.AssetType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Vault).WithMany(p => p.DigitalAssets)
                .HasForeignKey(d => d.VaultId)
                .HasConstraintName("FK_Assets_Vaults");
        });

        modelBuilder.Entity<DigitalSignature>(entity =>
        {
            entity.HasKey(e => e.SignatureId);

            entity.ToTable("DIGITAL_SIGNATURES");

            entity.Property(e => e.SignedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Valid");

            entity.HasOne(d => d.Signer).WithMany(p => p.DigitalSignatures)
                .HasForeignKey(d => d.SignerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Signatures_Users");

            entity.HasOne(d => d.Verification).WithMany(p => p.DigitalSignatures)
                .HasForeignKey(d => d.VerificationId)
                .HasConstraintName("FK_Signatures_Verif");
        });

        modelBuilder.Entity<DigitalVault>(entity =>
        {
            entity.HasKey(e => e.VaultId);

            entity.ToTable("DIGITAL_VAULTS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.VaultName).HasMaxLength(100);

            entity.HasOne(d => d.Owner).WithMany(p => p.DigitalVaults)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaults_Users");
        });

        modelBuilder.Entity<EncryptionKey>(entity =>
        {
            entity.HasKey(e => e.KeyId);

            entity.ToTable("ENCRYPTION_KEYS");

            entity.Property(e => e.Algorithm)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("AES-256");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.KeyVersion).HasDefaultValue(1);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Asset).WithMany(p => p.EncryptionKeys)
                .HasForeignKey(d => d.AssetId)
                .HasConstraintName("FK_EncKeys_Assets");
        });

        modelBuilder.Entity<ExecutorAssignment>(entity =>
        {
            entity.ToTable("EXECUTOR_ASSIGNMENTS");

            entity.Property(e => e.AssignedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.Executor).WithMany(p => p.ExecutorAssignments)
                .HasForeignKey(d => d.ExecutorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ExecAssign_Users");

            entity.HasOne(d => d.Vault).WithMany(p => p.ExecutorAssignments)
                .HasForeignKey(d => d.VaultId)
                .HasConstraintName("FK_ExecAssign_Vaults");
        });

        modelBuilder.Entity<HandoverCase>(entity =>
        {
            entity.HasKey(e => e.HandoverId);

            entity.ToTable("HANDOVER_CASES");

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("In_Progress");

            entity.HasOne(d => d.Assignment).WithMany(p => p.HandoverCases)
                .HasForeignKey(d => d.AssignmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HandoverCases_Assignments");

            entity.HasOne(d => d.Request).WithMany(p => p.HandoverCases)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("FK_HandoverCases_Requests");
        });

        modelBuilder.Entity<HandoverDocument>(entity =>
        {
            entity.ToTable("HANDOVER_DOCUMENTS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FileType)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Request).WithMany(p => p.HandoverDocuments)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("FK_HandoverDocs_Requests");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.HandoverDocuments)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HandoverDocs_Users");
        });

        modelBuilder.Entity<HandoverRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);

            entity.ToTable("HANDOVER_REQUESTS");

            entity.Property(e => e.InitiatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.RequestType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Draft");

            entity.HasOne(d => d.Executor).WithMany(p => p.HandoverRequests)
                .HasForeignKey(d => d.ExecutorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HandoverReq_Users");

            entity.HasOne(d => d.Vault).WithMany(p => p.HandoverRequests)
                .HasForeignKey(d => d.VaultId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_HandoverReq_Vaults");
        });

        modelBuilder.Entity<LegalVerification>(entity =>
        {
            entity.HasKey(e => e.VerificationId);

            entity.ToTable("LEGAL_VERIFICATIONS");

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending");
            entity.Property(e => e.SubmittedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Request).WithMany(p => p.LegalVerifications)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("FK_LegalVerif_Requests");

            entity.HasOne(d => d.Verifier).WithMany(p => p.LegalVerifications)
                .HasForeignKey(d => d.VerifierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LegalVerif_Users");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("MESSAGES");

            entity.Property(e => e.MessageType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Text");
            entity.Property(e => e.SentAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Conversation).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ConversationId)
                .HasConstraintName("FK_Messages_Conversations");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Messages_Users");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("NOTIFICATIONS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Title).HasMaxLength(150);
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.HasOne(d => d.Handover).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.HandoverId)
                .HasConstraintName("FK_Notif_HandoverCases");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Notif_Users");
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);

            entity.ToTable("PAYMENT_TRANSACTIONS");

            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Pending");
            entity.Property(e => e.TransactionCode)
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.HasOne(d => d.Subscription).WithMany(p => p.PaymentTransactions)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("FK_PayTrans_UserSub");

            entity.HasOne(d => d.User).WithMany(p => p.PaymentTransactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PayTrans_Users");
        });

        modelBuilder.Entity<ProofOfLife>(entity =>
        {
            entity.HasKey(e => e.CheckId);

            entity.ToTable("PROOF_OF_LIFE");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.FrequencyDays).HasDefaultValue(30);
            entity.Property(e => e.GracePeriodDays).HasDefaultValue(14);
            entity.Property(e => e.LastPingAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.ProofOfLives)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ProofOfLife_Users");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("ROLES");

            entity.HasIndex(e => e.RoleName, "UQ_ROLES_RoleName").IsUnique();

            entity.Property(e => e.RoleName)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId);

            entity.ToTable("SUBSCRIPTION_PLANS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.DurationDays).HasDefaultValue(30);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MaxStorageBytes).HasDefaultValue(1073741824L);
            entity.Property(e => e.MaxVaults).HasDefaultValue(1);
            entity.Property(e => e.PlanName).HasMaxLength(100);
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<TwoFactorAuth>(entity =>
        {
            entity.HasKey(e => e.TwoFactorId);

            entity.ToTable("TWO_FACTOR_AUTHS");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Method)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Secret).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User).WithMany(p => p.TwoFactorAuths)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_2FA_Users");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("USERS");

            entity.HasIndex(e => e.Email, "UQ_USERS_Email").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetime())");

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "UserRole",
                    r => r.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("FK_UserRoles_Roles"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .HasConstraintName("FK_UserRoles_Users"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("USER_ROLES");
                    });
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId);

            entity.ToTable("USER_SUBSCRIPTIONS");

            entity.Property(e => e.StartDate).HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Plan).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserSub_Plans");

            entity.HasOne(d => d.User).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserSub_Users");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
>>>>>>> a4348ee039e341d95316f69f42cacc3f656357d7
