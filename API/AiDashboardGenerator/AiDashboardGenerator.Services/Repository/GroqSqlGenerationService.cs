using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AiDashboardGenerator.Services.Repository;

public sealed class GroqSqlGenerationService : ISqlGenerationService
{
    private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GroqSqlGenerationService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<SqlDatasetPlan> GenerateSqlAsync(
        string userQuestion,
        QueryPlanResponse queryPlan,
        FileMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Groq:ApiKey"];
        var model = _configuration["Groq:Model"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Groq:ApiKey is not configured.");

        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Groq:Model is not configured.");

        var metadataJson = JsonSerializer.Serialize(metadata);
        var queryPlanJson = JsonSerializer.Serialize(queryPlan);

        var systemPrompt = """
            You are an expert DuckDB SQL generator for an Excel analytics application.

            Generate read-only DuckDB SQL datasets from the supplied Excel metadata,
            query plan, and user question.

            AVAILABLE TABLES:
            The backend creates one DuckDB table for every worksheet. The table name
            is exactly the worksheet name. Use only those table names and columns
            present in the metadata. Identifiers containing spaces or special
            characters must be double-quoted.

            OUTPUT CONTRACT:
            Return only valid JSON in this exact shape:
            {
              "datasets": [
                { "key": "Sheet1", "sql": "SELECT ..." },
                { "key": "chart_1", "sql": "SELECT ..." }
              ]
            }

            DATASET RULES:
            1. Create exactly one base dataset whose key is the first sheet name
               from the query plan (or Result when no sheet is available).
            2. The base dataset must return one row containing every requested
               aggregation using the exact aggregation names as output aliases.
               Include calculated metrics using their exact names when possible.
            3. If the query plan has no aggregations, return the requested columns
               from the relevant worksheet in the base dataset.
            4. For every visualization whose type is not "kpi", create one dataset
               named chart_N, where N is its zero-based index in Visualizations.
               Keep the original visualization index; do not renumber chart keys.
            5. A chart dataset must return the visualization's XAxis column using
               that exact column name as its output alias, plus a numeric column
               named value. Group by the visualization's GroupBy columns (or XAxis
               when GroupBy is empty).
            6. For client-level counts use COUNT(DISTINCT ClientId). For account-level
               counts use COUNT(DISTINCT AccountId). Use COUNT(*) only when the
               query plan explicitly requests row counts. Use SUM/AVG/MIN/MAX for
               numeric measures when requested.
            7. Apply global filters to the base and chart datasets only when the
               query plan explicitly contains global filters. Apply aggregation
               conditions to their matching KPI aggregation only; do not let one
               metric's conditions affect another metric.
            8. For joins, use the join definitions from the query plan and join the
               specified worksheet tables on the specified columns and join type.
            9. For date trends, group by an appropriate date value or date_trunc
               period. Return the grouped date under the exact XAxis alias.
            10. Return only SQL SELECT statements (WITH ... SELECT is allowed).
                Do not return semicolons, multiple statements, DDL, DML, PRAGMA,
                ATTACH, COPY, file-reading functions, or external access functions.
            11. IMPORTANT: Every Excel worksheet is imported with all_varchar=true.
                Therefore, every source column is VARCHAR in DuckDB, even when the
                metadata identifies it as numeric, date, or boolean.
            12. For numeric calculations (SUM, AVG, MIN, MAX, arithmetic), explicitly
                convert the source column using:
                TRY_CAST(NULLIF(TRIM("ColumnName"), '') AS DOUBLE)
                Never apply SUM/AVG directly to a VARCHAR column.
            13. For numeric comparisons and numeric filters, use TRY_CAST on the
                source column before comparing it with a numeric literal.
            14. For identifier columns such as ClientId, AccountId, phone numbers,
                codes, and reference numbers, keep them as VARCHAR. Never cast
                identifiers to numeric values because leading zeroes and
                alphanumeric identifiers must be preserved.
            15. For date grouping and date filters, use TRY_CAST(column AS DATE)
                only when the stored date text is in a DuckDB-recognized format.
                If the metadata indicates another date format, use an appropriate
                TRY_STRPTIME format and cast the result to DATE or TIMESTAMP.
            16. For boolean/status columns, compare using the actual string values
                found in metadata. Do not assume that 0/1 or TRUE/FALSE have a
                particular business meaning.
            17. Never invent tables, columns, values, metrics, or business meanings.
                Respect metadata sample values and data types.
            18. Do not return markdown fences or explanations.
            """;

        var userPrompt = $"""
            USER QUESTION:
            {userQuestion}

            EXCEL METADATA:
            {metadataJson}

            QUERY PLAN:
            {queryPlanJson}
            """;

        var requestBody = new
        {
            model,
            temperature = 0,
            max_tokens = 4096,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Groq SQL generation failed ({(int)response.StatusCode}): {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Groq returned an empty SQL dataset plan.");

        var result = JsonSerializer.Deserialize<SqlDatasetPlan>(content, JsonOptions);

        if (result == null || result.Datasets.Count == 0)
            throw new InvalidOperationException("Groq did not return any SQL datasets.");

        return result;
    }
}
