using System;
using System.Collections.Generic;
<<<<<<< HEAD
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.DAL.Entities
{
    public class BeneficiaryReceipt
    {
        public int ReceiptId { get; set; }
        public int RequestId { get; set; }
        public int BeneficiaryId { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Confirmed, Completed
        public DateTime? ReceivedAt { get; set; }
        public string? DigitalSignatureHash { get; set; }
        public string? Note { get; set; }

        public User? Beneficiary { get; set; }
    }
=======

namespace LegacyVault.DAL.Entities;

public partial class BeneficiaryReceipt
{
    public int ReceiptId { get; set; }

    public int HandoverId { get; set; }

    public int BeneficiaryId { get; set; }

    public string Status { get; set; } = null!;

    public string? ConfirmationNote { get; set; }

    public DateTime ConfirmedAt { get; set; }

    public virtual User Beneficiary { get; set; } = null!;

    public virtual HandoverCase Handover { get; set; } = null!;
>>>>>>> a4348ee039e341d95316f69f42cacc3f656357d7
}
