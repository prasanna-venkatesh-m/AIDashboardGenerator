using AiDashboardGenerator.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IBlobService
    {
        Task<string> UploadFileAsync(
        string userId,
        IFormFile file,
        CancellationToken cancellationToken = default);

        Task SaveMetadataAsync(FileMetadata metadata);
        Task<FileMetadata?> GetMetadataAsync(string userId, string chatId);
        Task<Stream> DownloadFileAsync(string blobName, CancellationToken cancellationToken = default);
    }
}
