using AiDashboardGenerator.Models;

namespace AiDashboardGenerator.Services.Interface;

public interface ISqlGenerationService
{
    Task<SqlDatasetPlan> GenerateSqlAsync(
        string userQuestion,
        QueryPlanResponse queryPlan,
        FileMetadata metadata,
        CancellationToken cancellationToken = default);
}
