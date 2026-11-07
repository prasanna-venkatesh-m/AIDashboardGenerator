using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.DTO
{
    public class FileUploadRequest
    {
        public string UserId { get; set; } = string.Empty;

        public IFormFile? File { get; set; }
    }
}
