using AiDashboardGenerator.DTO;
using AiDashboardGenerator.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Services.Interface
{
    public interface IQuestionService
    {
        Task<QuestionResult> PreparePromptAsync(QuestionRequest request);
    }
}
