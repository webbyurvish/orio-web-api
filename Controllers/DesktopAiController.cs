using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/desktop/ai")]
[Authorize]
public class DesktopAiController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DesktopAiController> _logger;

    public DesktopAiController(IConfiguration configuration, ILogger<DesktopAiController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("answer")]
    public async Task<ActionResult<DesktopAiAnswerResponse>> Answer([FromBody] DesktopAiAnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserContent))
            return BadRequest(new { message = "userContent is required." });

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        var defaultSystemPrompt = _configuration["AzureOpenAI:SystemPrompt"] ?? "You are a helpful AI assistant.";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Desktop AI request rejected: AzureOpenAI config missing.");
            return StatusCode(500, new { message = "AI is not configured on the server." });
        }

        var basePrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? defaultSystemPrompt
            : request.SystemPrompt.Trim();

        // Keep behavior aligned with desktop expectations (answer as candidate persona).
        basePrompt +=
            "\n\nYou are role-playing as the job candidate described in the resume. " +
            "Always answer in FIRST PERSON as that candidate. " +
            "Never mention that you are an AI, assistant, or language model.";

        var client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        var options = new ChatCompletionsOptions { DeploymentName = deploymentName };
        options.Messages.Add(new ChatRequestSystemMessage(basePrompt));

        if (!string.IsNullOrWhiteSpace(request.ResumeContext))
        {
            options.Messages.Add(new ChatRequestSystemMessage(
                "Here is the candidate's resume. Use this as context when answering:\n\n" + request.ResumeContext));
        }

        options.Messages.Add(new ChatRequestUserMessage(request.UserContent.Trim()));

        var response = await client.GetChatCompletionsAsync(options);
        var answer = response.Value.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;

        return Ok(new DesktopAiAnswerResponse { Answer = answer });
    }
}

public sealed class DesktopAiAnswerRequest
{
    public string? UserContent { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ResumeContext { get; set; }
}

public sealed class DesktopAiAnswerResponse
{
    public string Answer { get; set; } = string.Empty;
}

