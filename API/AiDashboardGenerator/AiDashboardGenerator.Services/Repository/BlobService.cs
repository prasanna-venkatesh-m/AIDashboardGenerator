using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AiDashboardGenerator.Services.Repository
{
    public class BlobService : IBlobService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IConfiguration _configuration;

        public BlobService(IConfiguration configuration)
        {
            _configuration = configuration;

            _blobServiceClient = new BlobServiceClient(
                configuration.GetConnectionString("AzureBlobStorage"));
        }

        public async Task<string> UploadFileAsync(
            string userId,
            IFormFile file,
            CancellationToken cancellationToken = default)
        {
            var containerName = _configuration["AzureBlobStorage:ContainerName"];
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            await containerClient.CreateIfNotExistsAsync(
                cancellationToken: cancellationToken);

            var extension = Path.GetExtension(file.FileName);
            var blobName = $"{userId}/{Guid.NewGuid()}{extension}";

            var blobClient = containerClient.GetBlobClient(blobName);

            await using var stream = file.OpenReadStream();

            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = file.ContentType
                    }
                },
                cancellationToken);

            return blobName;
        }

        public async Task SaveMetadataAsync(FileMetadata metadata)
        {
            var container = _blobServiceClient.GetBlobContainerClient("file-metadata");

            await container.CreateIfNotExistsAsync();

            var blob = container.GetBlobClient(
                $"{metadata.UserId}/{metadata.ChatId}/{metadata.FileId}.json");

            var json = System.Text.Json.JsonSerializer.Serialize(metadata);

            await blob.UploadAsync(BinaryData.FromString(json), overwrite: true);
        }

        public async Task<FileMetadata?> GetMetadataAsync(string userId, string chatId)
        {
            var container = _blobServiceClient.GetBlobContainerClient("file-metadata");

            var blobs = container.GetBlobsAsync(
                BlobTraits.None,
                BlobStates.None,
                $"{userId}/{chatId}/",
                CancellationToken.None);

            await foreach (var item in blobs)
            {
                var blob = container.GetBlobClient(item.Name);
                var response = await blob.DownloadContentAsync();

                return response.Value.Content.ToObjectFromJson<FileMetadata>();
            }

            return null;
        }

        public async Task<Stream> DownloadFileAsync(string blobName, CancellationToken cancellationToken = default)
        {
            var container = _blobServiceClient.GetBlobContainerClient("uploaded-files");
            var blob = container.GetBlobClient(blobName);

            var stream = new MemoryStream();

            await blob.DownloadToAsync(stream, cancellationToken);

            stream.Position = 0;

            return stream;
        }
    }
}