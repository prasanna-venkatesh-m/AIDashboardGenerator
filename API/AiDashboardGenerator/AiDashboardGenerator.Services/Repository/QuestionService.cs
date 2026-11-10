using AiDashboardGenerator.DTO;
using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using System.Text.Json;

namespace AiDashboardGenerator.Services.Repository;

public class QuestionService : IQuestionService
{
    private readonly IBlobService _blobService;
    private readonly ILmStudioService _lmStudioService;
    private readonly ISqlGenerationService _sqlGenerationService;
    private readonly IDuckDbQueryService _duckDbQueryService;

    public QuestionService(
        IBlobService blobService,
        ILmStudioService lmStudioService,
        ISqlGenerationService sqlGenerationService,
        IDuckDbQueryService duckDbQueryService)
    {
        _blobService = blobService;
        _lmStudioService = lmStudioService;
        _sqlGenerationService = sqlGenerationService;
        _duckDbQueryService = duckDbQueryService;
    }

    public async Task<QuestionResult> PreparePromptAsync(QuestionRequest request)
    {
        var metadata = await _blobService.GetMetadataAsync(
            request.UserId,
            request.ChatId);

        if (metadata == null)
        {
            throw new Exception("Chat or file metadata not found.");
        }

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

        // FIRST LOCAL LLM CALL: Generate the structured query plan.
        var queryPlan = await _lmStudioService.GenerateResponseAsync(prompt);

        if (string.Equals(
            queryPlan.Intent,
            "unsupported",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("The requested query is not supported.");
        }

        if (queryPlan.RequiresClarification)
        {
            return new QuestionResult
            {
                QueryPlan = queryPlan
            };
        }

        // SECOND LOCAL LLM CALL: Generate DuckDB SQL datasets.
        // The first argument is metadataJson, matching ISqlGenerationService.
        var sqlPlan = await _sqlGenerationService.GenerateSqlAsync(
            metadataJson,
            queryPlan,
            metadata,
            CancellationToken.None);

        await using var stream = await _blobService.DownloadFileAsync(
            metadata.BlobName);

        var data = await _duckDbQueryService.ExecuteAsync(
            stream,
            metadata,
            sqlPlan);

        // THIRD LOCAL LLM CALL: Generate dashboard configuration.
        var dashboard = await _lmStudioService.GenerateDashboardAsync(
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
