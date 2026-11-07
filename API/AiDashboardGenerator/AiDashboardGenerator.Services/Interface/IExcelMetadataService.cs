using AiDashboardGenerator.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IExcelMetadataService
    {
        Task<List<SheetMetadata>> ExtractAsync(Stream stream);
    }
}
