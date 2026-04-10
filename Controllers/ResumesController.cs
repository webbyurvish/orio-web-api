using System.Security.Claims;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Models;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ResumesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly AppDbContext _db;
    private readonly ResumeTextExtractor _textExtractor;
    private readonly ResumeStructuredParsingService _parsingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResumesController> _logger;

    public ResumesController(
        AppDbContext db,
        ResumeTextExtractor textExtractor,
        ResumeStructuredParsingService parsingService,
        IConfiguration configuration,
        ILogger<ResumesController> logger)
    {
        _db = db;
        _textExtractor = textExtractor;
        _parsingService = parsingService;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid? GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId) ? null : userId;
    }

    /// <summary>Upload PDF/DOCX, extract text, parse with Azure OpenAI, persist structured JSON.</summary>
    [HttpPost("parse-upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ResumeDetailDto), 201)]
    public async Task<ActionResult<ResumeDetailDto>> ParseUpload([FromForm] string? title, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = Request.Form.Files["file"];
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please select a file." });

        var uploadedFile = file!;
        var fileName = uploadedFile.FileName ?? "resume.pdf";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx"))
            return BadRequest(new { message = "Only PDF and DOCX files are supported." });

        var titleTrimmed = (title ?? fileName ?? "My Resume").Trim();
        if (string.IsNullOrEmpty(titleTrimmed)) titleTrimmed = "My Resume";

        await using var ms = new MemoryStream();
        await uploadedFile.CopyToAsync(ms, ct);
        var content = ms.ToArray();

        string plainText;
        try
        {
            plainText = _textExtractor.ExtractPlainText(content, fileName ?? "resume.pdf", ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        ResumeStructuredDocument structured;
        try
        {
            structured = await _parsingService.ParseResumeTextAsync(plainText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume parse AI error");
            return StatusCode(502, new { message = "Resume parsing failed. Try again or use a clearer PDF/DOCX." });
        }

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Title = titleTrimmed.Length > 500 ? titleTrimmed[..500] : titleTrimmed,
            FileName = fileName.Length > 500 ? fileName[..500] : fileName,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StructuredDataJson = JsonSerializer.Serialize(structured, JsonOpts),
        };

        _db.Resumes.Add(resume);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, MapDetail(resume, structured));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(201)]
    public async Task<ActionResult<ResumeDto>> Upload([FromForm] string? title, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var file = Request.Form.Files["file"];
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please select a file." });

        var uploadedFile = file!;
        var contentType = uploadedFile.ContentType?.ToLowerInvariant() ?? "";
        var fileName = uploadedFile.FileName ?? "resume.pdf";
        if (contentType != "application/pdf" && !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only PDF files are allowed for this legacy endpoint. Use parse-upload for DOCX." });

        var titleTrimmed = (title ?? fileName ?? "My Resume").Trim();
        if (string.IsNullOrEmpty(titleTrimmed)) titleTrimmed = "My Resume";

        using var stream = new MemoryStream();
        await uploadedFile.CopyToAsync(stream, ct);
        var content = stream.ToArray();

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Title = titleTrimmed.Length > 500 ? titleTrimmed[..500] : titleTrimmed,
            FileName = fileName.Length > 500 ? fileName[..500] : fileName,
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Resumes.Add(resume);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, new ResumeDto
        {
            Id = resume.Id,
            Title = resume.Title,
            FileName = resume.FileName,
            CreatedAt = resume.CreatedAt,
            UpdatedAt = resume.UpdatedAt,
            HasStructuredData = false,
        });
    }

    /// <summary>Create a resume with no file for manual entry in the structured editor.</summary>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(ResumeDetailDto), 201)]
    public async Task<ActionResult<ResumeDetailDto>> CreateManual([FromBody] ResumeManualCreateDto? body, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var titleTrimmed = (body?.Title ?? "My Resume").Trim();
        if (string.IsNullOrEmpty(titleTrimmed)) titleTrimmed = "My Resume";

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Title = titleTrimmed.Length > 500 ? titleTrimmed[..500] : titleTrimmed,
            FileName = "manual.orio",
            Content = Array.Empty<byte>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StructuredDataJson = null,
        };

        _db.Resumes.Add(resume);
        await _db.SaveChangesAsync(ct);

        var shell = ResumeStructuredParsingService.EmptyDocument();
        return StatusCode(201, MapDetail(resume, shell));
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ResumeDto>), 200)]
    public async Task<ActionResult<List<ResumeDto>>> List(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var list = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .Select(r => new ResumeDto
            {
                Id = r.Id,
                Title = r.Title,
                FileName = r.FileName,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                HasStructuredData = r.StructuredDataJson != null,
            })
            .ToListAsync(ct);

        return Ok(list);
    }

    /// <summary>Re-run AI parse on an existing uploaded file (PDF/DOCX).</summary>
    [HttpPost("{id:guid}/parse")]
    [ProducesResponseType(typeof(ResumeDetailDto), 200)]
    public async Task<ActionResult<ResumeDetailDto>> ParseExisting(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null) return NotFound(new { message = "Resume not found." });

        var ext = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx"))
            return BadRequest(new { message = "Only PDF and DOCX files can be parsed." });

        string plainText;
        try
        {
            plainText = _textExtractor.ExtractPlainText(resume.Content, resume.FileName, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        ResumeStructuredDocument structured;
        try
        {
            structured = await _parsingService.ParseResumeTextAsync(plainText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume re-parse AI error");
            return StatusCode(502, new { message = "Resume parsing failed." });
        }

        resume.StructuredDataJson = JsonSerializer.Serialize(structured, JsonOpts);
        resume.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(MapDetail(resume, structured));
    }

    [HttpGet("{id:guid}/detail")]
    [ProducesResponseType(typeof(ResumeDetailDto), 200)]
    public async Task<ActionResult<ResumeDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null) return NotFound(new { message = "Resume not found." });

        ResumeStructuredDocument structured;
        if (!string.IsNullOrWhiteSpace(resume.StructuredDataJson))
        {
            try
            {
                structured = JsonSerializer.Deserialize<ResumeStructuredDocument>(resume.StructuredDataJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ?? ResumeStructuredParsingService.EmptyDocument();
            }
            catch
            {
                structured = ResumeStructuredParsingService.EmptyDocument();
            }
        }
        else
        {
            structured = ResumeStructuredParsingService.EmptyDocument();
        }

        return Ok(MapDetail(resume, structured));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ResumeDto), 200)]
    public async Task<ActionResult<ResumeDto>> PatchTitle(Guid id, [FromBody] ResumePatchDto body, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null) return NotFound(new { message = "Resume not found." });

        if (!string.IsNullOrWhiteSpace(body.Title))
        {
            var t = body.Title.Trim();
            resume.Title = t.Length > 500 ? t[..500] : t;
            resume.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new ResumeDto
        {
            Id = resume.Id,
            Title = resume.Title,
            FileName = resume.FileName,
            CreatedAt = resume.CreatedAt,
            UpdatedAt = resume.UpdatedAt,
            HasStructuredData = resume.StructuredDataJson != null,
        });
    }

    [HttpPut("{id:guid}/structured")]
    [ProducesResponseType(typeof(ResumeDetailDto), 200)]
    public async Task<ActionResult<ResumeDetailDto>> SaveStructured(Guid id, [FromBody] ResumeStructuredDocument body, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null) return NotFound(new { message = "Resume not found." });

        var json = JsonSerializer.Serialize(body, JsonOpts);
        if (json.Length > 600_000)
            return BadRequest(new { message = "Resume data is too large." });

        resume.StructuredDataJson = json;
        resume.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(MapDetail(resume, body));
    }

    [HttpGet("{id:guid}/insights")]
    [ProducesResponseType(typeof(ResumeInsightsDto), 200)]
    public async Task<ActionResult<ResumeInsightsDto>> Insights(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var json = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId)
            .Select(r => r.StructuredDataJson)
            .FirstOrDefaultAsync(ct);

        if (json == null)
            return Ok(new ResumeInsightsDto { MissingFields = new List<string> { "No structured resume saved yet." }, ImprovementTips = new() });

        ResumeStructuredDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ResumeStructuredDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return Ok(new ResumeInsightsDto());
        }

        return Ok(ResumeInsightsService.Build(doc ?? ResumeStructuredParsingService.EmptyDocument()));
    }

    [HttpPost("{id:guid}/ai/improve")]
    [ProducesResponseType(typeof(ResumeImproveResponse), 200)]
    public async Task<ActionResult<ResumeImproveResponse>> Improve(Guid id, [FromBody] ResumeImproveRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var exists = await _db.Resumes.AsNoTracking().AnyAsync(r => r.Id == id && r.UserId == userId, ct);
        if (!exists) return NotFound(new { message = "Resume not found." });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { message = "text is required." });

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            return StatusCode(500, new { message = "AI is not configured on the server." });

        var instruction = request.Target switch
        {
            "summary" => "Rewrite as a tight professional summary (first person, 2–4 sentences). Keep facts; improve clarity and impact.",
            "experience" or "experienceDescription" => "Polish this work experience description for a resume: strong action verbs, concise bullets where natural, preserve technologies and facts.",
            "project" or "projectDescription" => "Improve this project description: outcome-focused, concise, mention stack where relevant.",
            "bullet" => "Sharpen this single bullet: measurable impact if plausible, concise, professional.",
            "education" => "Tighten this education blurb if needed; keep factual.",
            _ => "Improve this resume text for clarity and professional tone without inventing facts.",
        };

        var client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        var options = new ChatCompletionsOptions { DeploymentName = deploymentName, Temperature = 0.35f, MaxTokens = 1200 };
        options.Messages.Add(new ChatRequestSystemMessage(
            "You are an expert resume writer. Never fabricate employers, dates, degrees, or metrics. Only polish wording."));
        options.Messages.Add(new ChatRequestUserMessage(instruction + "\n\n---\n" + request.Text.Trim()));

        try
        {
            var response = await client.GetChatCompletionsAsync(options, ct);
            var improved = response.Value.Choices.FirstOrDefault()?.Message?.Content?.Trim() ?? request.Text;
            return Ok(new ResumeImproveResponse { Text = improved });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume improve AI failed");
            return StatusCode(502, new { message = "AI improvement failed. Try again." });
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null) return NotFound(new { message = "Resume not found." });

        _db.Resumes.Remove(resume);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(typeof(FileResult), 200)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var resume = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId)
            .Select(r => new { r.Content, r.FileName })
            .FirstOrDefaultAsync(ct);

        if (resume == null || resume.Content == null || resume.Content.Length == 0)
            return NotFound(new { message = "Resume not found." });

        var mime = ResumeTextExtractor.GuessMimeType(resume.FileName);
        return File(resume.Content, mime, resume.FileName);
    }

    [HttpGet("{id:guid}/text")]
    [ProducesResponseType(typeof(string), 200)]
    public async Task<ActionResult<string>> GetPlainText(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var row = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId)
            .Select(r => new { r.Content, r.FileName })
            .FirstOrDefaultAsync(ct);

        if (row == null || row.Content == null || row.Content.Length == 0)
            return NotFound(new { message = "Resume not found." });

        try
        {
            var text = _textExtractor.ExtractPlainText(row.Content, row.FileName, ct);
            return Ok(string.IsNullOrWhiteSpace(text) ? "Could not extract text from this file." : text);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static ResumeDetailDto MapDetail(Resume resume, ResumeStructuredDocument structured) => new()
    {
        Id = resume.Id,
        Title = resume.Title,
        FileName = resume.FileName,
        CreatedAt = resume.CreatedAt,
        UpdatedAt = resume.UpdatedAt,
        HasStructuredData = !string.IsNullOrWhiteSpace(resume.StructuredDataJson),
        Structured = structured,
    };
}

public class ResumeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool HasStructuredData { get; set; }
}

public class ResumeDetailDto : ResumeDto
{
    public ResumeStructuredDocument Structured { get; set; } = new();
}

public class ResumeImproveRequest
{
    public string Target { get; set; } = "summary";
    public string Text { get; set; } = "";
}

public class ResumeImproveResponse
{
    public string Text { get; set; } = "";
}

public class ResumeManualCreateDto
{
    public string? Title { get; set; }
}

public class ResumePatchDto
{
    public string? Title { get; set; }
}
