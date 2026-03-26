using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ResumesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ResumesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<ResumeDto>> Upload([FromForm] string? title, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var file = Request.Form.Files["file"];
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Please select a PDF file." });

        var uploadedFile = file!;
        var contentType = uploadedFile.ContentType?.ToLowerInvariant() ?? "";
        string fileName = uploadedFile.FileName ?? "resume.pdf";
        if (contentType != "application/pdf" && !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only PDF files are allowed." });

        var titleTrimmed = (title ?? fileName ?? "My Resume").Trim();
        if (string.IsNullOrEmpty(titleTrimmed))
            titleTrimmed = "My Resume";

        using var stream = new MemoryStream();
        await uploadedFile.CopyToAsync(stream, ct);
        var content = stream.ToArray();

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = titleTrimmed.Length > 500 ? titleTrimmed[..500] : titleTrimmed,
            FileName = fileName.Length > 500 ? fileName[..500] : fileName,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        _db.Resumes.Add(resume);
        await _db.SaveChangesAsync(ct);

        var dto = new ResumeDto
        {
            Id = resume.Id,
            Title = resume.Title,
            FileName = resume.FileName,
            CreatedAt = resume.CreatedAt
        };
        return StatusCode(201, dto);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ResumeDto>), 200)]
    public async Task<ActionResult<List<ResumeDto>>> List(CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var list = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ResumeDto
            {
                Id = r.Id,
                Title = r.Title,
                FileName = r.FileName,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(list);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var resume = await _db.Resumes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (resume == null)
            return NotFound(new { message = "Resume not found." });

        _db.Resumes.Remove(resume);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var resume = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId)
            .Select(r => new { r.Content, r.FileName })
            .FirstOrDefaultAsync(ct);

        if (resume == null || resume.Content == null || resume.Content.Length == 0)
            return NotFound(new { message = "Resume not found." });

        return File(resume.Content, "application/pdf", resume.FileName);
    }

    [HttpGet("{id:guid}/text")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<string>> GetPlainText(Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var resume = await _db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == id && r.UserId == userId)
            .Select(r => new { r.Content })
            .FirstOrDefaultAsync(ct);

        if (resume == null || resume.Content == null || resume.Content.Length == 0)
            return NotFound(new { message = "Resume not found." });

        using var ms = new MemoryStream(resume.Content);
        using var pdf = PdfDocument.Open(ms);
        var sb = new System.Text.StringBuilder();
        foreach (Page page in pdf.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(text);
            }
        }

        var result = sb.ToString();
        if (string.IsNullOrWhiteSpace(result))
            result = "Resume PDF could not be converted to plain text.";

        return Ok(result);
    }
}

public class ResumeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
