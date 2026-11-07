
using AiDashboardGenerator.DTO;
using AiDashboardGenerator.Services.Interface;
using Microsoft.AspNetCore.Mvc;

namespace AIDashboardGenerator.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController(IFileService fileService) : ControllerBase
{
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest("User ID is required.");

        if (request.File == null || request.File.Length == 0)
            return BadRequest("Please select a file.");

        var chatId = await fileService.UploadFileAsync(
            request.UserId,
            request.File,
            cancellationToken);

        return Ok(new
        {
            Message = "File uploaded successfully.",
            UserId = request.UserId,
            ChatId = chatId,
            FileName = request.File.FileName,
            FileSize = request.File.Length
        });
    }
}
