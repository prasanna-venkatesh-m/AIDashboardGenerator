using AiDashboardGenerator.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IExcelQueryService
    {
        Task<Dictionary<string, List<Dictionary<string, object?>>>> ExecuteAsync(
            Stream stream,
            QueryPlanResponse plan,
            FileMetadata metadata);
    }
}
