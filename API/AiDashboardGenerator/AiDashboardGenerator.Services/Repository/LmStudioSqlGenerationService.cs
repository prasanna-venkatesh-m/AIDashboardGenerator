
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using Microsoft.Extensions.Configuration;

namespace AiDashboardGenerator.Services.Repository
{
    public class LmStudioSqlGenerationService : ISqlGenerationService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public LmStudioSqlGenerationService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;

            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        public async Task<SqlDatasetPlan> GenerateSqlAsync(
            string metadataJson,
            QueryPlanResponse queryPlan,
            FileMetadata fileMetadata,
            CancellationToken cancellationToken)
        {
            var baseUrl = _configuration["LMStudio:BaseUrl"]
                ?? "http://localhost:1235/v1";

            var model = _configuration["LMStudio:Model"]
                ?? "qwen2.5-coder-7b-instruct";

            var endpoint =
                $"{baseUrl.TrimEnd('/')}/chat/completions";

            var systemPrompt = """
                You are an expert Excel data analyst and DuckDB SQL developer.

                Your task is to generate DuckDB SQL queries using:
                1. Excel metadata.
                2. The existing analysis query plan.
                3. Uploaded file information.

                IMPORTANT RULES:

                1. Use only sheets and columns available in metadata.
                2. Excel data is imported using:
                   read_xlsx(..., header=true, all_varchar=true).

                3. All source columns are VARCHAR.

                4. For numeric calculations use:
                   TRY_CAST(NULLIF(TRIM("ColumnName"), '') AS DOUBLE)

                5. Never convert identifier columns into numbers.

                6. Use safe date conversions when required.

                7. Generate valid DuckDB SQL syntax.

                8. Generate SELECT queries only.

                9. Never generate INSERT, UPDATE, DELETE, DROP,
                   CREATE, ALTER, COPY, ATTACH or DETACH statements.

                10. Never access external files, URLs or databases.

                11. Use exact table and column names from metadata.

                12. Use meaningful dataset keys.

                13. Generate separate datasets for different visualizations
                    when required by the query plan.

                14. Follow the visualization requirements in the query plan.

                15. Do not invent columns, tables or relationships.

                16. If the question cannot be answered using available
                    metadata, return an empty datasets array.

                Return only JSON matching this structure:

                {
                    "datasets": [
                        {
                            "key": "dataset_key",
                            "sql": "SELECT ..."
                        }
                    ]
                }
                """;

            var serializedQueryPlan =
                JsonSerializer.Serialize(queryPlan);

            var serializedFileMetadata =
                JsonSerializer.Serialize(fileMetadata);

            var userPrompt = $"""
                EXCEL METADATA:

                {metadataJson}

                EXISTING QUERY PLAN:

                {serializedQueryPlan}

                UPLOADED FILE INFORMATION:

                {serializedFileMetadata}

                TASK:

                Generate the required DuckDB SQL queries.

                Follow the existing query plan and visualization
                requirements.

                Use only the available sheets and columns.

                Return the response using the required JSON schema.
                """;

            var requestBody = new
            {
                model,
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
                temperature = 0,
                max_tokens = 4096,
                stream = false,

                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "sql_dataset_plan",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                datasets = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            key = new
                                            {
                                                type = "string"
                                            },
                                            sql = new
                                            {
                                                type = "string"
                                            }
                                        },
                                        required = new[]
                                        {
                                            "key",
                                            "sql"
                                        },
                                        additionalProperties = false
                                    }
                                }
                            },
                            required = new[]
                            {
                                "datasets"
                            },
                            additionalProperties = false
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                endpoint);

            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var apiToken = _configuration["LMStudio:ApiToken"];

            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        apiToken);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var responseContent =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"LM Studio API error: {(int)response.StatusCode}. " +
                    responseContent);
            }

            using var responseDocument =
                JsonDocument.Parse(responseContent);

            var root = responseDocument.RootElement;

            if (!root.TryGetProperty(
                    "choices",
                    out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    "LM Studio returned no completion choices.");
            }

            var choice = choices[0];

            if (choice.TryGetProperty(
                    "finish_reason",
                    out var finishReasonElement))
            {
                var finishReason = finishReasonElement.GetString();

                if (string.Equals(
                    finishReason,
                    "length",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "LM Studio response reached the token limit. " +
                        "Increase max_tokens or reduce the prompt size.");
                }
            }

            if (!choice.TryGetProperty(
                    "message",
                    out var message) ||
                !message.TryGetProperty(
                    "content",
                    out var contentElement))
            {
                throw new InvalidOperationException(
                    "LM Studio response does not contain message content.");
            }

            var generatedJson = contentElement.GetString();

            if (string.IsNullOrWhiteSpace(generatedJson))
            {
                throw new InvalidOperationException(
                    "LM Studio returned empty content.");
            }

            SqlDatasetPlan? result;

            try
            {
                result = JsonSerializer.Deserialize<SqlDatasetPlan>(
                    generatedJson,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "Failed to parse SQL dataset plan returned by LM Studio. " +
                    generatedJson,
                    ex);
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "Failed to deserialize the SQL dataset plan.");
            }

            if (result.Datasets == null)
            {
                throw new InvalidOperationException(
                    "LM Studio response does not contain a datasets array.");
            }

            foreach (var dataset in result.Datasets)
            {
                if (string.IsNullOrWhiteSpace(dataset.Key))
                {
                    throw new InvalidOperationException(
                        "Generated dataset contains an empty key.");
                }

                if (string.IsNullOrWhiteSpace(dataset.Sql))
                {
                    throw new InvalidOperationException(
                        $"Generated SQL is empty for dataset: {dataset.Key}");
                }
            }

            return result;
        }
    }
}
