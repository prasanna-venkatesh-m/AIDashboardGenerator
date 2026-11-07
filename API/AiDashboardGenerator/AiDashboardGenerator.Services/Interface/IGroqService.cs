
using AiDashboardGenerator.Models;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IGroqService
    {
        Task<QueryPlanResponse> GenerateResponseAsync(string prompt);

        Task<DashboardConfiguration> GenerateDashboardAsync(
            QueryPlanResponse queryPlan,
            Dictionary<string, List<Dictionary<string, object?>>> data);
    }
}
