using AiDashboardGenerator.Services.Interface;
using AiDashboardGenerator.Services.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AiDashboardGenerator.Services
{
    public class ServiceModule
    {
        public ServiceModule(IServiceCollection services)
        {
            services.AddScoped<IFileService, FileService>();
            services.AddScoped<IBlobService, BlobService>();
            services.AddHttpClient<IGroqService, GroqService>();

            //services.AddHttpClient<ISqlGenerationService, GroqSqlGenerationService>();
            //services.AddHttpClient<ISqlGenerationService, OpenAiSqlGenerationService>();
            services.AddHttpClient<ISqlGenerationService, LmStudioSqlGenerationService>();
            services.AddHttpClient<ILmStudioService, LmStudioService>();

            services.AddScoped<IQuestionService, QuestionService>();
            services.AddScoped<IExcelMetadataService, ExcelMetadataService>();
            services.AddScoped<IExcelQueryService, ExcelQueryService>();
            services.AddScoped<IDuckDbQueryService, DuckDbQueryService>();
        }
    }
}
