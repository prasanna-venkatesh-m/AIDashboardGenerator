using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Models
{

    public class FileMetadata
    {
        public string ChatId { get; set; } = "";
        public string FileId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string BlobName { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        public List<SheetMetadata> Sheets { get; set; } = [];
    }

    public class SheetMetadata
    {
        public string SheetName { get; set; } = "";
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public List<ColumnMetadata> Columns { get; set; } = [];
    }

    public class ColumnMetadata
    {
        public string ColumnName { get; set; } = "";

        public string DataType { get; set; } = "";

        public int NullCount { get; set; }

        public int UniqueCount { get; set; }

        public int DuplicateCount { get; set; }

        public int NonEmptyCount { get; set; }

        public double UniquePercentage { get; set; }

        public bool IsPossiblePK { get; set; }

        public bool IsPossibleFK { get; set; }

        public bool IsCategorical { get; set; }

        public bool IsIdentifier { get; set; }

        public List<string> SampleValues { get; set; } = [];

        public Dictionary<string, int> ValueCounts { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
