using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Claims;
using LegacyVault.DAL.Entities;

namespace LegacyVault.BLL.Interfaces
{
    public interface IJwtTokenService
    {
        string GenerateAccessToken(int userId, string email, string role);
        RefreshToken GenerateRefreshToken(int userId);
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    }
}
