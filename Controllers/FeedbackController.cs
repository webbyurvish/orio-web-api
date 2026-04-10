using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Analytics;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<FeedbackController> _logger;
    private readonly IAnalyticsRecorder _analytics;

    public FeedbackController(AppDbContext db, ILogger<FeedbackController> logger, IAnalyticsRecorder analytics)
    {
        _db = db;
        _logger = logger;
        _analytics = analytics;
    }

    [HttpPost]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length < 3)
            return BadRequest(new { message = "Feedback must be at least 3 characters." });

        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);
        if (!userExists)
            return Unauthorized();

        _db.UserFeedbacks.Add(new UserFeedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _analytics.RecordAsync(
            userId,
            AnalyticsEventTypes.FeedbackSubmitted,
            System.Text.Json.JsonSerializer.Serialize(new { length = message.Length }),
            "server",
            null,
            ct);

        _logger.LogInformation("User feedback saved for user {UserId}", userId);

        return NoContent();
    }
}
