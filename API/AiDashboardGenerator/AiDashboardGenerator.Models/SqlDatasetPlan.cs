namespace AiDashboardGenerator.Models;

public sealed class SqlDatasetPlan
{
    public List<SqlDatasetQuery> Datasets { get; set; } = [];
}

public sealed class SqlDatasetQuery
{
    public string Key { get; set; } = "";
    public string Sql { get; set; } = "";
}
