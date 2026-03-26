using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallSessionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CallSessionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CallSessionDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<CallSessionDto>> Create([FromBody] CreateCallSessionRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = new CallSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = (request.Title ?? "").Trim().Length > 500 ? request.Title!.Trim()[..500] : (request.Title ?? "").Trim(),
            Description = (request.Description ?? "").Trim().Length > 4000 ? request.Description!.Trim()[..4000] : (request.Description ?? "").Trim(),
            ResumeId = request.ResumeId,
            Language = (request.Language ?? "English").Trim().Length > 50 ? request.Language!.Trim()[..50] : (request.Language ?? "English").Trim(),
            SimpleLanguage = request.SimpleLanguage,
            ExtraContext = request.ExtraContext != null && request.ExtraContext.Length > 2000 ? request.ExtraContext[..2000] : request.ExtraContext,
            AiModel = (request.AiModel ?? "GPT-4.1 Mini").Trim().Length > 100 ? request.AiModel!.Trim()[..100] : (request.AiModel ?? "GPT-4.1 Mini").Trim(),
            SaveTranscript = request.SaveTranscript,
            IsFreeSession = request.IsFreeSession,
            Status = "Not Activated",
            EndsAt = null,
            CreatedAt = DateTime.UtcNow,
            AiUsage = 0
        };

        _db.CallSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var dto = MapToDto(session, null);
        return StatusCode(201, dto);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> Update([FromRoute] Guid id, [FromBody] CreateCallSessionRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        session.Title = (request.Title ?? "").Trim().Length > 500 ? request.Title!.Trim()[..500] : (request.Title ?? "").Trim();
        session.Description = (request.Description ?? "").Trim().Length > 4000 ? request.Description!.Trim()[..4000] : (request.Description ?? "").Trim();
        session.ResumeId = request.ResumeId;
        session.Language = (request.Language ?? "English").Trim().Length > 50 ? request.Language!.Trim()[..50] : (request.Language ?? "English").Trim();
        session.SimpleLanguage = request.SimpleLanguage;
        session.ExtraContext = request.ExtraContext != null && request.ExtraContext.Length > 2000 ? request.ExtraContext[..2000] : request.ExtraContext;
        session.AiModel = (request.AiModel ?? "GPT-4.1 Mini").Trim().Length > 100 ? request.AiModel!.Trim()[..100] : (request.AiModel ?? "GPT-4.1 Mini").Trim();
        session.SaveTranscript = request.SaveTranscript;

        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CallSessionDto>), 200)]
    public async Task<ActionResult<PagedResult<CallSessionDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.CallSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new CallSessionDto
            {
                Id = s.Id,
                Title = s.Title,
                Description = s.Description,
                ResumeId = s.ResumeId,
                Language = s.Language,
                SimpleLanguage = s.SimpleLanguage,
                ExtraContext = s.ExtraContext,
                AiModel = s.AiModel,
                SaveTranscript = s.SaveTranscript,
                Status = s.Status,
                EndsAt = s.EndsAt,
                EndsIn = ComputeEndsIn(s.Status, s.EndsAt),
                IsFreeSession = s.IsFreeSession,
                AiUsage = s.AiUsage,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResult<CallSessionDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(List<CallSessionMessageDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<CallSessionMessageDto>>> GetMessages([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var sessionExists = await _db.CallSessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct);
        if (!sessionExists)
            return NotFound();

        var messages = await _db.CallSessionMessages
            .AsNoTracking()
            .Where(m => m.CallSessionId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new CallSessionMessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(messages);
    }

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(CallSessionMessageDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionMessageDto>> AddMessage([FromRoute] Guid id, [FromBody] AddCallSessionMessageRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var sessionExists = await _db.CallSessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct);
        if (!sessionExists)
            return NotFound();

        var role = (request.Role ?? "User").Trim();
        if (role.Length > 50) role = role[..50];
        var content = (request.Content ?? "").Trim();
        if (content.Length > 100_000) content = content[..100_000];

        var msg = new CallSessionMessage
        {
            Id = Guid.NewGuid(),
            CallSessionId = id,
            Role = role,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        _db.CallSessionMessages.Add(msg);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, new CallSessionMessageDto { Id = msg.Id, Role = msg.Role, Content = msg.Content, CreatedAt = msg.CreatedAt });
    }

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        // If already ended, allow re-activate by starting a new window from now.
        session.Status = "Active";
        var minutes = session.IsFreeSession ? 2 : 30;
        session.EndsAt = DateTime.UtcNow.AddMinutes(minutes);
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpPost("{id:guid}/extend")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> Extend([FromRoute] Guid id, [FromQuery] int minutes = 30, CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        minutes = Math.Clamp(minutes, 1, 24 * 60);
        session.Status = "Active";
        var baseTime = session.EndsAt.HasValue && session.EndsAt.Value > DateTime.UtcNow ? session.EndsAt.Value : DateTime.UtcNow;
        session.EndsAt = baseTime.AddMinutes(minutes);
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpPost("{id:guid}/end")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> End([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        session.Status = "Ended";
        session.EndsAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpPost("{id:guid}/ai-usage")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> IncrementAiUsage([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        session.AiUsage += 1;
        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpPost("{id:guid}/messages/bulk")]
    [ProducesResponseType(typeof(List<CallSessionMessageDto>), 201)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<CallSessionMessageDto>>> AddMessagesBulk([FromRoute] Guid id, [FromBody] List<AddCallSessionMessageRequest> requests, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var sessionExists = await _db.CallSessions.AnyAsync(s => s.Id == id && s.UserId == userId, ct);
        if (!sessionExists)
            return NotFound();

        var list = new List<CallSessionMessageDto>();
        var toIterate = requests ?? new List<AddCallSessionMessageRequest>();
        foreach (var r in toIterate)
        {
            var role = (r.Role ?? "User").Trim();
            if (role.Length > 50) role = role[..50];
            var content = (r.Content ?? "").Trim();
            if (content.Length > 100_000) content = content[..100_000];
            var msg = new CallSessionMessage
            {
                Id = Guid.NewGuid(),
                CallSessionId = id,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };
            _db.CallSessionMessages.Add(msg);
            list.Add(new CallSessionMessageDto { Id = msg.Id, Role = msg.Role, Content = msg.Content, CreatedAt = msg.CreatedAt });
        }
        if (list.Count > 0)
            await _db.SaveChangesAsync(ct);
        return StatusCode(201, list);
    }

    private static CallSessionDto MapToDto(CallSession s, string? resumeFileName) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        ResumeId = s.ResumeId,
        Language = s.Language,
        SimpleLanguage = s.SimpleLanguage,
        ExtraContext = s.ExtraContext,
        AiModel = s.AiModel,
        SaveTranscript = s.SaveTranscript,
        Status = s.Status,
        EndsAt = s.EndsAt,
        EndsIn = ComputeEndsIn(s.Status, s.EndsAt),
        IsFreeSession = s.IsFreeSession,
        AiUsage = s.AiUsage,
        CreatedAt = s.CreatedAt
    };

    private static string ComputeEndsIn(string status, DateTime? endsAtUtc)
    {
        var normalized = (status ?? string.Empty).Trim();
        if (string.Equals(normalized, "Not Activated", StringComparison.OrdinalIgnoreCase))
            return "Not Activated";
        if (string.Equals(normalized, "Ended", StringComparison.OrdinalIgnoreCase))
            return "Ended";

        if (!endsAtUtc.HasValue)
            return string.IsNullOrWhiteSpace(normalized) ? "—" : normalized;

        var remaining = endsAtUtc.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "Ended";

        var minutes = (int)Math.Floor(remaining.TotalMinutes);
        var seconds = remaining.Seconds;
        return $"{minutes:00}:{seconds:00}";
    }
}

public class CreateCallSessionRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? ResumeId { get; set; }
    public string? Language { get; set; }
    public bool SimpleLanguage { get; set; }
    public string? ExtraContext { get; set; }
    public string? AiModel { get; set; }
    public bool SaveTranscript { get; set; }
    public bool IsFreeSession { get; set; } = true;
}

public class CallSessionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? ResumeId { get; set; }
    public string Language { get; set; } = string.Empty;
    public bool SimpleLanguage { get; set; }
    public string? ExtraContext { get; set; }
    public string AiModel { get; set; } = string.Empty;
    public bool SaveTranscript { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? EndsAt { get; set; }
    public string EndsIn { get; set; } = string.Empty;
    public bool IsFreeSession { get; set; }
    public int AiUsage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public class CallSessionMessageDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AddCallSessionMessageRequest
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}
