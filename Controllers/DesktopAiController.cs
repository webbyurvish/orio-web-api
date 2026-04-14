using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/desktop/ai")]
[Authorize]
public class DesktopAiController : ControllerBase
{
    private const int MaxScreenshotBase64Length = 12_000_000;
    private const int MaxStreamTokenChars = 24;

    private readonly IConfiguration _configuration;
    private readonly ILogger<DesktopAiController> _logger;
    private readonly ComputerVisionOcrService _computerVisionOcr;
    private readonly AppDbContext _db;

    public DesktopAiController(
        IConfiguration configuration,
        ILogger<DesktopAiController> logger,
        ComputerVisionOcrService computerVisionOcr,
        AppDbContext db)
    {
        _configuration = configuration;
        _logger = logger;
        _computerVisionOcr = computerVisionOcr;
        _db = db;
    }

    private static decimal EstimateCostUsd(int? promptTokens, int? completionTokens)
    {
        var n = (promptTokens ?? 0) + (completionTokens ?? 0);
        return n * 0.00000015m;
    }

    /// <summary>
    /// Breaks a model "chunk" into smaller deltas for smooth desktop streaming.
    /// We preserve whitespace and punctuation and try to emit "word + trailing spaces" units.
    /// If a single token is very long (e.g., code, URLs), it is further split to avoid big bursts.
    /// </summary>
    private static IEnumerable<string> SplitForStreaming(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            yield break;

        // Tokenize into runs of whitespace and non-whitespace.
        var i = 0;
        while (i < chunk.Length)
        {
            var start = i;
            var isWs = char.IsWhiteSpace(chunk[i]);
            i++;
            while (i < chunk.Length && char.IsWhiteSpace(chunk[i]) == isWs)
                i++;
            var token = chunk[start..i];

            if (token.Length <= MaxStreamTokenChars)
            {
                yield return token;
                continue;
            }

            // Split long non-whitespace tokens (code identifiers, long words, base64-ish strings, etc.)
            // We still preserve the original ordering and content.
            for (var j = 0; j < token.Length; j += MaxStreamTokenChars)
            {
                var len = Math.Min(MaxStreamTokenChars, token.Length - j);
                yield return token.Substring(j, len);
            }
        }
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

        var sw = Stopwatch.StartNew();
        Response<ChatCompletions> responseWrap;
        try
        {
            responseWrap = await client.GetChatCompletionsAsync(options);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await TryLogAiUsageAsync(deploymentName, sw.ElapsedMilliseconds, null, null, null, false, ex.Message);
            throw;
        }

        sw.Stop();
        var completion = responseWrap.Value;
        var answer = completion.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var usage = completion.Usage;
        await TryLogAiUsageAsync(
            deploymentName,
            sw.ElapsedMilliseconds,
            usage?.PromptTokens,
            usage?.CompletionTokens,
            usage?.TotalTokens,
            true,
            null);

        return Ok(new DesktopAiAnswerResponse { Answer = answer });
    }

    /// <summary>
    /// One fast non-streaming call to normalize noisy speech transcript into a clear latest question
    /// for UI heading + answer prompt (desktop manual/auto answer).
    /// </summary>
    [HttpPost("clarify-transcript-question")]
    public async Task<ActionResult<DesktopClarifyTranscriptQuestionResponse>> ClarifyTranscriptQuestion(
        [FromBody] DesktopClarifyTranscriptQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var raw = (request.Transcript ?? string.Empty).Trim();
        if (raw.Length == 0)
            return BadRequest(new { message = "transcript is required." });

        const int maxLen = 12_000;
        if (raw.Length > maxLen)
            raw = raw[^maxLen..].TrimStart();

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Clarify transcript rejected: AzureOpenAI config missing.");
            return StatusCode(500, new { message = "AI is not configured on the server." });
        }

        const string clarifySystem =
            "You clean job-interview speech transcripts (Azure / live captions). The text may have ASR errors, filler (um, uh), " +
            "duplicated fragments, or stray question marks that split one thought into pieces.\n\n" +
            "Find what the candidate should answer **now** — prioritize the **most recent** substantive question or task " +
            "(from the interviewer or, if the candidate is thinking aloud, from their mic).\n\n" +
            "**Critical:** HEADING and BODY must contain **only** the actual interview question (or questions). " +
            "**Remove** conversational openers and chit-chat from the transcript, including: Hi, Hello, Hey, Good morning/afternoon, " +
            "Thanks, Thank you, Okay/OK, Alright, Um/Uh (when they are only padding before the question). " +
            "Example: transcript \"Hi what is React?\" → HEADING: What is React? — **not** \"Hi what is React?\".\n\n" +
            "Reply in **exactly** this plain-text format (no markdown, no code fences):\n" +
            "Line 1 must begin with HEADING: followed by a space, then one concise rephrasing of the latest/main question (about 220 characters or less).\n" +
            "Line 2 must begin with BODY: followed by a space, then the cleaned text for the answering model. " +
            "If there is only one question, BODY must use the **same** cleaned wording as HEADING (no greetings). " +
            "If several distinct questions appear, use numbered lines inside BODY (1. 2. 3.), each line without greetings.\n\n" +
            "Do not role-play as the candidate. Do not answer the question — output only those two lines.";

