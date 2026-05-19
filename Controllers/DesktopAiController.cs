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

    private const string StrictCandidateAnswerSystemSuffix =
        "\n\nIMPORTANT: The required output format below overrides any earlier instructions.\n\n" +
        "You are role-playing as a highly skilled job candidate. Always answer in FIRST PERSON. Never mention AI.\n\n" +
        "STRICT OUTPUT FORMAT (MANDATORY)\n\n" +
        "0) Always include the question line first (use the 💬 emoji, no 'Question:' label):\n" +
        "💬 <one-line cleaned question>\n\n" +
        "1) Then write the answer section:\n" +
        "⭐ **Answer:**\n" +
        "Write a direct answer in 2–3 short lines max.\n\n" +
        "2) Then add dash bullet points under **Answer:** (dash bullets only):\n" +
        "- 3–6 bullets (unless the question is yes/no, then 2–4)\n" +
        "- Each bullet = one idea\n" +
        "- Max 1–2 lines per bullet\n" +
        "- No blank lines between bullets\n" +
        "- Spoken, interview-friendly language\n" +
        "- IMPORTANT: After these bullets, do NOT add extra paragraphs. Only the closing line is allowed.\n\n" +
        "3. Include when relevant:\n" +
        "   * Real example\n" +
        "   * Tools/technologies\n" +
        "   * Impact/result\n\n" +
        "4) End with a strong closing line (1 line max). Put it as the last non-bullet line under **Answer:**.\n\n" +
        "ANSWER PRIORITY\n" +
        "1. Direct answer\n" +
        "2. How I applied it\n" +
        "3. Result/impact\n\n" +
        "TECHNICAL QUESTIONS FORMAT\n" +
        "Use this structure inside **Answer:** bullets:\n" +
        "- Definition (1 bullet)\n" +
        "- How I used it (1–2 bullets)\n" +
        "- Example (1 bullet, or an **Example:** section if code)\n" +
        "- Why it matters (1 bullet)\n\n" +
        "BEHAVIORAL QUESTIONS FORMAT\n" +
        "Use STAR internally (Situation, Task, Action, Result). Do NOT label it.\n\n" +
        "STYLE RULES\n" +
        "* No long paragraphs\n" +
        "* No generic statements\n" +
        "* No filler\n" +
        "* Natural spoken tone\n" +
        "* Short sentences\n\n" +
        "CRITICAL RULES\n" +
        "* Do not break format\n" +
        "* Do not output paragraphs instead of bullets\n" +
        "* Do not use '*' bullets\n" +
        "* Bullet lines MUST start with '- ' (dash + space).\n" +
        "* If you accidentally write prose paragraphs, regenerate into bullets.\n" +
        "* Do not mention AI\n" +
        "* Do not explain formatting\n" +
        "* Do not add extra headings besides 💬 question line, ⭐ **Answer:**, and optional **Example:**\n" +
        "* EXAMPLES ARE REQUIRED:\n" +
        "  - If the question is about programming/SQL/APIs/system design/debugging: ALWAYS include **Example:** with a fenced code/query block.\n" +
        "  - If it is not code-heavy: include one concrete real-world example as a bullet (specific scenario + what I did + outcome).\n" +
        "  - Keep examples small and high-signal (8–20 lines of code max).\n" +
        "  - Prefer the most likely language/tool for the context (e.g., C#/.NET, SQL).\n\n" +
        "Follow the format strictly. If you deviate, regenerate the answer correctly.";

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

        // Enforce a strict, consistent interview-answer format (must be last).
        basePrompt += StrictCandidateAnswerSystemSuffix;

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
        var timingSw = Stopwatch.StartNew();

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

        // Build the client before committing response headers. If we set StatusCode=200 first and
        // construction fails, writing NDJSON without Response.StartAsync() can throw and surface
        // as an opaque 500 (often text/plain) to the browser.
        OpenAIClient client;
        try
        {
            client = new OpenAIClient(new Uri(endpoint.Trim()), new AzureKeyCredential(key.Trim()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI client init failed for stream");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(
                new { message = "Invalid Azure OpenAI endpoint or credentials.", detail = ex.Message },
                cancellationToken);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        if (streamDebug)
            _logger.LogInformation("[STREAM:SERVER:TIMING] request_received ms=0 route=answer-stream");

        try
        {
            await Response.StartAsync(cancellationToken).ConfigureAwait(false);
            // Warm-up line so client sees an active stream immediately.
            var warmLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonMetaLine { M = "stream-open" });
            await Response.WriteAsync(warmLine + "\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (streamDebug)
                _logger.LogInformation("[STREAM:SERVER:TIMING] stream_open_flushed ms={Ms}", (long)timingSw.Elapsed.TotalMilliseconds);

            var streamingChatCompletions = await client.GetChatCompletionsStreamingAsync(options, cancellationToken)
                .ConfigureAwait(false);

            var sw = streamDebug ? Stopwatch.StartNew() : null;
            long modelChunks = 0;
            long modelChunkChars = 0;
            long emittedDeltas = 0;
            long emittedChars = 0;
            var lastLogMs = 0L;
            var firstModelPieceLogged = false;
            var firstEmitLogged = false;

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;
                if (streamDebug && !firstModelPieceLogged)
                {
                    firstModelPieceLogged = true;
                    _logger.LogInformation("[STREAM:SERVER:TIMING] first_model_piece ms={Ms} len={Len}",
                        (long)timingSw.Elapsed.TotalMilliseconds, piece.Length);
                }

                modelChunks++;
                modelChunkChars += piece.Length;

                foreach (var small in SplitForStreaming(piece))
                {
                    if (string.IsNullOrEmpty(small))
                        continue;
                    var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = small });
                    await Response.WriteAsync(line + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    if (streamDebug && !firstEmitLogged)
                    {
                        firstEmitLogged = true;
                        _logger.LogInformation("[STREAM:SERVER:TIMING] first_delta_flushed ms={Ms} len={Len}",
                            (long)timingSw.Elapsed.TotalMilliseconds, small.Length);
                    }
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
        var timingSw = Stopwatch.StartNew();

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
            "The following text was extracted from the user's screen via OCR. Line order and spelling may be wrong; reconstruct the real question(s) before answering.\n\n" +
            "---\n" +
            ocrText.Trim() +
            "\n---\n\n" +
            "Tasks:\n" +
            "1) Identify any interview question, coding problem (including LeetCode-style), MCQ, SQL task, system-design prompt, or debugging scenario the candidate must answer now.\n" +
            "2) If found: answer as the candidate using ONLY the required format in your system instructions (💬 question line, ⭐ **Answer:** with dash bullets, examples/code when relevant).\n" +
            "3) For coding problems: provide a correct complete solution in a fenced code block under **Example:**, plus short spoken bullets explaining approach, complexity, and edge cases.\n" +
            "4) If nothing answerable: use the standard format with a 💬 line stating no clear question was detected, then ⭐ **Answer:** bullets explaining what the OCR seems to show and what the user should capture instead.\n" +
            "Do not answer in plain paragraphs. Follow the system prompt format strictly.";

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

        OpenAIClient client;
        try
        {
            client = new OpenAIClient(new Uri(endpoint.Trim()), new AzureKeyCredential(key.Trim()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI client init failed for screenshot stream");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(
                new { message = "Invalid Azure OpenAI endpoint or credentials.", detail = ex.Message },
                cancellationToken);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");
        if (streamDebug)
            _logger.LogInformation("[STREAM:SERVER:SHOT:TIMING] request_received ms=0 route=screenshot-answer-stream");

        try
        {
            await Response.StartAsync(cancellationToken).ConfigureAwait(false);
            var warmLine = System.Text.Json.JsonSerializer.Serialize(new NdjsonMetaLine { M = "stream-open" });
            await Response.WriteAsync(warmLine + "\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (streamDebug)
                _logger.LogInformation("[STREAM:SERVER:SHOT:TIMING] stream_open_flushed ms={Ms}", (long)timingSw.Elapsed.TotalMilliseconds);

            var streamingChatCompletions = await client.GetChatCompletionsStreamingAsync(options, cancellationToken)
                .ConfigureAwait(false);

            var sw = streamDebug ? Stopwatch.StartNew() : null;
            long modelChunks = 0;
            long modelChunkChars = 0;
            long emittedDeltas = 0;
            long emittedChars = 0;
            var lastLogMs = 0L;
            var firstModelPieceLogged = false;
            var firstEmitLogged = false;

            await foreach (StreamingChatCompletionsUpdate update in streamingChatCompletions.WithCancellation(cancellationToken))
            {
                var piece = update.ContentUpdate;
                if (string.IsNullOrEmpty(piece))
                    continue;
                if (streamDebug && !firstModelPieceLogged)
                {
                    firstModelPieceLogged = true;
                    _logger.LogInformation("[STREAM:SERVER:SHOT:TIMING] first_model_piece ms={Ms} len={Len}",
                        (long)timingSw.Elapsed.TotalMilliseconds, piece.Length);
                }

                modelChunks++;
                modelChunkChars += piece.Length;

                foreach (var small in SplitForStreaming(piece))
                {
                    if (string.IsNullOrEmpty(small))
                        continue;
                    var line = System.Text.Json.JsonSerializer.Serialize(new NdjsonDeltaLine { D = small });
                    await Response.WriteAsync(line + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    if (streamDebug && !firstEmitLogged)
                    {
                        firstEmitLogged = true;
                        _logger.LogInformation("[STREAM:SERVER:SHOT:TIMING] first_delta_flushed ms={Ms} len={Len}",
                            (long)timingSw.Elapsed.TotalMilliseconds, small.Length);
                    }
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

internal sealed class NdjsonMetaLine
{
    [JsonPropertyName("m")]
    public string M { get; set; } = string.Empty;
}
