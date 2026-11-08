using AiDashboardGenerator.Models;

namespace AiDashboardGenerator.Services.Interface;

public interface IDuckDbQueryService
{
    Task<Dictionary<string, List<Dictionary<string, object?>>>> ExecuteAsync(
        Stream excelStream,
        FileMetadata metadata,
        SqlDatasetPlan sqlPlan,
        CancellationToken cancellationToken = default);
}