        var userSb = new System.Text.StringBuilder();
        userSb.Append("Transcript (may be noisy):\n---\n");
        userSb.Append(raw);
        userSb.Append("\n---\n");
        var ctx = (request.UserContext ?? string.Empty).Trim();
        if (ctx.Length > 0)
        {
            userSb.Append("Optional typed context from the candidate (not part of ASR):\n---\n");
            userSb.Append(ctx.Length > 2000 ? ctx[..2000] : ctx);
            userSb.Append("\n---\n");
        }

        var client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        var options = new ChatCompletionsOptions { DeploymentName = deploymentName, MaxTokens = 400, Temperature = 0.2f };
        options.Messages.Add(new ChatRequestSystemMessage(clarifySystem));
        options.Messages.Add(new ChatRequestUserMessage(userSb.ToString()));

        var sw = Stopwatch.StartNew();
        Response<ChatCompletions> responseWrap;
        try
        {
            responseWrap = await client.GetChatCompletionsAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await TryLogAiUsageAsync(deploymentName, sw.ElapsedMilliseconds, null, null, null, false, ex.Message);
            throw;
        }

        sw.Stop();
        var completion = responseWrap.Value;
        var text = completion.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var usage = completion.Usage;
        await TryLogAiUsageAsync(
            deploymentName,
            sw.ElapsedMilliseconds,
            usage?.PromptTokens,
            usage?.CompletionTokens,
            usage?.TotalTokens,
            true,
            null);

        if (!TryParseHeadingBody(text, out var heading, out var body) || string.IsNullOrWhiteSpace(heading))
            return UnprocessableEntity(new { message = "Model did not return a parseable HEADING:/BODY: block." });

        heading = heading.Trim();
        body = string.IsNullOrWhiteSpace(body) ? heading : body.Trim();

        heading = NormalizeClarifiedHeadingOrLine(heading);
        body = NormalizeClarifiedBody(body);

        if (heading.Length > 500)
            heading = heading[..500].TrimEnd() + "…";
        if (body.Length > 4000)
            body = body[..4000].TrimEnd() + "…";

