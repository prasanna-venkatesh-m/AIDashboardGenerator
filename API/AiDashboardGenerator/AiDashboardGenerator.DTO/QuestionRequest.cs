using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.DTO
{
    public class QuestionRequest
    {
        public string UserId { get; set; } = "";
        public string ChatId { get; set; } = "";
        public string Query { get; set; } = "";
    }
}
