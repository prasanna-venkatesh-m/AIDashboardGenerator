
using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using ClosedXML.Excel;
using System.Globalization;
using System.Text.Json;

namespace AiDashboardGenerator.Services.Repository
{
    public class ExcelQueryService : IExcelQueryService
    {
        public Task<Dictionary<string, List<Dictionary<string, object?>>>> ExecuteAsync(
            Stream stream,
            QueryPlanResponse plan,
            FileMetadata metadata)
        {
            using var workbook = new XLWorkbook(stream);

            var sheetData = new Dictionary<string, List<Dictionary<string, object?>>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var sheetName in plan.Sheets)
            {
                var worksheet = workbook.Worksheet(sheetName);
                var range = worksheet.RangeUsed();

                if (range == null)
                {
                    sheetData[sheetName] = [];
                    continue;
                }

                var firstColumn = range.FirstColumn().ColumnNumber();
                var lastColumn = range.LastColumn().ColumnNumber();
                var headerRow = range.FirstRow().RowNumber();

                var headers = Enumerable.Range(
                        firstColumn,
                        lastColumn - firstColumn + 1)
                    .ToDictionary(
                        col => worksheet.Cell(headerRow, col).GetString().Trim(),
                        col => col,
                        StringComparer.OrdinalIgnoreCase);

                var requiredColumns = GetRequiredColumns(
                    sheetName, plan, metadata);

                foreach (var column in requiredColumns)
                {
                    if (!headers.ContainsKey(column))
                    {
                        throw new Exception(
                            $"Column '{column}' not found in sheet '{sheetName}'.");
                    }
                }

                var rows = new List<Dictionary<string, object?>>();

                foreach (var row in range.RowsUsed().Skip(1))
                {
                    var record = new Dictionary<string, object?>(
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var column in requiredColumns)
                    {
                        record[$"{sheetName}.{column}"] = GetCellValue(
                            worksheet.Cell(row.RowNumber(), headers[column]));
                    }

                    rows.Add(record);
                }

                sheetData[sheetName] = rows;
            }

            var result = new Dictionary<string, List<Dictionary<string, object?>>>();

