
using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AiDashboardGenerator.Services.Repository;

public class GroqService : IGroqService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private const string ApiUrl =
        "https://api.groq.com/openai/v1/chat/completions";

    public GroqService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    // FIRST LLM CALL: Generate Query Plan
    public async Task<QueryPlanResponse> GenerateResponseAsync(string prompt)
    {
        const string systemPrompt = """
            You are an Excel data analysis assistant.

            Analyze the Excel metadata and user question.
            Only use existing sheets and columns.
            Do not generate SQL or executable code.
            Generate a structured query plan matching the provided JSON schema.

            GLOBAL FILTER RULES:
            - Global filters apply to the entire dataset.
            - Do not put aggregation-specific conditions in global filters.
            - Keep global filters empty for overall dashboard requests
              unless the user explicitly requests filtering.
            - Never generate contradictory global filters.

            AGGREGATION RULES:
            - Calculate every aggregation independently.
            - Apply conditions only to their respective aggregations.
            - One aggregation's conditions must not affect another.
            - Use actual metadata values when generating conditions.
            - Never assume the meaning of 0 or 1 without metadata evidence.

            QUERY PLANNING RULES:

            1. GENERAL:
            - Use only existing sheets and columns from metadata.
            - Never invent column names or sheet names.
            - Use the correct aggregation functions.
            - Generate valid formulas using supported aggregation names.
            - Return clarification if requested information cannot be
              determined from metadata.

            2. GLOBAL FILTERS:
            - Use global filters only when the user explicitly requests
              filtering of the entire dataset.
            - For overall business performance dashboards,
              keep global filters empty.
            - Never place contradictory filters using AND logic.

            3. AGGREGATIONS:
            - Calculate every aggregation independently.
            - Put metric-specific conditions inside that aggregation.
            - Never modify the original dataset for aggregation conditions.
            - Use COUNT_DISTINCT for unique entity counts.
            - For client-level totals, visited clients, not-visited clients,
              paid clients and unpaid clients, use COUNT_DISTINCT on ClientId
              with the appropriate aggregation-specific condition.
            - For account-level totals, visited accounts, not-visited accounts,
              paid accounts and unpaid accounts, use COUNT_DISTINCT on AccountId
              with the appropriate aggregation-specific condition.
            - Use COUNT_ROWS only when the user explicitly asks for record/row counts.
            - Use COUNT for non-empty column values.

            4. METADATA:
            - Check DataType, SampleValues and ValueCounts.
            - Use actual metadata values for filter conditions.
            - Never assume the meaning of 0, 1, Yes, No, Paid or Pending.
            - Request clarification if a required value cannot be identified.

            5. CALCULATED METRICS:
            - Reference aggregation names exactly.
            - Never reference an aggregation that does not exist.
            - Use only +, -, *, / and parentheses.
            - Do not generate unsupported formula functions.

            6. GROUPING:
            - Keep the top-level GroupBy empty for an overall KPI summary.
            - For each chart visualization, populate that visualization's GroupBy
              with its actual XAxis column when category-wise data is required.
            - Use LastPaidDate or another real date column for time-series charts.
            - Do not put GROUP_BY or any grouping instruction inside CalculatedMetric.Formula.
            - CalculatedMetric.Formula must contain only arithmetic expressions
              using existing aggregation names and +, -, *, /, and parentheses.
            - Do not create a chart that requires grouped data without specifying
              the visualization GroupBy and XAxis.

            7. OUTPUT:
            - Return only JSON matching the provided response schema.
            - Do not include explanations outside the JSON.
            """;

        return await GenerateStructuredResponseAsync<QueryPlanResponse>(
            "query_plan_response",
            systemPrompt,
            prompt);
    }

    // SECOND LLM CALL: Generate Dashboard Configuration
    public async Task<DashboardConfiguration> GenerateDashboardAsync(
        QueryPlanResponse queryPlan,
        Dictionary<string, List<Dictionary<string, object?>>> data)
    {
        var options = CreateSerializerOptions();

        var queryPlanJson = JsonSerializer.Serialize(
            queryPlan,
            options);

        // Send only a preview to Groq.
        // The complete data remains available in the backend
        // for dashboard rendering.
        var dataJson = CreateDashboardPreviewJson(data, options);

        var prompt = $"""
            Generate a professional dashboard configuration using
            the supplied query plan and calculated Excel data preview.

            QUERY PLAN:
            {queryPlanJson}

            CALCULATED DATA PREVIEW:
            {dataJson}

            IMPORTANT:
            The supplied data is a limited preview of the actual calculated data.
            Use only the available sheet names, column names and data keys.
            Do not invent metrics, values or data keys.

            DASHBOARD GENERATION RULES:

            1. GENERAL:
            - Understand the dashboard purpose from the query plan.
            - Generate a meaningful title and description.
            - Use only metrics available in calculated data.
            - Never invent metrics, values, or data keys.
            - Never modify calculated values.
            - Generate a professional business dashboard.

            2. COMPONENTS:
            - Use metric_card for important KPI values.
            - Use chart for meaningful visualizations.
            - Use table for detailed information.
            - Use text for useful explanations.
            - Use divider to separate sections.
            - Generate approximately 4 to 6 KPI cards when data supports them.
            - Generate one chart component for EVERY non-KPI visualization
              in the query plan when its corresponding chart_N dataset exists.
            - Do not limit the dashboard to 1 or 2 charts.
            - Do not omit a chart that exists in the query plan and has data.
            - Avoid duplicate metrics and unnecessary components.
            - Give every component a unique ID.
            - Assign sequential display order.
            - Use appropriate columnSpan and rowSpan.

            3. DATA REFERENCES:
            - Every metric_card DataKey must exactly match
              a key in the calculated data.
            - Every chart series DataKey must reference
              an available data key.
            - Never invent data keys.
            - Never invent chart categories or values.
            - The backend provides visualization-specific grouped datasets
              named chart_0, chart_1, chart_2, and so on, matching the
              order of queryPlan.Visualizations.
            - For a non-KPI visualization at index N, set the chart component
              data_key to chart_N when that dataset exists.
            - Grouped chart datasets contain the visualization XAxis field
              and a numeric value field named value.
            - For these datasets, set series to one series with data_key=value.
            - Set x_axis to the actual grouping column from the query plan.
            - Do not use aggregate KPI keys as chart series when chart_N exists.
            - If the data does not support a meaningful chart,
              use metric cards or a table instead.

            4. CHARTS:
            - Create a chart component for each non-KPI visualization in
              the query plan when its chart_N dataset contains rows.
            - Use bar charts for category comparisons.
            - Use line charts for time-based trends.
            - Use pie charts for meaningful proportions.
            - Use area charts for trends where appropriate.
            - Set appropriate axis labels.
            - Do not generate charts without suitable data.
            - Use only chart types supported by the dashboard renderer:
              pie, donut, bar, horizontal_bar, line, and area.

            5. LAYOUT:
            - Use a responsive grid layout.
            - Use a light professional theme.
            - Use 4 columns for desktop.
            - Place KPI cards at the top.
            - Place charts and tables below KPI cards.
            - Use appropriate component spans.

            6. FORMATTING:
            - Use number for regular numeric values.
            - Use percentage for percentage metrics.
            - Use currency only for actual monetary values.
            - Use meaningful titles and subtitles.

            7. REQUIRED COMPONENT PROPERTIES:
            Every component MUST contain ALL properties defined
            in the DashboardComponent schema.

            Always include:
            - id
            - type
            - title
            - subtitle
            - order
            - column_span
            - row_span
            - data_key
            - chart_type
            - x_axis
            - y_axis
            - description
            - format
            - color
            - series
            - table_columns

            For properties not applicable to a component:
            - Use an empty string for string properties.
            - Use an empty array for array properties.

            COMPONENT-SPECIFIC RULES:

            Metric card:
            - Set data_key to the actual metric key.
            - Set chart_type to an empty string.
            - Set series to [].
            - Set table_columns to [].

            Chart:
            - Set data_key to an empty string unless required.
            - Set chart_type to the selected chart type.
            - Set series to the appropriate data series.
            - Set table_columns to [].

            Table:
            - Set series to [].
            - Populate table_columns using actual data keys.

            Text and divider:
            - Use empty data_key, chart_type, x_axis and y_axis.
            - Set series and table_columns to [].

            8. OUTPUT:
            - Return only JSON matching the DashboardConfiguration schema.
            - Every required property must be present.
            - Do not return HTML, CSS or JavaScript.
            - Do not include explanations outside the JSON.
            """;

        var dashboard = await GenerateStructuredResponseAsync<DashboardConfiguration>(
            "dashboard_configuration",
            """
            You are a professional dashboard designer.

            Convert calculated Excel data into a structured dashboard
            configuration.

            Follow the supplied instructions and strictly match
            the JSON schema.

            Include every required property.
            Never invent data or generate executable HTML, CSS or JavaScript.
            Keep the output concise.
            """,
            prompt);

        EnsureVisualizationComponents(dashboard, queryPlan, data);

        return dashboard;
    }

    // Ensure that every query-plan visualization with a generated grouped
    // dataset is represented in the final dashboard configuration.
    private static void EnsureVisualizationComponents(
        DashboardConfiguration dashboard,
        QueryPlanResponse queryPlan,
        Dictionary<string, List<Dictionary<string, object?>>> data)
    {
        dashboard.Components ??= [];

        for (var index = 0; index < queryPlan.Visualizations.Count; index++)
        {
            var visualization = queryPlan.Visualizations[index];
            var type = visualization.Type?.Trim().ToLowerInvariant() ?? "";

            if (type is "kpi" or "metric" or "metric_card" or "kpi_card")
                continue;

            var dataKey = $"chart_{index}";

            if (!data.TryGetValue(dataKey, out var chartRows) ||
                chartRows.Count == 0)
            {
                continue;
            }

            var chartType = type switch
            {
                "horizontal bar" or "horizontal-bar" => "horizontal_bar",
                "doughnut" => "donut",
                _ => type
            };

            if (chartType is not ("pie" or "donut" or "bar" or
                "horizontal_bar" or "line" or "area"))
            {
                continue;
            }

            var component = dashboard.Components.FirstOrDefault(existing =>
                existing.Type.Equals("chart", StringComparison.OrdinalIgnoreCase) &&
                existing.Title.Equals(
                    visualization.Title,
                    StringComparison.OrdinalIgnoreCase));

            if (component == null)
            {
                component = new DashboardComponent
                {
                    Id = $"chart_{index}_{Guid.NewGuid():N}"[..18],
                    Type = "chart",
                    Title = visualization.Title,
                    Order = dashboard.Components.Count + 1,
                    ColumnSpan = 2,
                    RowSpan = 2
                };

                dashboard.Components.Add(component);
            }

            component.Type = "chart";
            component.Title = visualization.Title;
            component.DataKey = dataKey;
            component.ChartType = chartType;
            component.XAxis = visualization.XAxis;
            component.YAxis = "value";
            component.Description = string.IsNullOrWhiteSpace(component.Description)
                ? visualization.Title
                : component.Description;
            component.Series =
            [
                new DashboardChartSeries
                {
                    Name = "Count",
                    DataKey = "value",
                    Color = string.IsNullOrWhiteSpace(component.Color)
                        ? "#635BDB"
                        : component.Color
                }
            ];
            component.TableColumns = [];
        }

        // Keep component order stable after adding any missing charts.
        dashboard.Components = dashboard.Components
            .OrderBy(component => component.Order)
            .ToList();
    }

    // CREATE A LIMITED DATA PREVIEW FOR DASHBOARD GENERATION
    private static string CreateDashboardPreviewJson(
        Dictionary<string, List<Dictionary<string, object?>>> data,
        JsonSerializerOptions options)
    {
        const int maxRowsPerSheet = 12;
        const int maxColumnsPerRow = 30;
        const int maxStringLength = 120;

        var preview = new Dictionary<string, object?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in data)
        {
            var previewRows = sheet.Value
                .Take(maxRowsPerSheet)
                .Select(row => row
                    .Take(maxColumnsPerRow)
                    .ToDictionary(
                        item => item.Key,
                        item => LimitValueLength(
                            item.Value,
                            maxStringLength),
                        StringComparer.OrdinalIgnoreCase))
                .ToList();

            preview[sheet.Key] = new
            {
                row_count = sheet.Value.Count,
                preview_rows = previewRows
            };
        }

        return JsonSerializer.Serialize(preview, options);
    }

    // LIMIT LONG STRING VALUES IN THE PREVIEW
    private static object? LimitValueLength(
        object? value,
        int maxLength)
    {
        if (value is string text && text.Length > maxLength)
        {
            return text[..maxLength];
        }

        return value;
    }

    // COMMON STRUCTURED RESPONSE HANDLER
    private async Task<T> GenerateStructuredResponseAsync<T>(
        string schemaName,
        string systemPrompt,
        string userPrompt)
    {
        var options = CreateSerializerOptions();

        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(
            options,
            typeof(T),
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true
            });

        NormalizeSchema(schema);

        var maxCompletionTokens =
            schemaName == "dashboard_configuration"
                ? 4096
                : 4096;

        var requestBody = new
        {
            model = _configuration["Groq:Model"],

            max_completion_tokens = maxCompletionTokens,

            reasoning_effort = "low",

            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userPrompt
                }
            },

            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = schemaName,
                    strict = true,
                    schema
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(
            requestBody,
            options);

        Console.WriteLine($"Groq Schema: {schemaName}");
        Console.WriteLine($"Groq System Prompt Length: {systemPrompt.Length}");
        Console.WriteLine($"Groq User Prompt Length: {userPrompt.Length}");
        Console.WriteLine($"Groq Request Size: {Encoding.UTF8.GetByteCount(requestJson)} bytes");
        Console.WriteLine($"Groq Completion Token Limit: {maxCompletionTokens}");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ApiUrl);

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _configuration["Groq:ApiKey"]);

        request.Content = new StringContent(
            requestJson,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request);

        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Groq API Error ({(int)response.StatusCode}): {responseContent}");
        }

        using var document = JsonDocument.Parse(responseContent);

        var choice = document.RootElement
            .GetProperty("choices")[0];

        var finishReason = choice
            .GetProperty("finish_reason")
            .GetString();

        if (finishReason == "length")
        {
            throw new Exception(
                $"Groq response was truncated for {schemaName}. " +
                $"Maximum completion tokens: {maxCompletionTokens}. " +
                "Reduce the output size or increase the token limit.");
        }

        var content = choice
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new Exception(
                $"Groq returned an empty response for {schemaName}.");
        }

        return JsonSerializer.Deserialize<T>(
            content,
            options)
            ?? throw new Exception(
                $"Invalid {schemaName} response.");
    }

    // JSON SERIALIZATION OPTIONS
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    // NORMALIZE SCHEMA FOR GROQ STRICT JSON OUTPUT
    private static void NormalizeSchema(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["properties"] is JsonObject properties)
            {
                obj["required"] = new JsonArray(
                    properties.Select(property =>
                        (JsonNode?)JsonValue.Create(property.Key)).ToArray());

                obj["additionalProperties"] = false;
            }

            foreach (var child in obj.ToList())
            {
                NormalizeSchema(child.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                NormalizeSchema(child);
            }
        }
    }
}
