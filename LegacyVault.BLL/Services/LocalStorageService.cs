using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.BLL.Services;

public interface IStorageService
{
    Task<string> UploadFileAsync(byte[] content, string fileName, string folder, CancellationToken ct);
}

public class LocalStorageService : IStorageService
{
    public async Task<string> UploadFileAsync(byte[] content, string fileName, string folder, CancellationToken ct)
    {
        var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
        var filePath = Path.Combine(folderPath, uniqueFileName);

        await File.WriteAllBytesAsync(filePath, content, ct);
        return Path.Combine("uploads", folder, uniqueFileName);
    }
}
