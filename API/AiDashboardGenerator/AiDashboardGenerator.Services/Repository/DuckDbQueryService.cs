using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using DuckDB.NET.Data;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace AiDashboardGenerator.Services.Repository;

public sealed class DuckDbQueryService : IDuckDbQueryService
{
    // Limits concurrent in-memory DuckDB jobs per API process.
    // Tune this after load testing with representative Excel files.
    private static readonly SemaphoreSlim QuerySemaphore = new(4, 4);

    private readonly IConfiguration _configuration;

    private static readonly Regex ForbiddenSql = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|ATTACH|DETACH|COPY|EXPORT|IMPORT|INSTALL|LOAD|PRAGMA|CALL|EXECUTE|PREPARE|BEGIN|COMMIT|ROLLBACK|VACUUM|SET|USE|READ_[A-Z0-9_]*|GLOB|HTTPFS|PARQUET_SCAN|CSV_SCAN|JSON_SCAN|SQLITE_SCAN|POSTGRES_SCAN|MYSQL_SCAN|QUERY|DUCKDB_[A-Z0-9_]*|INFORMATION_SCHEMA)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DuckDbQueryService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<Dictionary<string, List<Dictionary<string, object?>>>> ExecuteAsync(
        Stream excelStream,
        FileMetadata metadata,
        SqlDatasetPlan sqlPlan,
        CancellationToken cancellationToken = default)
    {
        if (metadata.Sheets == null || metadata.Sheets.Count == 0)
            throw new InvalidOperationException("The uploaded Excel file has no worksheet metadata.");

        if (sqlPlan.Datasets == null || sqlPlan.Datasets.Count == 0)
            throw new InvalidOperationException("No SQL datasets were generated.");

        await QuerySemaphore.WaitAsync(cancellationToken);

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "AiDashboardGenerator",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(workDirectory);
            var excelPath = Path.Combine(workDirectory, "source.xlsx");
            var tempDirectory = Path.Combine(workDirectory, "duckdb-temp");
            Directory.CreateDirectory(tempDirectory);

            await using (var fileStream = new FileStream(
                excelPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                await excelStream.CopyToAsync(fileStream, cancellationToken);
            }

            var memoryLimit = _configuration["DuckDB:MemoryLimit"] ?? "512MB";
            var threads = int.TryParse(_configuration["DuckDB:Threads"], out var configuredThreads)
                ? Math.Clamp(configuredThreads, 1, 8)
                : 2;
            var maxRows = int.TryParse(_configuration["DuckDB:MaxResultRows"], out var configuredRows)
                ? Math.Clamp(configuredRows, 1, 100000)
                : 10000;

            await using var connection = new DuckDBConnection("DataSource=:memory:");
            await connection.OpenAsync(cancellationToken);

            await ExecuteNonQueryAsync(connection, $"SET memory_limit = '{EscapeSqlString(memoryLimit)}';", cancellationToken);
            await ExecuteNonQueryAsync(connection, $"SET temp_directory = '{EscapeSqlString(tempDirectory)}';", cancellationToken);
            await ExecuteNonQueryAsync(connection, $"SET threads = {threads};", cancellationToken);

            // The Excel extension is installed once per host and loaded for this
            // in-memory connection. The first installation requires outbound access.
            await ExecuteNonQueryAsync(connection, "INSTALL excel;", cancellationToken);
            await ExecuteNonQueryAsync(connection, "LOAD excel;", cancellationToken);

            // Import worksheets into temporary DuckDB tables. These tables exist
            // only for this connection and are discarded when it is disposed.
            foreach (var sheet in metadata.Sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sheetName = sheet.SheetName?.Trim();
                if (string.IsNullOrWhiteSpace(sheetName))
                    continue;

                var createTableSql = $"""
                    CREATE TEMP TABLE {QuoteIdentifier(sheetName)} AS
                    SELECT *
                    FROM read_xlsx(
                        '{EscapeSqlString(excelPath)}',
                        sheet = '{EscapeSqlString(sheetName)}',
                        header = true,
                        all_varchar = true
                    );
                    """;

                await ExecuteNonQueryAsync(connection, createTableSql, cancellationToken);
            }

            var result = new Dictionary<string, List<Dictionary<string, object?>>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var dataset in sqlPlan.Datasets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(dataset.Key))
                    throw new InvalidOperationException("A SQL dataset key is empty.");

                if (result.ContainsKey(dataset.Key))
                    throw new InvalidOperationException($"Duplicate SQL dataset key '{dataset.Key}'.");

                var sql = ValidateAndNormalizeSql(dataset.Sql);
                var limitedSql = $"SELECT * FROM ({sql}) AS _dashboard_result LIMIT {maxRows}";

                result[dataset.Key] = await ExecuteDatasetAsync(
                    connection,
                    limitedSql,
                    cancellationToken);
            }

            return result;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                    Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Temporary-file cleanup should not hide the query result/error.
                // A scheduled host-level cleanup can remove abandoned temp folders.
            }
            finally
            {
                QuerySemaphore.Release();
            }
        }
    }

    private static string ValidateAndNormalizeSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("Groq returned an empty SQL statement.");

        sql = sql.Trim();
        sql = sql.TrimEnd(';').Trim();

        if (sql.Contains(';'))
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");

        if (!(sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
              sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Only SELECT queries are allowed.");
        }

        if (ForbiddenSql.IsMatch(sql))
            throw new InvalidOperationException("The generated SQL contains a prohibited operation or external data function.");

        if (sql.Contains("--", StringComparison.Ordinal) ||
            sql.Contains("/*", StringComparison.Ordinal) ||
            sql.Contains("*/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQL comments are not allowed.");
        }

        return sql;
    }

    private static async Task<List<Dictionary<string, object?>>> ExecuteDatasetAsync(
        DuckDBConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var value = reader.GetValue(ordinal);
                row[reader.GetName(ordinal)] = value == DBNull.Value ? null : NormalizeValue(value);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object? NormalizeValue(object value)
    {
        return value switch
        {
            DBNull => null,
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            TimeOnly time => time.ToString("HH:mm:ss.fffffff"),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }

    private static async Task ExecuteNonQueryAsync(
        DuckDBConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeSqlString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
