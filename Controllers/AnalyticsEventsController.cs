using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsEventsController : ControllerBase
{
    private readonly IAnalyticsRecorder _recorder;

    public AnalyticsEventsController(IAnalyticsRecorder recorder) => _recorder = recorder;

    [HttpPost("events")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Ingest([FromBody] AnalyticsBatchRequest body, CancellationToken ct)
    {
        if (body.Events == null || body.Events.Count == 0)
            return BadRequest(new { message = "At least one event is required." });
        if (body.Events.Count > 100)
            return BadRequest(new { message = "Maximum 100 events per batch." });

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        foreach (var e in body.Events)
        {
            await _recorder.RecordAsync(
                userId,
                e.EventType.Trim(),
                string.IsNullOrWhiteSpace(e.MetadataJson) ? null : e.MetadataJson.Trim(),
                (e.Source ?? "web").Trim().ToLowerInvariant(),
                e.CallSessionId,
                ct);
        }

        return NoContent();
    }
}