            if (plan.Joins.Count > 0)
            {
                var joinedRows = BuildJoinedRows(plan, sheetData);

                joinedRows = ApplyFilters(
                    joinedRows,
                    plan.Filters,
                    plan.FilterLogic,
                    metadata,
                    null);

                var output = ExecuteAggregations(
                    joinedRows,
                    plan.Aggregations,
                    plan.GroupBy,
                    plan.CalculatedMetrics,
                    metadata,
                    plan.Sheets);

                result[plan.Sheets.FirstOrDefault() ?? "Result"] = output;
            }
            else
            {
                foreach (var sheetName in plan.Sheets)
                {
                    var rows = sheetData[sheetName];

                    var filters = plan.Filters
                        .Where(x => string.IsNullOrWhiteSpace(x.SheetName) ||
                                    x.SheetName.Equals(
                                        sheetName,
                                        StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    rows = ApplyFilters(
                        rows,
                        filters,
                        plan.FilterLogic,
                        metadata,
                        sheetName);

                    var aggregations = plan.Aggregations
                        .Where(x => ResolveSheetName(
                            x.SheetName,
                            x.ColumnName,
                            metadata,
                            plan.Sheets).Equals(
                                sheetName,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (aggregations.Count > 0)
                    {
                        var output = ExecuteAggregations(
                            rows,
                            aggregations,
                            plan.GroupBy,
                            plan.CalculatedMetrics,
                            metadata,
                            [sheetName]);

                        result[sheetName] = output;
                    }
                    else
                    {
                        result[sheetName] = ProjectColumns(
                            rows,
                            plan.Columns
                                .Where(x => x.SheetName.Equals(
                                    sheetName,
                                    StringComparison.OrdinalIgnoreCase))
                                .Select(x => x.ColumnName)
                                .ToList(),
                            sheetName);
                    }
                }
            }

            // Build a separate grouped dataset for every chart visualization.
            // Aggregate KPI data remains unchanged under the original sheet key.
            AddVisualizationDatasets(
                result,
                sheetData,
                plan,
                metadata);

            return Task.FromResult(result);
        }

        // ---------------------------------------------------------
        // VISUALIZATION DATASETS
        // ---------------------------------------------------------

        private static void AddVisualizationDatasets(
            Dictionary<string, List<Dictionary<string, object?>>> result,
            Dictionary<string, List<Dictionary<string, object?>>> sheetData,
            QueryPlanResponse plan,
            FileMetadata metadata)
        {
            if (plan.Visualizations == null || plan.Visualizations.Count == 0)
                return;

            for (var index = 0; index < plan.Visualizations.Count; index++)
            {
                var visualization = plan.Visualizations[index];
                var type = visualization.Type?.Trim().ToLowerInvariant() ?? "";

                if (type is "kpi" or "metric" or "metric_card" or "kpi_card")
                    continue;

                var groupReference = visualization.GroupBy?
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (string.IsNullOrWhiteSpace(groupReference))
                    groupReference = visualization.XAxis;

                if (string.IsNullOrWhiteSpace(groupReference))
                    continue;

                var defaultSheet = plan.Sheets.FirstOrDefault() ?? "";
                var (sheetName, groupColumn) = ParseColumnReference(
                    groupReference, defaultSheet);

                if (!sheetData.TryGetValue(sheetName, out var sourceRows) ||
                    !HasColumn(metadata, sheetName, groupColumn))
                {
                    continue;
                }

                var filters = plan.Filters
                    .Where(x => string.IsNullOrWhiteSpace(x.SheetName) ||
                                x.SheetName.Equals(
                                    sheetName,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var filteredRows = ApplyFilters(
                    sourceRows,
                    filters,
                    plan.FilterLogic,
                    metadata,
                    sheetName);

                var title = visualization.Title ?? "";
                var entityColumn = title.Contains(
                    "account",
                    StringComparison.OrdinalIgnoreCase)
                        ? "AccountId"
                        : "ClientId";

                if (!HasColumn(metadata, sheetName, entityColumn))
                {
                    entityColumn = HasColumn(metadata, sheetName, "ClientId")
                        ? "ClientId"
                        : HasColumn(metadata, sheetName, "AccountId")
                            ? "AccountId"
                            : "";
                }

                // If the Y axis names a real numeric Excel column, sum it.
                // Otherwise, count distinct clients/accounts per category.
                var measureColumn = visualization.YAxis?.Trim() ?? "";
                var measureType = HasColumn(metadata, sheetName, measureColumn)
                    ? metadata.Sheets
                        .First(x => x.SheetName.Equals(
                            sheetName,
                            StringComparison.OrdinalIgnoreCase))
                        .Columns.First(x => x.ColumnName.Equals(
                            measureColumn,
                            StringComparison.OrdinalIgnoreCase))
                        .DataType?.ToLowerInvariant()
                    : null;

                var useNumericMeasure =
                    !string.IsNullOrWhiteSpace(measureColumn) &&
                    measureType is "number" or "numeric" or "decimal" or
                        "integer" or "int" or "double" or "float" or "currency";

                var groupedRows = filteredRows
                    .Where(row => row.GetValueOrDefault(
                        $"{sheetName}.{groupColumn}") != null)
                    .GroupBy(row => NormalizeValue(
                        row.GetValueOrDefault($"{sheetName}.{groupColumn}")))
                    .Select(group =>
                    {
                        var groupRecords = group.ToList();
                        object? categoryValue = groupRecords[0].GetValueOrDefault(
                            $"{sheetName}.{groupColumn}");

                        // Convert binary status values into readable labels.
                        var normalizedCategory = NormalizeStatusCategory(
                            groupColumn, categoryValue);

                        object metricValue;

                        if (useNumericMeasure)
                        {
                            metricValue = groupRecords
                                .Select(row => row.GetValueOrDefault(
                                    $"{sheetName}.{measureColumn}"))
                                .Where(value => value != null)
                                .Sum(value => Convert.ToDecimal(
                                    value, CultureInfo.InvariantCulture));
                        }
                        else if (!string.IsNullOrWhiteSpace(entityColumn))
                        {
                            metricValue = groupRecords
                                .Select(row => NormalizeValue(
                                    row.GetValueOrDefault(
                                        $"{sheetName}.{entityColumn}")))
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count();
                        }
                        else
                        {
                            metricValue = groupRecords.Count;
                        }

                        return new Dictionary<string, object?>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [groupColumn] = normalizedCategory,
                            ["value"] = metricValue,
                            ["record_count"] = groupRecords.Count
                        };
                    })
                    .ToList();

                // Keep date-based trends in chronological order.
                if (groupedRows.Count > 1 &&
                    (groupedRows[0][groupColumn] is DateTime ||
                     DateTime.TryParse(
                         Convert.ToString(groupedRows[0][groupColumn]),
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.None,
                         out _)))
                {
                    groupedRows = groupedRows
                        .OrderBy(row => row[groupColumn],
                            Comparer<object?>.Create(CompareValues))
                        .ToList();
                }

                result[$"chart_{index}"] = groupedRows;
            }
        }

        private static object? NormalizeStatusCategory(
            string columnName,
            object? value)
        {
            var text = Convert.ToString(
                value, CultureInfo.InvariantCulture)?.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "Unknown";

            if (columnName.Equals(
                "IsVisited", StringComparison.OrdinalIgnoreCase))
            {
                if (text is "1" || text.Equals(
                    "true", StringComparison.OrdinalIgnoreCase))
                    return "Visited";

                if (text is "0" || text.Equals(
                    "false", StringComparison.OrdinalIgnoreCase))
                    return "Not Visited";
            }

            if (columnName.Equals(
                "IsAmountPaid", StringComparison.OrdinalIgnoreCase))
            {
                if (text is "1" || text.Equals(
                    "true", StringComparison.OrdinalIgnoreCase))
                    return "Paid";

                if (text is "0" || text.Equals(
                    "false", StringComparison.OrdinalIgnoreCase))
                    return "Unpaid";
            }

            return value;
        }

        // ---------------------------------------------------------
        // REQUIRED COLUMNS
        // ---------------------------------------------------------

        private static List<string> GetRequiredColumns(
            string sheetName,
            QueryPlanResponse plan,
            FileMetadata metadata)
        {
            var columns = new List<string>();

            columns.AddRange(
                plan.Columns
                    .Where(x => x.SheetName.Equals(
                        sheetName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.ColumnName));

            // Chart grouping columns must be loaded even when they are not
            // explicitly listed in QueryPlan.Columns.
            foreach (var visualization in plan.Visualizations)
            {
                var chartColumns = new List<string>();

                if (!string.IsNullOrWhiteSpace(visualization.XAxis))
                    chartColumns.Add(visualization.XAxis);

                if (visualization.GroupBy != null)
                    chartColumns.AddRange(visualization.GroupBy);

                foreach (var reference in chartColumns)
                {
                    var (chartSheet, chartColumn) = ParseColumnReference(
                        reference, sheetName);

                    if (chartSheet.Equals(
                        sheetName,
                        StringComparison.OrdinalIgnoreCase) &&
                        HasColumn(metadata, sheetName, chartColumn))
                    {
                        columns.Add(chartColumn);
                    }
                }
            }

            // Distinct entity identifiers are needed to avoid counting
            // duplicate client/account rows in grouped chart datasets.
            if (HasColumn(metadata, sheetName, "ClientId"))
                columns.Add("ClientId");

            if (HasColumn(metadata, sheetName, "AccountId"))
                columns.Add("AccountId");

            foreach (var filter in plan.Filters)
            {
                if (string.IsNullOrWhiteSpace(filter.SheetName) ||
                    filter.SheetName.Equals(
                        sheetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (HasColumn(metadata, sheetName, filter.ColumnName))
                        columns.Add(filter.ColumnName);
                }
            }

            foreach (var group in plan.GroupBy)
            {
                var (groupSheet, groupColumn) = ParseColumnReference(
                    group, sheetName);

                if (groupSheet.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(groupColumn);
                }
            }

            foreach (var aggregation in plan.Aggregations)
            {
                var aggregationSheet = ResolveSheetName(
                    aggregation.SheetName,
                    aggregation.ColumnName,
                    metadata,
                    plan.Sheets);

                if (aggregationSheet.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(aggregation.ColumnName))
                        columns.Add(aggregation.ColumnName);

                    foreach (var condition in aggregation.Conditions)
                    {
                        if (string.IsNullOrWhiteSpace(condition.SheetName) ||
                            condition.SheetName.Equals(
                                sheetName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            columns.Add(condition.ColumnName);
                        }
                    }
                }
            }

            foreach (var join in plan.Joins)
            {
                if (join.LeftSheet.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(join.LeftColumn);
                }

                if (join.RightSheet.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(join.RightColumn);
                }
            }

            return columns
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ---------------------------------------------------------
        // EXCEL VALUE
        // ---------------------------------------------------------

        private static object? GetCellValue(IXLCell cell)
        {
            if (cell.IsEmpty())
                return null;

            return cell.DataType switch
            {
                XLDataType.Text => cell.GetString(),
                XLDataType.Number => cell.GetDouble(),
                XLDataType.Boolean => cell.GetBoolean(),
                XLDataType.DateTime => cell.GetDateTime(),
                XLDataType.TimeSpan => cell.GetTimeSpan(),
                _ => cell.GetString()
            };
        }

        // ---------------------------------------------------------
        // FILTERS
        // ---------------------------------------------------------

        private static List<Dictionary<string, object?>> ApplyFilters(
            List<Dictionary<string, object?>> rows,
            List<FilterCondition> filters,
            string filterLogic,
            FileMetadata metadata,
            string? defaultSheet)
        {
            if (filters.Count == 0)
                return rows;

            return rows.Where(row =>
            {
                var results = filters.Select(filter =>
                {
                    var sheet = string.IsNullOrWhiteSpace(filter.SheetName)
                        ? defaultSheet ?? ResolveSheetName(
                            "", filter.ColumnName, metadata,
                            metadata.Sheets.Select(x => x.SheetName).ToList())
                        : filter.SheetName;

                    var key = $"{sheet}.{filter.ColumnName}";

                    row.TryGetValue(key, out var value);

                    return ApplyFilter(value, filter);
                }).ToList();

                return filterLogic.Equals(
                    "OR",
                    StringComparison.OrdinalIgnoreCase)
                        ? results.Any(x => x)
                        : results.All(x => x);
            }).ToList();
        }

        private static bool ApplyFilter(
            object? cellValue,
            FilterCondition filter)
        {
            var operation = filter.Operator.Trim().ToLowerInvariant();

            if (operation is "is_null" or "is_empty")
                return cellValue == null ||
                       string.IsNullOrWhiteSpace(Convert.ToString(cellValue));

            if (operation is "is_not_null" or "is_not_empty")
                return cellValue != null &&
                       !string.IsNullOrWhiteSpace(Convert.ToString(cellValue));

            if (cellValue == null)
                return false;

            var actual = Convert.ToString(
                cellValue,
                CultureInfo.InvariantCulture) ?? "";

            var expected = filter.Value ?? "";

            var actualNumber = decimal.TryParse(
                actual,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var number);

            var expectedNumber = decimal.TryParse(
                expected,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var expectedValue);

            var comparison = actualNumber && expectedNumber
                ? number.CompareTo(expectedValue)
                : string.Compare(
                    actual,
                    expected,
                    StringComparison.OrdinalIgnoreCase);

            return operation switch
            {
                "=" or "==" or "equals" => comparison == 0,
                "!=" or "<>" or "not_equals" => comparison != 0,
                ">" or "greater_than" => actualNumber && expectedNumber && number > expectedValue,
                "<" or "less_than" => actualNumber && expectedNumber && number < expectedValue,
                ">=" or "greater_than_or_equal" => actualNumber && expectedNumber && number >= expectedValue,
                "<=" or "less_than_or_equal" => actualNumber && expectedNumber && number <= expectedValue,

                "contains" => actual.Contains(
                    expected, StringComparison.OrdinalIgnoreCase),

                "not_contains" => !actual.Contains(
                    expected, StringComparison.OrdinalIgnoreCase),

                "starts_with" => actual.StartsWith(
                    expected, StringComparison.OrdinalIgnoreCase),

                "ends_with" => actual.EndsWith(
                    expected, StringComparison.OrdinalIgnoreCase),

                "between" => actualNumber &&
                    expectedNumber &&
                    decimal.TryParse(
                        filter.SecondValue,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var secondValue) &&
                    number >= expectedValue &&
                    number <= secondValue,

                "in" => expected.Split(',')
                    .Select(x => x.Trim())
                    .Contains(actual, StringComparer.OrdinalIgnoreCase),

                _ => false
            };
        }

        // ---------------------------------------------------------
        // AGGREGATIONS
        // ---------------------------------------------------------

        private static List<Dictionary<string, object?>> ExecuteAggregations(
            List<Dictionary<string, object?>> rows,
            List<Aggregation> aggregations,
            List<string> groupBy,
            List<CalculatedMetric> calculatedMetrics,
            FileMetadata metadata,
            List<string> sheets)
        {
            var groups = groupBy.Count == 0
                ? new List<List<Dictionary<string, object?>>> { rows }
                : rows.GroupBy(row =>
                    JsonSerializer.Serialize(groupBy.Select(column =>
                    {
                        var (sheet, name) = ParseColumnReference(
                            column, sheets.FirstOrDefault() ?? "");

                        return row.GetValueOrDefault($"{sheet}.{name}");
                    }).ToList()))
                    .Select(x => x.ToList())
                    .ToList();

            var result = new List<Dictionary<string, object?>>();

            foreach (var records in groups)
            {
                var output = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var groupColumn in groupBy)
                {
                    var (sheet, column) = ParseColumnReference(
                        groupColumn, sheets.FirstOrDefault() ?? "");

                    output[groupColumn] = records.FirstOrDefault()?
                        .GetValueOrDefault($"{sheet}.{column}");
                }

                foreach (var aggregation in aggregations)
                {
                    var sheet = ResolveSheetName(
                        aggregation.SheetName,
                        aggregation.ColumnName,
                        metadata,
                        sheets);

                    var filteredRecords = records
                        .Where(record => aggregation.Conditions.All(condition =>
                        {
                            var conditionSheet = string.IsNullOrWhiteSpace(
                                condition.SheetName)
                                    ? sheet
                                    : condition.SheetName;

                            var value = record.GetValueOrDefault(
                                $"{conditionSheet}.{condition.ColumnName}");

                            return ApplyFilter(value, condition);
                        }))
                        .ToList();

                    var values = string.IsNullOrWhiteSpace(
                        aggregation.ColumnName)
                            ? filteredRecords.Select(x => (object?)1).ToList()
                            : filteredRecords
                                .Select(x => x.GetValueOrDefault(
                                    $"{sheet}.{aggregation.ColumnName}"))
                                .Where(x => x != null)
                                .ToList();

                    var function = aggregation.Function
                        .Trim()
                        .ToLowerInvariant()
                        .Replace("-", "_")
                        .Replace(" ", "_");

                    var outputName = string.IsNullOrWhiteSpace(aggregation.Name)
                        ? $"{aggregation.Function}_{aggregation.ColumnName}"
                        : aggregation.Name;

                    output[outputName] = CalculateAggregation(
                        function,
                        values,
                        filteredRecords.Count,
                        metadata,
                        sheet,
                        aggregation.ColumnName);
                }

                foreach (var metric in calculatedMetrics)
                {
                    // GROUP_BY is a query-plan grouping instruction, not a
                    // mathematical formula function. Some AI-generated plans
                    // incorrectly place it inside CalculatedMetric.Formula.
                    // Preserve the metric key as null and continue processing
                    // the dashboard instead of passing GROUP_BY to FormulaParser.
                    if (!string.IsNullOrWhiteSpace(metric.Formula) &&
                        metric.Formula.Contains(
                            "GROUP_BY",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        output[metric.Name] = null;
                        continue;
                    }

                    var value = EvaluateFormula(metric.Formula, output);
                    output[metric.Name] = value;
                }

                result.Add(output);
            }

            return result;
        }

        private static object? CalculateAggregation(
            string function,
            List<object?> values,
            int rowCount,
            FileMetadata metadata,
            string sheetName,
            string columnName)
        {
            // Normalize common aggregation names generated by AI models.
            function = function
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

            function = function switch
            {
                "avg" or "mean" or "average_value" => "average",
                "total" or "summation" => "sum",
                "min_value" or "minimum" => "min",
                "max_value" or "maximum" => "max",
                "distinct" or "unique_count" or "count_unique" => "count_distinct",
                "countrows" or "row_count" or "rows_count" => "count_rows",
                _ => function
            };

            switch (function)
            {
                case "count":
                    return values.Count;

                case "count_rows":
                    return rowCount;

                case "count_distinct":
                case "distinct_count":
                    return values
                        .Select(NormalizeValue)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                case "sum":
                case "average":
                    EnsureNumericColumn(metadata, sheetName, columnName);

                    var numbers = values.Select(x =>
                        Convert.ToDecimal(
                            x,
                            CultureInfo.InvariantCulture)).ToList();

                    if (function == "sum")
                        return numbers.Sum();

                    return numbers.Count > 0
                        ? numbers.Average()
                        : 0m;

                case "min":
                case "max":
                    if (values.Count == 0)
                        return null;

                    var ordered = values.OrderBy(
                        x => x,
                        Comparer<object?>.Create(CompareValues)).ToList();

                    return function == "min"
                        ? ordered.First()
                        : ordered.Last();

                default:
                    throw new NotSupportedException(
                        $"Aggregation function '{function}' is not supported.");
            }
        }

        private static void EnsureNumericColumn(
            FileMetadata metadata,
            string sheetName,
            string columnName)
        {
            var column = metadata.Sheets
                .FirstOrDefault(x => x.SheetName.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))?
                .Columns.FirstOrDefault(x => x.ColumnName.Equals(
                    columnName,
                    StringComparison.OrdinalIgnoreCase));

            var type = column?.DataType?.ToLowerInvariant();

            var numericTypes = new[]
            {
                "number", "numeric", "decimal", "integer",
                "int", "int32", "int64", "double", "float",
                "currency", "percentage"
            };

            if (type == null ||
                !numericTypes.Contains(type))
            {
                throw new InvalidOperationException(
                    $"Cannot perform SUM or AVERAGE on non-numeric column " +
                    $"'{columnName}' in sheet '{sheetName}'. Detected type: '{column?.DataType ?? "Unknown"}'.");
            }
        }

        // ---------------------------------------------------------
        // CALCULATED METRICS
        // ---------------------------------------------------------

        private static double EvaluateFormula(
            string formula,
            Dictionary<string, object?> values)
        {
            var numericValues = values
                .Where(x => x.Value != null)
                .ToDictionary(
                    x => x.Key,
                    x => Convert.ToDouble(
                        x.Value,
                        CultureInfo.InvariantCulture),
                    StringComparer.OrdinalIgnoreCase);

            return new FormulaParser(formula, numericValues).Parse();
        }

        private class FormulaParser
        {
            private readonly string _expression;
            private readonly Dictionary<string, double> _values;
            private int _position;

            public FormulaParser(
                string expression,
                Dictionary<string, double> values)
            {
                _expression = expression;
                _values = values;
            }

            public double Parse()
            {
                var result = ParseExpression();

                SkipSpaces();

                if (_position < _expression.Length)
                    throw new FormatException("Invalid calculated metric formula.");

                return result;
            }

            private double ParseExpression()
            {
                var value = ParseTerm();

                while (true)
                {
                    SkipSpaces();

                    if (Match('+'))
                        value += ParseTerm();
                    else if (Match('-'))
                        value -= ParseTerm();
                    else
                        return value;
                }
            }

            private double ParseTerm()
            {
                var value = ParseFactor();

                while (true)
                {
                    SkipSpaces();

                    if (Match('*'))
                    {
                        value *= ParseFactor();
                    }
                    else if (Match('/'))
                    {
                        var divisor = ParseFactor();

                        value = Math.Abs(divisor) < 0.0000000001
                            ? 0
                            : value / divisor;
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseFactor()
            {
                SkipSpaces();

                if (Match('+'))
                    return ParseFactor();

                if (Match('-'))
                    return -ParseFactor();

                if (Match('('))
                {
                    var value = ParseExpression();

                    if (!Match(')'))
                        throw new FormatException("Missing closing parenthesis.");

                    return value;
                }

                if (_position < _expression.Length &&
                    (char.IsDigit(_expression[_position]) ||
                     _expression[_position] == '.'))
                {
                    return ParseNumber();
                }

                return ParseVariable();
            }

            private double ParseNumber()
            {
                var start = _position;

                while (_position < _expression.Length &&
                       (char.IsDigit(_expression[_position]) ||
                        _expression[_position] == '.'))
                {
                    _position++;
                }

                return double.Parse(
                    _expression[start.._position],
                    CultureInfo.InvariantCulture);
            }

            private double ParseVariable()
            {
                var start = _position;

                while (_position < _expression.Length &&
                       (char.IsLetterOrDigit(_expression[_position]) ||
                        _expression[_position] == '_'))
                {
                    _position++;
                }

                if (start == _position)
                    throw new FormatException("Invalid formula variable.");

                var name = _expression[start.._position];
                SkipSpaces();

                // Handle supported formula functions before treating the
                // identifier as a column/metric name.
                if (Match('('))
                    return ParseFunction(name);

                if (!_values.TryGetValue(name, out var value))
                {
                    throw new Exception(
                        $"Calculated metric references unknown value '{name}'.");
                }

                return value;
            }

            private double ParseFunction(string functionName)
            {
                if (functionName.Equals("NULLIF", StringComparison.OrdinalIgnoreCase))
                {
                    // NULLIF(value1, value2) returns zero when both values
                    // are equal. Division by zero is safely handled by ParseTerm.
                    var firstValue = ParseExpression();

                    SkipSpaces();
                    if (!Match(','))
                        throw new FormatException(
                            "NULLIF requires two arguments separated by a comma.");

                    var secondValue = ParseExpression();

                    SkipSpaces();
                    if (!Match(')'))
                        throw new FormatException(
                            "Missing closing parenthesis in NULLIF function.");

                    return Math.Abs(firstValue - secondValue) < 0.0000000001
                        ? 0
                        : firstValue;
                }

                if (functionName.Equals("IF", StringComparison.OrdinalIgnoreCase))
                {
                    // IF(condition, value_if_true, value_if_false)
                    var condition = ParseCondition();

                    SkipSpaces();
                    if (!Match(','))
                        throw new FormatException(
                            "IF requires three arguments separated by commas.");

                    var trueValue = ParseExpression();

                    SkipSpaces();
                    if (!Match(','))
                        throw new FormatException(
                            "IF requires three arguments separated by commas.");

                    var falseValue = ParseExpression();

                    SkipSpaces();
                    if (!Match(')'))
                        throw new FormatException(
                            "Missing closing parenthesis in IF function.");

                    return Math.Abs(condition) > 0.0000000001
                        ? trueValue
                        : falseValue;
                }

                throw new NotSupportedException(
                    $"Calculated metric function '{functionName}' is not supported.");
            }

            private double ParseCondition()
            {
                var left = ParseExpression();
                SkipSpaces();

                if (MatchString(">="))
                    return left >= ParseExpression() ? 1 : 0;

                if (MatchString("<="))
                    return left <= ParseExpression() ? 1 : 0;

                if (MatchString("<>"))
                    return Math.Abs(left - ParseExpression()) > 0.0000000001 ? 1 : 0;

                if (MatchString("!="))
                    return Math.Abs(left - ParseExpression()) > 0.0000000001 ? 1 : 0;

                if (MatchString("=="))
                    return Math.Abs(left - ParseExpression()) < 0.0000000001 ? 1 : 0;

                if (Match('>'))
                    return left > ParseExpression() ? 1 : 0;

                if (Match('<'))
                    return left < ParseExpression() ? 1 : 0;

                if (Match('='))
                    return Math.Abs(left - ParseExpression()) < 0.0000000001 ? 1 : 0;

                // A numeric condition is also accepted: zero is false,
                // any non-zero value is true.
                return Math.Abs(left) > 0.0000000001 ? 1 : 0;
            }

            private bool MatchString(string value)
            {
                if (_position + value.Length > _expression.Length ||
                    !_expression.AsSpan(_position, value.Length).SequenceEqual(value))
                {
                    return false;
                }

                _position += value.Length;
                return true;
            }

            private bool Match(char character)
            {
                if (_position >= _expression.Length ||
                    _expression[_position] != character)
                    return false;

                _position++;
                return true;
            }

            private void SkipSpaces()
            {
                while (_position < _expression.Length &&
                       char.IsWhiteSpace(_expression[_position]))
                {
                    _position++;
                }
            }
        }

        // ---------------------------------------------------------
        // JOINS
        // ---------------------------------------------------------

        private static List<Dictionary<string, object?>> BuildJoinedRows(
            QueryPlanResponse plan,
            Dictionary<string, List<Dictionary<string, object?>>> sheetData)
        {
            var firstSheet = plan.Sheets.First();

            var rows = sheetData[firstSheet];
            var joinedSheets = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                firstSheet
            };

            while (joinedSheets.Count < plan.Sheets.Count)
            {
                var join = plan.Joins.FirstOrDefault(x =>
                    joinedSheets.Contains(x.LeftSheet) &&
                    !joinedSheets.Contains(x.RightSheet));

                if (join == null)
                {
                    throw new InvalidOperationException(
                        "Unable to connect all requested sheets. Check the join conditions.");
                }

                rows = JoinRows(
                    rows,
                    sheetData[join.RightSheet],
                    join);

                joinedSheets.Add(join.RightSheet);
            }

            return rows;
        }

        private static List<Dictionary<string, object?>> JoinRows(
            List<Dictionary<string, object?>> leftRows,
            List<Dictionary<string, object?>> rightRows,
            JoinCondition join)
        {
            var result = new List<Dictionary<string, object?>>();
            var matchedRight = new HashSet<int>();

            var joinType = join.JoinType.ToLowerInvariant();

            if (joinType is not ("inner" or "left" or "right" or "full" or "full_outer" or "left_outer" or "right_outer"))
            {
                throw new NotSupportedException(
                    $"Join type '{join.JoinType}' is not supported.");
            }

            foreach (var left in leftRows)
            {
                var matches = new List<(Dictionary<string, object?> Row, int Index)>();

                for (var i = 0; i < rightRows.Count; i++)
                {
                    var right = rightRows[i];

                    var leftValue = left.GetValueOrDefault(
                        $"{join.LeftSheet}.{join.LeftColumn}");

                    var rightValue = right.GetValueOrDefault(
                        $"{join.RightSheet}.{join.RightColumn}");

                    if (ValuesEqual(leftValue, rightValue))
                        matches.Add((right, i));
                }

                if (matches.Count > 0)
                {
                    foreach (var match in matches)
                    {
                        matchedRight.Add(match.Index);
                        result.Add(MergeRows(left, match.Row));
                    }
                }
                else if (joinType is "left" or "full" or "left_outer" or "full_outer")
                {
                    result.Add(new Dictionary<string, object?>(left));
                }
            }

            if (joinType is "right" or "full" or "right_outer" or "full_outer")
            {
                for (var i = 0; i < rightRows.Count; i++)
                {
                    if (!matchedRight.Contains(i))
                    {
                        result.Add(new Dictionary<string, object?>(
                            rightRows[i]));
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, object?> MergeRows(
            Dictionary<string, object?> left,
            Dictionary<string, object?> right)
        {
            var merged = new Dictionary<string, object?>(
                left,
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in right)
                merged[item.Key] = item.Value;

            return merged;
        }

        // ---------------------------------------------------------
        // PROJECTION
        // ---------------------------------------------------------

        private static List<Dictionary<string, object?>> ProjectColumns(
            List<Dictionary<string, object?>> rows,
            List<string> columns,
            string sheetName)
        {
            return rows.Select(row =>
            {
                var output = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var column in columns)
                {
                    output[column] = row.GetValueOrDefault(
                        $"{sheetName}.{column}");
                }

                return output;
            }).ToList();
        }

        // ---------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------

        private static string ResolveSheetName(
            string requestedSheet,
            string columnName,
            FileMetadata metadata,
            List<string> availableSheets)
        {
            if (!string.IsNullOrWhiteSpace(requestedSheet))
                return requestedSheet;

            return metadata.Sheets
                .FirstOrDefault(sheet =>
                    availableSheets.Contains(
                        sheet.SheetName,
                        StringComparer.OrdinalIgnoreCase) &&
                    sheet.Columns.Any(column =>
                        column.ColumnName.Equals(
                            columnName,
                            StringComparison.OrdinalIgnoreCase)))
                ?.SheetName ?? availableSheets.FirstOrDefault() ?? "";
        }

        private static bool HasColumn(
            FileMetadata metadata,
            string sheetName,
            string columnName)
        {
            return metadata.Sheets
                .FirstOrDefault(x => x.SheetName.Equals(
                    sheetName,
                    StringComparison.OrdinalIgnoreCase))?
                .Columns.Any(x => x.ColumnName.Equals(
                    columnName,
                    StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static (string Sheet, string Column) ParseColumnReference(
            string reference,
            string defaultSheet)
        {
            var parts = reference.Split('.', 2);

            return parts.Length == 2
                ? (parts[0], parts[1])
                : (defaultSheet, reference);
        }

        private static bool ValuesEqual(object? left, object? right)
        {
            if (left == null || right == null)
                return false;

            var leftText = Convert.ToString(
                left, CultureInfo.InvariantCulture) ?? "";

            var rightText = Convert.ToString(
                right, CultureInfo.InvariantCulture) ?? "";

            if (decimal.TryParse(leftText, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var leftNumber) &&
                decimal.TryParse(rightText, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var rightNumber))
            {
                return leftNumber == rightNumber;
            }

            return string.Equals(
                leftText,
                rightText,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeValue(object? value)
        {
            if (value == null)
                return "";

            if (value is DateTime date)
                return date.ToString("O", CultureInfo.InvariantCulture);

            return Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? "";
        }

        private static int CompareValues(object? left, object? right)
        {
            if (left == null)
                return right == null ? 0 : -1;

            if (right == null)
                return 1;

            var leftText = Convert.ToString(
                left, CultureInfo.InvariantCulture) ?? "";

            var rightText = Convert.ToString(
                right, CultureInfo.InvariantCulture) ?? "";

            if (decimal.TryParse(leftText, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var leftNumber) &&
                decimal.TryParse(rightText, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (left is DateTime leftDate && right is DateTime rightDate)
                return leftDate.CompareTo(rightDate);

            return string.Compare(
                leftText,
                rightText,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}