using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Text;

using AiDashboardGenerator.Models;
using AiDashboardGenerator.Services.Interface;
using ClosedXML.Excel;
using System.Globalization;

namespace AiDashboardGenerator.Services.Repository
{
    public class ExcelMetadataService : IExcelMetadataService
    {
        private const int MaxDistinctValues = 50;
        private const int MaxSampleValues = 5;

        public Task<List<SheetMetadata>> ExtractAsync(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);

            var sheets = new List<SheetMetadata>();

            // Retain actual PK values for FK detection.
            var primaryKeyValues =
                new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var worksheet in workbook.Worksheets)
            {
                var range = worksheet.RangeUsed();

                if (range == null)
                    continue;

                var firstRow = range.FirstRow().RowNumber();
                var firstColumn = range.FirstColumn().ColumnNumber();
                var lastColumn = range.LastColumn().ColumnNumber();

                var dataRows = range.RowsUsed()
                    .Skip(1)
                    .ToList();

                var columns = new List<ColumnMetadata>();

                for (int col = firstColumn; col <= lastColumn; col++)
                {
                    var columnName = worksheet
                        .Cell(firstRow, col)
                        .GetString()
                        .Trim();

                    if (string.IsNullOrWhiteSpace(columnName))
                        columnName = $"Column_{col}";

                    var profile = ProfileColumn(
                        worksheet,
                        dataRows,
                        col,
                        columnName);

                    columns.Add(profile.Metadata);

                    if (profile.Metadata.IsPossiblePK &&
                        profile.UniqueValues != null)
                    {
                        primaryKeyValues[
                            GetColumnKey(worksheet.Name, columnName)
                        ] = profile.UniqueValues;
                    }
                }

                sheets.Add(new SheetMetadata
                {
                    SheetName = worksheet.Name,
                    RowCount = dataRows.Count,
                    ColumnCount = columns.Count,
                    Columns = columns
                });
            }

            DetectPossibleForeignKeys(
                workbook,
                sheets,
                primaryKeyValues);

