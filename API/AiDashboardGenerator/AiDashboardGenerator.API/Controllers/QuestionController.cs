using AiDashboardGenerator.DTO;
using AiDashboardGenerator.Services.Interface;
using AiDashboardGenerator.Services.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiDashboardGenerator.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuestionController : ControllerBase
    {
        private readonly IQuestionService questionService;
        public QuestionController(IQuestionService _questionService)
        {
            questionService = _questionService;
        }
        [HttpPost]
        public async Task<IActionResult> Ask(QuestionRequest request)
        {
            var prompt = await questionService.PreparePromptAsync(request);

            return Ok(new { Prompt = prompt });
        }
    }
}
