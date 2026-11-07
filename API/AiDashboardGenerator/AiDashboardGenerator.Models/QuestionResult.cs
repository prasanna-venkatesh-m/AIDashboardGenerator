using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Models
{
    public class QuestionResult
    {
        public QueryPlanResponse QueryPlan { get; set; } = new();
        public Dictionary<string, List<Dictionary<string, object?>>> Data { get; set; } = [];
        public DashboardConfiguration? Dashboard { get; set; }
    }
}
