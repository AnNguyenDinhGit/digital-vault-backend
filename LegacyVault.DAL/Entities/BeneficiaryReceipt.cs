using System;
using System.Collections.Generic;
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
}
