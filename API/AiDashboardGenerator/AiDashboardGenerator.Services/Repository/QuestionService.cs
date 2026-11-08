using AiDashboardGenerator.DTO;
using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using System.Text.Json;

namespace AiDashboardGenerator.Services.Repository;

public class QuestionService : IQuestionService
{
    private readonly IBlobService _blobService;
    private readonly IGroqService _groqService;
    private readonly ISqlGenerationService _sqlGenerationService;
    private readonly IDuckDbQueryService _duckDbQueryService;

    public QuestionService(
        IBlobService blobService,
        IGroqService groqService,
        ISqlGenerationService sqlGenerationService,
        IDuckDbQueryService duckDbQueryService)
    {
        _blobService = blobService;
        _groqService = groqService;
        _sqlGenerationService = sqlGenerationService;
        _duckDbQueryService = duckDbQueryService;
    }

    public async Task<QuestionResult> PreparePromptAsync(QuestionRequest request)
    {
        var metadata = await _blobService.GetMetadataAsync(
            request.UserId,
            request.ChatId);

        if (metadata == null)
            throw new Exception("Chat or file metadata not found.");

        var metadataJson = JsonSerializer.Serialize(metadata);

        var prompt = $"""
            You are an expert Excel data analyst and query planner.

            Convert the user's question into a valid structured query plan
            using only the provided Excel metadata. Do not generate SQL.

            EXCEL FILE METADATA:
            {metadataJson}

            USER QUESTION:
            {request.Query}
            """;

        var queryPlan = await _groqService.GenerateResponseAsync(prompt);

        if (queryPlan.Intent == "unsupported")
            throw new Exception("The requested query is not supported.");

        if (queryPlan.RequiresClarification)
        {
            return new QuestionResult
            {
                QueryPlan = queryPlan
            };
        }

        var sqlPlan = await _sqlGenerationService.GenerateSqlAsync(
            request.Query,
            queryPlan,
            metadata);

        await using var stream = await _blobService.DownloadFileAsync(
            metadata.BlobName);

        var data = await _duckDbQueryService.ExecuteAsync(
            stream,
            metadata,
            sqlPlan);

        var dashboard = await _groqService.GenerateDashboardAsync(
            queryPlan,
            data);

        return new QuestionResult
        {
            QueryPlan = queryPlan,
            Data = data,
            Dashboard = dashboard
        };
    }
}