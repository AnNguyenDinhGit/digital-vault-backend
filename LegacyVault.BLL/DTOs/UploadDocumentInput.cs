using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace LegacyVault.BLL.DTOs;

public class UploadDocumentInput
{
    [Required]
    public IFormFile File { get; set; } = null!;

    
}