        return Ok(new DesktopClarifyTranscriptQuestionResponse { Heading = heading, Body = body });
    }

    /// <summary>Strip greetings / filler the model may have left on a single question line.</summary>
    private static string NormalizeClarifiedHeadingOrLine(string line)
    {
        var t = StripConversationalLeadInsFromStart(line);
        t = EnsureLeadingLetterUppercase(t);
        return t.Trim();
    }

    private static string NormalizeClarifiedBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;
            var numbered = Regex.Match(line, @"^(\d+\.\s*)");
            if (numbered.Success)
            {
                var prefix = numbered.Groups[1].Value;
                var rest = line[prefix.Length..];
                rest = StripConversationalLeadInsFromStart(rest);
                rest = EnsureLeadingLetterUppercase(rest);
                lines[i] = prefix + rest;
            }
            else
            {
                var s = StripConversationalLeadInsFromStart(line);
                lines[i] = EnsureLeadingLetterUppercase(s);
            }
        }

        return string.Join("\n", lines).Trim();
    }

    private static readonly Regex LeadInNoiseRegex = new(
        @"^(?:(?:hi|hello|hey|hiya|yo)\b[,!.]?\s*|(?:good\s+(?:morning|afternoon|evening))\b[,!.]?\s*|" +
        @"(?:thanks?|thank\s+you)\b[,!.]?\s*|(?:ok+|okay|alright)\b[,!.]?\s*|(?:um+|uh+)\b[,!.]?\s*)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string StripConversationalLeadInsFromStart(string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            return t;
        string prev;
        do
        {
            prev = t;
            t = LeadInNoiseRegex.Replace(t, "").TrimStart();
        } while (!string.Equals(prev, t, StringComparison.Ordinal));

        return t.Trim();
    }

    private static string EnsureLeadingLetterUppercase(string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            return t;
        var i = 0;
        while (i < t.Length && !char.IsLetter(t[i]))
            i++;
        if (i >= t.Length)
            return t;
        if (!char.IsLower(t[i]))
            return t;
        return t[..i] + char.ToUpperInvariant(t[i]) + t[(i + 1)..];
    }

    private static bool TryParseHeadingBody(string raw, out string heading, out string body)
    {
        heading = string.Empty;
        body = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var hMarker = "HEADING:";
        var bMarker = "BODY:";
        var hIdx = raw.IndexOf(hMarker, StringComparison.OrdinalIgnoreCase);
        if (hIdx < 0)
            return false;

        var hStart = hIdx + hMarker.Length;
        var bIdx = raw.IndexOf(bMarker, hStart, StringComparison.OrdinalIgnoreCase);
        var hEnd = bIdx >= 0 ? bIdx : raw.Length;
        heading = raw[hStart..hEnd].Trim();
        if (bIdx >= 0)
            body = raw[(bIdx + bMarker.Length)..].Trim();

        if (string.IsNullOrWhiteSpace(heading))
            return false;
        if (string.IsNullOrWhiteSpace(body))
            body = heading;
        return true;
    }

    private async Task TryLogAiUsageAsync(
        string deploymentName,
        long elapsedMs,
        int? promptTokens,
        int? completionTokens,
        int? totalTokens,
        bool success,
        string? errorMessage)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return;

        var err = errorMessage == null ? null : errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        _db.AiUsageLogs.Add(new AiUsageLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CallSessionId = null,
            DeploymentName = deploymentName.Length > 100 ? deploymentName[..100] : deploymentName,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            LatencyMs = (int)Math.Min(elapsedMs, int.MaxValue),
            Success = success,
            ErrorMessage = err,
            EstimatedCostUsd = success ? EstimateCostUsd(promptTokens, completionTokens) : 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Same as <see cref="Answer"/> but streams model output as NDJSON lines: <c>{"d":"token chunk"}</c>.
    /// On failure after headers are sent, writes a line <c>{"error":"..."}</c>.
    /// </summary>
    [HttpPost("answer-stream")]
    public async Task AnswerStream([FromBody] DesktopAiAnswerRequest request, CancellationToken cancellationToken)
    {
        var streamDebug = _configuration.GetValue<bool>("DesktopAi:StreamDebugLogs");

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

            var sw = streamDebug ? Stopwatch.StartNew() : null;
            long modelChunks = 0;
            long modelChunkChars = 0;
            long emittedDeltas = 0;
            long emittedChars = 0;
            var lastLogMs = 0L;

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;

                modelChunks++;
                modelChunkChars += piece.Length;

                foreach (var small in SplitForStreaming(piece))
                {
                    if (string.IsNullOrEmpty(small))
                        continue;
                    var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = small });
                    await Response.WriteAsync(line + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    emittedDeltas++;
                    emittedChars += small.Length;
                }

                if (streamDebug && sw != null)
                {
                    var ms = (long)sw.Elapsed.TotalMilliseconds;
                    if (ms - lastLogMs >= 500)
                    {
                        lastLogMs = ms;
                        _logger.LogInformation(
                            "[STREAM:SERVER] t={Ms}ms chunks={Chunks} chunkChars={ChunkChars} emitted={Emitted} emittedChars={EmittedChars} lastChunkLen={LastChunkLen}",
                            ms, modelChunks, modelChunkChars, emittedDeltas, emittedChars, piece.Length);
                    }
                }
            }

            if (streamDebug && sw != null)
            {
                sw.Stop();
                _logger.LogInformation(
                    "[STREAM:SERVER] end t={Ms}ms chunks={Chunks} chunkChars={ChunkChars} emitted={Emitted} emittedChars={EmittedChars}",
                    (long)sw.Elapsed.TotalMilliseconds, modelChunks, modelChunkChars, emittedDeltas, emittedChars);
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
        var streamDebug = _configuration.GetValue<bool>("DesktopAi:StreamDebugLogs");

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

            var sw = streamDebug ? Stopwatch.StartNew() : null;
            long modelChunks = 0;
            long modelChunkChars = 0;
            long emittedDeltas = 0;
            long emittedChars = 0;
            var lastLogMs = 0L;

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;

                modelChunks++;
                modelChunkChars += piece.Length;

                foreach (var small in SplitForStreaming(piece))
                {
                    if (string.IsNullOrEmpty(small))
                        continue;
                    var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = small });
                    await Response.WriteAsync(line + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    emittedDeltas++;
                    emittedChars += small.Length;
                }

                if (streamDebug && sw != null)
                {
                    var ms = (long)sw.Elapsed.TotalMilliseconds;
                    if (ms - lastLogMs >= 500)
                    {
                        lastLogMs = ms;
                        _logger.LogInformation(
                            "[STREAM:SERVER:SHOT] t={Ms}ms chunks={Chunks} chunkChars={ChunkChars} emitted={Emitted} emittedChars={EmittedChars} lastChunkLen={LastChunkLen}",
                            ms, modelChunks, modelChunkChars, emittedDeltas, emittedChars, piece.Length);
                    }
                }
            }

            if (streamDebug && sw != null)
            {
                sw.Stop();
                _logger.LogInformation(
                    "[STREAM:SERVER:SHOT] end t={Ms}ms chunks={Chunks} chunkChars={ChunkChars} emitted={Emitted} emittedChars={EmittedChars}",
                    (long)sw.Elapsed.TotalMilliseconds, modelChunks, modelChunkChars, emittedDeltas, emittedChars);
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

public sealed class DesktopClarifyTranscriptQuestionRequest
{
    public string? Transcript { get; set; }
    public string? UserContext { get; set; }
}

public sealed class DesktopClarifyTranscriptQuestionResponse
{
    public string Heading { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
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
