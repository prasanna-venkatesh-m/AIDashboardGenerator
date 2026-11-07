using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using Hangfire;
using Microsoft.AspNetCore.Http;

namespace AiDashboardGenerator.Services.Repository
{
    public class FileService : IFileService
    {
        private readonly IBlobService _blobService;
        private readonly IExcelMetadataService _metadataService;

        public FileService(
            IBlobService blobService,
            IExcelMetadataService metadataService)
        {
            _blobService = blobService;
            _metadataService = metadataService;
        }

        public async Task<string> UploadFileAsync(
            string userId,
            IFormFile file,
            CancellationToken cancellationToken = default)
        {
            await using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);

            var fileBytes = memoryStream.ToArray();

            var uploadTask = _blobService.UploadFileAsync(
                userId,
                file,
                cancellationToken);

            await using var metadataStream = new MemoryStream(fileBytes);

            var metadataTask = _metadataService.ExtractAsync(metadataStream);

            await Task.WhenAll(uploadTask, metadataTask);

            var chatId = Guid.NewGuid().ToString();
            var fileId = Guid.NewGuid().ToString();

            var metadata = new FileMetadata
            {
                ChatId = chatId,
                FileId = fileId,
                UserId = userId,
                FileName = file.FileName,
                BlobName = uploadTask.Result,
                FileSize = file.Length,
                UploadedAt = DateTime.UtcNow,
                Sheets = metadataTask.Result
            };

            await _blobService.SaveMetadataAsync(metadata);

            return chatId;
        }
    }
}