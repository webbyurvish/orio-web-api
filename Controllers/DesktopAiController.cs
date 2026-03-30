using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/desktop/ai")]
[Authorize]
public class DesktopAiController : ControllerBase
{
    private const int MaxScreenshotBase64Length = 12_000_000;

    private readonly IConfiguration _configuration;
    private readonly ILogger<DesktopAiController> _logger;
    private readonly ComputerVisionOcrService _computerVisionOcr;

    public DesktopAiController(
        IConfiguration configuration,
        ILogger<DesktopAiController> logger,
        ComputerVisionOcrService computerVisionOcr)
    {
        _configuration = configuration;
        _logger = logger;
        _computerVisionOcr = computerVisionOcr;
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

    /// <summary>
    /// Same as <see cref="Answer"/> but streams model output as NDJSON lines: <c>{"d":"token chunk"}</c>.
    /// On failure after headers are sent, writes a line <c>{"error":"..."}</c>.
    /// </summary>
    [HttpPost("answer-stream")]
    public async Task AnswerStream([FromBody] DesktopAiAnswerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserContent))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "userContent is required." }, cancellationToken);
            return;
        }

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        var defaultSystemPrompt = _configuration["AzureOpenAI:SystemPrompt"] ?? "You are a helpful AI assistant.";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Desktop AI stream rejected: AzureOpenAI config missing.");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(new { message = "AI is not configured on the server." }, cancellationToken);
            return;
        }

        var basePrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? defaultSystemPrompt
            : request.SystemPrompt.Trim();

        basePrompt +=
            "\n\nYou are role-playing as the job candidate described in the resume. " +
            "Always answer in FIRST PERSON as that candidate. " +
            "Never mention that you are an AI, assistant, or language model.";

        var options = new ChatCompletionsOptions { DeploymentName = deploymentName };
        options.Messages.Add(new ChatRequestSystemMessage(basePrompt));

        if (!string.IsNullOrWhiteSpace(request.ResumeContext))
        {
            options.Messages.Add(new ChatRequestSystemMessage(
                "Here is the candidate's resume. Use this as context when answering:\n\n" + request.ResumeContext));
        }

        options.Messages.Add(new ChatRequestUserMessage(request.UserContent.Trim()));

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        OpenAIClient client;
        try
        {
            client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI client init failed for stream");
            var errLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonErrorLine { Error = ex.Message });
            await Response.WriteAsync(errLine + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        try
        {
            var streamingChatCompletions = await client.GetChatCompletionsStreamingAsync(options, cancellationToken)
                .ConfigureAwait(false);

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;

                var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = piece });
                await Response.WriteAsync(line + "\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Desktop AI stream failed during generation");
            var errLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonErrorLine { Error = ex.Message });
            await Response.WriteAsync(errLine + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// OCR via Azure Computer Vision (Read), then streams an interview-style answer via Azure OpenAI (same persona as <see cref="AnswerStream"/>).
    /// </summary>
    [HttpPost("screenshot-answer-stream")]
    public async Task ScreenshotAnswerStream([FromBody] DesktopScreenshotAnswerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "imageBase64 is required." }, cancellationToken);
            return;
        }

        if (request.ImageBase64.Length > MaxScreenshotBase64Length)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "Image payload is too large." }, cancellationToken);
            return;
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(request.ImageBase64.Trim());
        }
        catch (FormatException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "imageBase64 is not valid Base64." }, cancellationToken);
            return;
        }

        if (imageBytes.Length > 8_000_000)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { message = "Decoded image is too large (max ~8 MB)." }, cancellationToken);
            return;
        }

        string ocrText;
        try
        {
            var mime = string.IsNullOrWhiteSpace(request.MimeType) ? "image/png" : request.MimeType.Trim();
            ocrText = await _computerVisionOcr.ReadTextFromImageAsync(imageBytes, mime, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Screenshot OCR failed");
            Response.StatusCode = StatusCodes.Status502BadGateway;
            await Response.WriteAsJsonAsync(new { message = ex.Message }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new
            {
                message = "No readable text was found in the screenshot. Try zooming the question, using a higher-contrast view, or capturing a smaller region next time."
            }, cancellationToken);
            return;
        }

        var userContent =
            "The following text was extracted from a screenshot of the user's screen (interview, IDE, browser, shared document, etc.) using OCR. Spelling or line breaks may be imperfect.\n\n" +
            "---\n" +
            ocrText.Trim() +
            "\n---\n\n" +
            "1) Decide whether there is an interview question, coding problem, multiple-choice question, system-design prompt, or similar that the candidate should answer.\n" +
            "2) If yes: briefly restate the question, then give a concise, natural, interview-ready answer in FIRST PERSON as the candidate. For coding questions: provide a correct solution and a short explanation.\n" +
            "3) If there is no clear question: explain briefly what the text seems to be and that you did not find an answerable interview question.\n" +
            "Keep the response easy to read aloud.";

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        var defaultSystemPrompt = _configuration["AzureOpenAI:SystemPrompt"] ?? "You are a helpful AI assistant.";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Screenshot AI stream rejected: AzureOpenAI config missing.");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(new { message = "AI is not configured on the server." }, cancellationToken);
            return;
        }

        var basePrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? defaultSystemPrompt
            : request.SystemPrompt.Trim();

        basePrompt +=
            "\n\nYou are role-playing as the job candidate described in the resume. " +
            "Always answer in FIRST PERSON as that candidate. " +
            "Never mention that you are an AI, assistant, or language model.";

        var options = new ChatCompletionsOptions { DeploymentName = deploymentName };
        options.Messages.Add(new ChatRequestSystemMessage(basePrompt));

        if (!string.IsNullOrWhiteSpace(request.ResumeContext))
        {
            options.Messages.Add(new ChatRequestSystemMessage(
                "Here is the candidate's resume. Use this as context when answering:\n\n" + request.ResumeContext));
        }

        options.Messages.Add(new ChatRequestUserMessage(userContent));

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        OpenAIClient client;
        try
        {
            client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI client init failed for screenshot stream");
            var errLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonErrorLine { Error = ex.Message });
            await Response.WriteAsync(errLine + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        try
        {
            var streamingChatCompletions = await client.GetChatCompletionsStreamingAsync(options, cancellationToken)
                .ConfigureAwait(false);

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;

                var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = piece });
                await Response.WriteAsync(line + "\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot AI stream failed during generation");
            var errLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonErrorLine { Error = ex.Message });
            await Response.WriteAsync(errLine + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}

public sealed class DesktopAiAnswerRequest
{
    public string? UserContent { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ResumeContext { get; set; }
}

public sealed class DesktopScreenshotAnswerRequest
{
    /// <summary>Base64-encoded image (PNG or JPEG recommended).</summary>
    public string? ImageBase64 { get; set; }

    /// <summary>Optional MIME type, e.g. image/png or image/jpeg.</summary>
    public string? MimeType { get; set; }

    public string? SystemPrompt { get; set; }
    public string? ResumeContext { get; set; }
}

public sealed class DesktopAiAnswerResponse
{
    public string Answer { get; set; } = string.Empty;
}

internal sealed class NdjsonDeltaLine
{
    [JsonPropertyName("d")]
    public string D { get; set; } = string.Empty;
}

internal sealed class NdjsonErrorLine
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}
