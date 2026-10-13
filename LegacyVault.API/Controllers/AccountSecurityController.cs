using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LegacyVault.BLL.Interfaces;

namespace LegacyVault.API.Controllers
{
    [ApiController]
    [Route("api/account-security")]
    //[Authorize]
    public class AccountSecurityController : ControllerBase
    {
        private readonly IAccountEncryptionService _encryptionService;

        public AccountSecurityController(IAccountEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        [HttpPost("encrypt-field")]
        public IActionResult EncryptSensitiveData([FromBody] EncryptRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.PlainText))
                return BadRequest("Dữ liệu đầu vào không được để trống.");

            var cipherText = _encryptionService.Encrypt(request.PlainText);
            return Ok(new { CipherText = cipherText });
        }

        [HttpPost("decrypt-field")]
        public IActionResult DecryptSensitiveData([FromBody] DecryptRequestDto request)
        {
            try
            {
                var plainText = _encryptionService.Decrypt(request.CipherText);
                return Ok(new { PlainText = plainText });
            }
            catch
            {
                return BadRequest("Giải mã thất bại. Dữ liệu bị thay đổi hoặc sai khóa.");
            }
        }
    }

    public record EncryptRequestDto(string PlainText);
    public record DecryptRequestDto(string CipherText);
}