using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.BLL.Interfaces
{
    public interface IAccountEncryptionService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}