            return Task.FromResult(sheets);
        }

        // ---------------------------------------------------------
        // COLUMN PROFILING
        // ---------------------------------------------------------

        private static ColumnProfile ProfileColumn(
            IXLWorksheet worksheet,
            List<IXLRangeRow> rows,
            int columnNumber,
            string columnName)
        {
            var uniqueValues = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            var valueCounts = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            var sampleValues = new List<string>();

            var random = new Random(17);

            var captureValueCounts = true;

            var nonEmptyCount = 0;
            var nullCount = 0;
            var uniqueCount = 0;

            var typeStats = new ColumnTypeStats();

            foreach (var row in rows)
            {
                var cell = worksheet.Cell(
                    row.RowNumber(),
                    columnNumber);

                if (cell.IsEmpty() ||
                    string.IsNullOrWhiteSpace(cell.GetString()))
                {
                    nullCount++;
                    continue;
                }

                nonEmptyCount++;

                UpdateDataType(typeStats, cell);

                var value = GetCellStringValue(cell);

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var isNewValue = uniqueValues.Add(value);

                if (!isNewValue)
                {
                    if (captureValueCounts &&
                        valueCounts.ContainsKey(value))
                    {
                        valueCounts[value]++;
                    }

                    continue;
                }

                uniqueCount++;

                // Store frequencies only while cardinality is low.
                if (captureValueCounts)
                {
                    if (uniqueCount <= MaxDistinctValues)
                    {
                        valueCounts[value] = 1;
                    }
                    else
                    {
                        captureValueCounts = false;
                        valueCounts.Clear();
                    }
                }

                // Reservoir sampling over distinct values.
                if (sampleValues.Count < MaxSampleValues)
                {
                    sampleValues.Add(value);
                }
                else
                {
                    var index = random.Next(uniqueCount);

                    if (index < MaxSampleValues)
                    {
                        sampleValues[index] = value;
                    }
                }
            }

            var duplicateCount = nonEmptyCount - uniqueCount;

            var isPossiblePK =
                nonEmptyCount > 0 &&
                uniqueCount == nonEmptyCount &&
                nullCount == 0;

            var uniquePercentage = nonEmptyCount > 0
                ? (double)uniqueCount / nonEmptyCount * 100
                : 0;

            var dataType = DetectDataType(typeStats);

            var isIdentifier =
                isPossiblePK ||
                (uniquePercentage >= 80 &&
                 nonEmptyCount >= 20 &&
                 LooksLikeIdentifier(columnName));

            var isCategorical =
                !isIdentifier &&
                uniqueCount > 0 &&
                uniqueCount <= MaxDistinctValues;

            var metadata = new ColumnMetadata
            {
                ColumnName = columnName,

                DataType = dataType,

                NullCount = nullCount,

                NonEmptyCount = nonEmptyCount,

                UniqueCount = uniqueCount,

                DuplicateCount = duplicateCount,

                UniquePercentage = Math.Round(
                    uniquePercentage, 2),

                IsPossiblePK = isPossiblePK,

                IsPossibleFK = false,

                IsCategorical = isCategorical,

                IsIdentifier = isIdentifier,

                SampleValues = captureValueCounts
                    ? valueCounts.Keys.ToList()
                    : sampleValues,

                ValueCounts = captureValueCounts
                    ? valueCounts
                    : new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase)
            };

            return new ColumnProfile
            {
                Metadata = metadata,

                // Keep all values only for potential PK columns.
                UniqueValues = isPossiblePK
                    ? uniqueValues
                    : null
            };
        }

        // ---------------------------------------------------------
        // DATA TYPE DETECTION
        // ---------------------------------------------------------

        private static void UpdateDataType(
            ColumnTypeStats stats,
            IXLCell cell)
        {
            switch (cell.DataType)
            {
                case XLDataType.Text:
                    stats.HasText = true;
                    break;

                case XLDataType.Number:
                    var number = cell.GetDouble();

                    if (number % 1 == 0)
                        stats.HasInteger = true;
                    else
                        stats.HasDecimal = true;

                    break;

                case XLDataType.DateTime:
                    stats.HasDateTime = true;
                    break;

                case XLDataType.Boolean:
                    stats.HasBoolean = true;
                    break;

                case XLDataType.TimeSpan:
                    stats.HasTimeSpan = true;
                    break;

                default:
                    stats.HasOther = true;
                    break;
            }
        }

        private static string DetectDataType(
            ColumnTypeStats stats)
        {
            var types = new List<string>();

            if (stats.HasText)
                types.Add("String");

            if (stats.HasInteger)
                types.Add("Integer");

            if (stats.HasDecimal)
                types.Add("Decimal");

            if (stats.HasDateTime)
                types.Add("DateTime");

            if (stats.HasBoolean)
                types.Add("Boolean");

            if (stats.HasTimeSpan)
                types.Add("TimeSpan");

            if (stats.HasOther)
                types.Add("Other");

            if (types.Count == 0)
                return "Unknown";

            if (types.Count == 1)
                return types[0];

            if (stats.HasInteger &&
                stats.HasDecimal &&
                types.Count == 2)
            {
                return "Decimal";
            }

            return "Mixed";
        }

        // ---------------------------------------------------------
        // FOREIGN KEY DETECTION
        // ---------------------------------------------------------

        private static void DetectPossibleForeignKeys(
            XLWorkbook workbook,
            List<SheetMetadata> sheets,
            Dictionary<string, HashSet<string>> primaryKeyValues)
        {
            foreach (var sheet in sheets)
            {
                var worksheet = workbook.Worksheet(sheet.SheetName);

                var range = worksheet.RangeUsed();

                if (range == null)
                    continue;

                var headerRow = range.FirstRow().RowNumber();

                foreach (var column in sheet.Columns)
                {
                    foreach (var otherSheet in sheets)
                    {
                        if (sheet.SheetName.Equals(
                            otherSheet.SheetName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var primaryKeyColumn = otherSheet.Columns
                            .FirstOrDefault(x =>
                                x.IsPossiblePK &&
                                x.ColumnName.Equals(
                                    column.ColumnName,
                                    StringComparison.OrdinalIgnoreCase));

                        if (primaryKeyColumn == null)
                            continue;

                        var primaryKeyKey = GetColumnKey(
                            otherSheet.SheetName,
                            primaryKeyColumn.ColumnName);

                        if (!primaryKeyValues.TryGetValue(
                            primaryKeyKey,
                            out var referencedValues))
                        {
                            continue;
                        }

                        var columnNumber = FindColumnNumber(
                            worksheet,
                            headerRow,
                            column.ColumnName);

                        if (columnNumber == 0)
                            continue;

                        var sourceRows = range.RowsUsed().Skip(1);

                        var nonEmptyCount = 0;
                        var matchingCount = 0;

                        foreach (var row in sourceRows)
                        {
                            var cell = worksheet.Cell(
                                row.RowNumber(),
                                columnNumber);

                            if (cell.IsEmpty() ||
                                string.IsNullOrWhiteSpace(cell.GetString()))
                            {
                                continue;
                            }

                            nonEmptyCount++;

                            var value = GetCellStringValue(cell);

                            if (referencedValues.Contains(value))
                                matchingCount++;
                        }

                        if (nonEmptyCount > 0 &&
                            matchingCount == nonEmptyCount)
                        {
                            column.IsPossibleFK = true;
                            column.IsIdentifier = true;
                            break;
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------

        private static int FindColumnNumber(
            IXLWorksheet worksheet,
            int headerRow,
            string columnName)
        {
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int col = 1; col <= lastColumn; col++)
            {
                var header = worksheet.Cell(
                    headerRow,
                    col).GetString().Trim();

                if (header.Equals(
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return col;
                }
            }

            return 0;
        }

        private static string GetCellStringValue(IXLCell cell)
        {
            return cell.DataType switch
            {
                XLDataType.Number => cell.GetDouble()
                    .ToString("G", CultureInfo.InvariantCulture),

                XLDataType.DateTime => cell.GetDateTime()
                    .ToString("O", CultureInfo.InvariantCulture),

                XLDataType.Boolean => cell.GetBoolean().ToString(),

                XLDataType.TimeSpan => cell.GetTimeSpan()
                    .ToString(),

                _ => cell.GetString().Trim()
            };
        }

        private static string GetColumnKey(
            string sheetName,
            string columnName)
        {
            return $"{sheetName}.{columnName}";
        }

        private static bool LooksLikeIdentifier(string columnName)
        {
            var name = columnName
                .Replace("_", "")
                .Replace("-", "")
                .ToLowerInvariant();

            return name.EndsWith("id") ||
                   name.EndsWith("code") ||
                   name.EndsWith("number") ||
                   name.EndsWith("no");
        }

        // ---------------------------------------------------------
        // INTERNAL CLASSES
        // ---------------------------------------------------------

        private class ColumnProfile
        {
            public ColumnMetadata Metadata { get; set; } = new();

            public HashSet<string>? UniqueValues { get; set; }
        }

        private class ColumnTypeStats
        {
            public bool HasText { get; set; }

            public bool HasInteger { get; set; }

            public bool HasDecimal { get; set; }

            public bool HasDateTime { get; set; }

            public bool HasBoolean { get; set; }

            public bool HasTimeSpan { get; set; }

            public bool HasOther { get; set; }
        }
    }
}
