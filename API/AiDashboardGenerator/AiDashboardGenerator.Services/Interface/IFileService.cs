using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IFileService
    {
        Task<string> UploadFileAsync(string userId, IFormFile file,CancellationToken cancellationToken = default);
    }
}
