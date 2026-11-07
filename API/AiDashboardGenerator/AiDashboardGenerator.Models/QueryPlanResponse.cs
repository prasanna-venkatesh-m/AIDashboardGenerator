
namespace AiDashboardGenerator.Models
{
    public class QueryPlanResponse
    {
        public string Intent { get; set; } = "";

        public List<string> Sheets { get; set; } = [];

        public List<ColumnSelection> Columns { get; set; } = [];

        public List<FilterCondition> Filters { get; set; } = [];

        public string FilterLogic { get; set; } = "AND";

        public List<string> GroupBy { get; set; } = [];

        public List<Aggregation> Aggregations { get; set; } = [];

        public List<JoinCondition> Joins { get; set; } = [];

        public List<CalculatedMetric> CalculatedMetrics { get; set; } = [];

        public List<Visualization> Visualizations { get; set; } = [];

        public bool RequiresClarification { get; set; }

        public string ClarificationQuestion { get; set; } = "";
    }

    public class ColumnSelection
    {
        public string SheetName { get; set; } = "";

        public string ColumnName { get; set; } = "";

        public string Role { get; set; } = "";
    }

    public class FilterCondition
    {
        public string SheetName { get; set; } = "";

        public string ColumnName { get; set; } = "";

        public string Operator { get; set; } = "";

        public string Value { get; set; } = "";

        public string SecondValue { get; set; } = "";
    }

    public class Aggregation
    {
        public string Name { get; set; } = "";

        public string SheetName { get; set; } = "";

        public string ColumnName { get; set; } = "";

        public string Function { get; set; } = "";

        public List<FilterCondition> Conditions { get; set; } = [];
    }

    public class JoinCondition
    {
        public string LeftSheet { get; set; } = "";

        public string LeftColumn { get; set; } = "";

        public string RightSheet { get; set; } = "";

        public string RightColumn { get; set; } = "";

        public string JoinType { get; set; } = "inner";
    }

    public class CalculatedMetric
    {
        public string Name { get; set; } = "";

        public string Formula { get; set; } = "";

        public string DataType { get; set; } = "number";

        public string Format { get; set; } = "";
    }

    public class Visualization
    {
        public string Type { get; set; } = "";

        public string Title { get; set; } = "";

        public string XAxis { get; set; } = "";

        public string YAxis { get; set; } = "";

        public List<string> Series { get; set; } = [];

        public List<string> GroupBy { get; set; } = [];
    }
}
