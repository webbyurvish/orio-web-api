using System.Security.Claims;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PKeetDashboard.API.Analytics;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallSessionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CallSessionsController> _logger;
    private readonly IAnalyticsRecorder _analytics;
    private static readonly TimeSpan FreeSessionDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FreeSessionCooldown = TimeSpan.FromMinutes(15);
    private const decimal CreditsPerHour = 2.0m; // 30 minutes = 1 credit

    public CallSessionsController(AppDbContext db, IConfiguration configuration, ILogger<CallSessionsController> logger, IAnalyticsRecorder analytics)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _analytics = analytics;
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

        // Enforce free-session cooldown: after a free session ends/expires, user must wait 15 minutes
        // before creating another free session.
        if (request.IsFreeSession)
        {
            var now = DateTime.UtcNow;

            // Prevent stockpiling free sessions: only allow one pending free session at a time,
            // and don't allow creating a new free session while another free one is active.
            var hasPendingOrActiveFree = await _db.CallSessions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    s.IsFreeSession &&
                    (
                        (s.Status ?? "").Trim().ToLower() == "not activated" ||
                        (
                            (s.Status ?? "").Trim().ToLower() == "active" &&
                            (!s.EndsAt.HasValue || s.EndsAt.Value > now)
                        )
                    ),
                    ct);

            if (hasPendingOrActiveFree)
            {
                return Conflict(new { message = "You already have a free session that is not activated or currently active." });
            }

            var lastEndedFree = await _db.CallSessions
                .AsNoTracking()
                .Where(s =>
                    s.UserId == userId &&
                    s.IsFreeSession &&
                    s.EndsAt.HasValue &&
                    (s.Status ?? "").Trim().ToLower() != "not activated" &&
                    s.EndsAt.Value <= now)
                .OrderByDescending(s => s.EndsAt)
                .Select(s => s.EndsAt)
                .FirstOrDefaultAsync(ct);

            if (lastEndedFree.HasValue)
            {
                var nextAllowedAt = lastEndedFree.Value.Add(FreeSessionCooldown);
                if (now < nextAllowedAt)
                {
                    var retryAfterSeconds = (int)Math.Ceiling((nextAllowedAt - now).TotalSeconds);
                    Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                    return StatusCode(429, new
                    {
                        message = "Please wait before creating another free session.",
                        nextAllowedAtUtc = nextAllowedAt
                    });
                }
            }
        }

        var session = new CallSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = (request.Title ?? "").Trim().Length > 500 ? request.Title!.Trim()[..500] : (request.Title ?? "").Trim(),
            Description = (request.Description ?? "").Trim().Length > 4000 ? request.Description!.Trim()[..4000] : (request.Description ?? "").Trim(),
            ResumeId = request.ResumeId,
            Language = (request.Language ?? "English").Trim().Length > 50 ? request.Language!.Trim()[..50] : (request.Language ?? "English").Trim(),
            SimpleLanguage = request.SimpleLanguage,
            NaturalSpeakingMode = request.NaturalSpeakingMode ?? false,
            ExtraContext = request.ExtraContext != null && request.ExtraContext.Length > 2000 ? request.ExtraContext[..2000] : request.ExtraContext,
            AiModel = ResolveStoredAiModel(request.AiModel),
            SaveTranscript = request.SaveTranscript,
            IsFreeSession = request.IsFreeSession,
            Status = "Not Activated",
            EndsAt = null,
            CreatedAt = DateTime.UtcNow,
            AiUsage = 0
        };

        _db.CallSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await _analytics.RecordAsync(
            userId,
            AnalyticsEventTypes.SessionCreated,
            JsonSerializer.Serialize(new { sessionId = session.Id, free = session.IsFreeSession }),
            "server",
            session.Id,
            ct);

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
        if (request.NaturalSpeakingMode.HasValue)
            session.NaturalSpeakingMode = request.NaturalSpeakingMode.Value;
        session.ExtraContext = request.ExtraContext != null && request.ExtraContext.Length > 2000 ? request.ExtraContext[..2000] : request.ExtraContext;
        session.AiModel = ResolveStoredAiModel(request.AiModel);
        session.SaveTranscript = request.SaveTranscript;

        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(session, null));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        _db.CallSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CallSessionDto>), 200)]
    public async Task<ActionResult<PagedResult<CallSessionDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? view = null,
        CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.CallSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId);

        // Optional list view: must match dashboard "All / Live / Ended / Not Activated" semantics so pagination totals are correct.
        var utcNow = DateTime.UtcNow;
        var v = (view ?? "all").Trim().ToLowerInvariant();
        // Use ToLower() comparisons — EF Core cannot translate string.Equals(..., StringComparison).
        if (v is "live")
        {
            query = query.Where(s =>
                (s.Status ?? "").Trim().ToLower() != "not activated" &&
                !(
                    (s.Status ?? "").Trim().ToLower() == "ended" ||
                    (s.EndsAt.HasValue && s.EndsAt.Value <= utcNow)
                ));
        }
        else if (v is "ended")
        {
            query = query.Where(s =>
                (s.Status ?? "").Trim().ToLower() == "ended" ||
                (s.EndsAt.HasValue && s.EndsAt.Value <= utcNow));
        }
        else if (v is "not-activated" or "notactivated")
        {
            query = query.Where(s =>
                (s.Status ?? "").Trim().ToLower() == "not activated");
        }

        query = query.OrderByDescending(s => s.CreatedAt);

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
                NaturalSpeakingMode = s.NaturalSpeakingMode,
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

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CallSessionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<CallSessionDto>> Get([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        return Ok(MapToDto(session, null));
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

        // New rule: usage-based billing. Do NOT charge on activate.
        // If the session already ended, require a new session instead of re-activating.
        if ((session.Status ?? "").Trim().Equals("Ended", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "This session has ended. Please create a new session to start again." });

        session.Status = "Active";
        session.ActivatedAtUtc = DateTime.UtcNow;
        // EndsAt is no longer used for billing; keep it null for active sessions.
        session.EndsAt = null;
        await _db.SaveChangesAsync(ct);

        await _analytics.RecordAsync(
            userId,
            AnalyticsEventTypes.SessionActivated,
            JsonSerializer.Serialize(new { sessionId = session.Id, mode = session.IsFreeSession ? "Free" : "Paid" }),
            "server",
            session.Id,
            ct);

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

        // Extensions are no longer applicable with usage-based billing.
        return BadRequest(new { message = "Sessions no longer use extensions. Billing is based on minutes used when you end the session." });
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

        // Usage-based billing: charge on end based on activated duration.
        if (!session.IsFreeSession)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Unauthorized();

            var start = session.ActivatedAtUtc ?? session.CreatedAt;
            var minutesUsed = Math.Max(0, (DateTime.UtcNow - start).TotalMinutes);
            // Credits: minutes / 60, rounded to 2 decimals.
            var cost = Math.Round((decimal)minutesUsed / 60m * CreditsPerHour, 2, MidpointRounding.AwayFromZero);

            if (cost > 0)
            {
                // Never go negative; if user has less than cost, charge what remains.
                var charged = Math.Min(user.CallCredits, cost);
                user.CallCredits -= charged;
                session.CreditsCharged += charged;
            }
        }

        session.Status = "Ended";
        session.EndsAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _analytics.RecordAsync(
            userId,
            AnalyticsEventTypes.SessionEnded,
            JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                aiUsage = session.AiUsage,
                minutes = (session.EndsAt!.Value - (session.ActivatedAtUtc ?? session.CreatedAt)).TotalMinutes,
                creditsCharged = session.CreditsCharged
            }),
            "server",
            session.Id,
            ct);

        // Best-effort: generate AI notes after each call ends (only if transcript is enabled).
        if (session.SaveTranscript)
        {
            try
            {
                await GenerateAndPersistAiNotesAsync(session.Id, userId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI notes generation failed for session {SessionId}", session.Id);
            }
        }
        return Ok(MapToDto(session, null));
    }

    [HttpGet("{id:guid}/ai-notes")]
    [ProducesResponseType(typeof(AiNotesDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<AiNotesDto>> GetAiNotes([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session == null)
            return NotFound();

        return Ok(new AiNotesDto
        {
            Notes = session.AiNotes,
            UpdatedAt = session.AiNotesUpdatedAt
        });
    }

    [HttpPost("{id:guid}/ai-notes/generate")]
    [ProducesResponseType(typeof(AiNotesDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<AiNotesDto>> GenerateAiNotes([FromRoute] Guid id, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var notes = await GenerateAndPersistAiNotesAsync(id, userId, ct);
        return Ok(notes);
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

        await _analytics.RecordAsync(
            userId,
            AnalyticsEventTypes.AiResponseGenerated,
            JsonSerializer.Serialize(new { sessionId = session.Id }),
            "server",
            session.Id,
            ct);

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
        NaturalSpeakingMode = s.NaturalSpeakingMode,
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

    private async Task<AiNotesDto> GenerateAndPersistAiNotesAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        var session = await _db.CallSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session == null)
            throw new InvalidOperationException("Session not found.");

        var msgs = await _db.CallSessionMessages
            .AsNoTracking()
            .Where(m => m.CallSessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(ct);

        if (msgs.Count == 0)
        {
            session.AiNotes = null;
            session.AiNotesUpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new AiNotesDto { Notes = session.AiNotes, UpdatedAt = session.AiNotesUpdatedAt };
        }

        static string NormalizeRole(string? role)
        {
            var r = (role ?? string.Empty).Trim();
            if (r.Equals("System", StringComparison.OrdinalIgnoreCase)) return "Interviewer";
            if (r.Equals("Interviewer", StringComparison.OrdinalIgnoreCase)) return "Interviewer";
            if (r.Equals("User", StringComparison.OrdinalIgnoreCase)) return "You";
            return r;
        }

        var transcriptLines = msgs
            .Where(m => !string.Equals(m.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(m.Role, "AI", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"{NormalizeRole(m.Role)}: {m.Content}")
            .ToList();

        if (transcriptLines.Count == 0)
        {
            session.AiNotes = null;
            session.AiNotesUpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new AiNotesDto { Notes = session.AiNotes, UpdatedAt = session.AiNotesUpdatedAt };
        }

        var transcript = string.Join("\n", transcriptLines);
        if (transcript.Length > 80_000)
            transcript = transcript[^80_000..];

        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var key = _configuration["AzureOpenAI:Key"];
        var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("AzureOpenAI config missing.");

        var systemPrompt =
            "You are an expert meeting note-taker for interview calls.\n" +
            "Summarize the conversation into crisp notes with no fluff.\n\n" +
            "Output STRICTLY in markdown with these sections and bullet points:\n" +
            "## Key points\n" +
            "- ...\n\n" +
            "## Action items\n" +
            "- ...\n\n" +
            "## Decisions\n" +
            "- ...\n\n" +
            "If a section has nothing, include a single bullet: - None\n";

        var options = new ChatCompletionsOptions { DeploymentName = deploymentName, Temperature = 0.2f };
        options.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
        options.Messages.Add(new ChatRequestUserMessage(
            "Here is the call transcript:\n\n" + transcript));

        var client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        var response = await client.GetChatCompletionsAsync(options, ct).ConfigureAwait(false);
        var notes = response.Value.Choices.FirstOrDefault()?.Message?.Content?.Trim();

        session.AiNotes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        session.AiNotesUpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new AiNotesDto { Notes = session.AiNotes, UpdatedAt = session.AiNotesUpdatedAt };
    }

    /// <summary>
    /// When the client omits a model, persist the configured Azure OpenAI deployment name (matches runtime).
    /// </summary>
    private string ResolveStoredAiModel(string? requestAiModel)
    {
        var configured = (_configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini").Trim();
        if (string.IsNullOrWhiteSpace(configured))
            configured = "gpt-4o-mini";
        if (configured.Length > 100)
            configured = configured[..100];

        var trimmed = (requestAiModel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return configured;

        return trimmed.Length > 100 ? trimmed[..100] : trimmed;
    }
}

public class CreateCallSessionRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? ResumeId { get; set; }
    public string? Language { get; set; }
    public bool SimpleLanguage { get; set; }
    public bool? NaturalSpeakingMode { get; set; }
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
    public bool NaturalSpeakingMode { get; set; }
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

public class AiNotesDto
{
    public string? Notes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
