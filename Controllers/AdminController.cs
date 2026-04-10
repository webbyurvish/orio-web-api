using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly AdminAnalyticsService _analytics;
    private readonly AppDbContext _db;

    public AdminController(AdminAnalyticsService analytics, AppDbContext db)
    {
        _analytics = analytics;
        _db = db;
    }

    private static (DateTime from, DateTime to) ParseRange(DateTime? fromUtc, DateTime? toUtc)
    {
        var to = toUtc ?? DateTime.UtcNow;
        var from = fromUtc ?? to.AddDays(-30);
        if (to < from) (from, to) = (to, from);
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc));
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardDto>> Dashboard([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetDashboardAsync(from, to, ct));
    }

    [HttpGet("users")]
    public async Task<ActionResult<AdminPagedResult<AdminUserRowDto>>> Users(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        return Ok(await _analytics.GetUsersAsync(fromUtc, toUtc, search, page, pageSize, ct));
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<AdminSubscriptionSummaryDto>> Subscriptions(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetSubscriptionsAsync(from, to, ct));
    }

    [HttpGet("usage")]
    public async Task<ActionResult<AdminUsageSummaryDto>> Usage(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetUsageAsync(from, to, ct));
    }

    [HttpGet("ai-metrics")]
    public async Task<ActionResult<AdminAiMetricsDto>> AiMetrics(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetAiMetricsAsync(from, to, ct));
    }

    [HttpGet("funnel")]
    public async Task<ActionResult<AdminFunnelDto>> Funnel(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetFunnelAsync(from, to, ct));
    }

    [HttpGet("feedback")]
    public async Task<ActionResult<AdminPagedResult<AdminFeedbackRowDto>>> Feedback(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetFeedbackAsync(from, to, page, pageSize, ct));
    }

    [HttpGet("system-metrics")]
    public async Task<ActionResult<AdminSystemMetricsDto>> SystemMetrics(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        return Ok(await _analytics.GetSystemMetricsAsync(from, to, ct));
    }

    [HttpGet("export/events.csv")]
    public async Task<IActionResult> ExportEventsCsv(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
    {
        var (from, to) = ParseRange(fromUtc, toUtc);
        var rows = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.CreatedAtUtc >= from && e.CreatedAtUtc <= to)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(50_000)
            .Select(e => new
            {
                e.Id,
                e.UserId,
                e.CallSessionId,
                e.EventType,
                e.Source,
                e.CreatedAtUtc
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("id,userId,callSessionId,eventType,source,createdAtUtc");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(r.Id.ToString()),
                CsvEscape(r.UserId?.ToString() ?? ""),
                CsvEscape(r.CallSessionId?.ToString() ?? ""),
                CsvEscape(r.EventType),
                CsvEscape(r.Source),
                CsvEscape(r.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture))));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "orio-analytics-events.csv");
    }

    private static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return s;
    }
}
