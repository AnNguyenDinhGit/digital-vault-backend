using System;
using System.Collections.Generic;
<<<<<<< HEAD
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.DAL.Entities
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool Is2FAEnabled { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
=======

namespace LegacyVault.DAL.Entities;

public partial class User
{
    public int UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Phone { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AssetDocument> AssetDocuments { get; set; } = new List<AssetDocument>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Authentication> Authentications { get; set; } = new List<Authentication>();

    public virtual ICollection<BeneficiaryAssignment> BeneficiaryAssignments { get; set; } = new List<BeneficiaryAssignment>();

    public virtual ICollection<BeneficiaryReceipt> BeneficiaryReceipts { get; set; } = new List<BeneficiaryReceipt>();

    public virtual ICollection<DigitalSignature> DigitalSignatures { get; set; } = new List<DigitalSignature>();

    public virtual ICollection<DigitalVault> DigitalVaults { get; set; } = new List<DigitalVault>();

    public virtual ICollection<ExecutorAssignment> ExecutorAssignments { get; set; } = new List<ExecutorAssignment>();

    public virtual ICollection<HandoverDocument> HandoverDocuments { get; set; } = new List<HandoverDocument>();

    public virtual ICollection<HandoverRequest> HandoverRequests { get; set; } = new List<HandoverRequest>();

    public virtual ICollection<LegalVerification> LegalVerifications { get; set; } = new List<LegalVerification>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();

    public virtual ICollection<ProofOfLife> ProofOfLives { get; set; } = new List<ProofOfLife>();

    public virtual ICollection<TwoFactorAuth> TwoFactorAuths { get; set; } = new List<TwoFactorAuth>();

    public virtual ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
>>>>>>> a4348ee039e341d95316f69f42cacc3f656357d7
}
