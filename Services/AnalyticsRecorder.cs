using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Services;

public sealed class AnalyticsRecorder : IAnalyticsRecorder
{
    private readonly AppDbContext _db;

    public AnalyticsRecorder(AppDbContext db) => _db = db;

    public async Task RecordAsync(
        Guid? userId,
        string eventType,
        string? metadataJson,
        string source,
        Guid? callSessionId = null,
        CancellationToken ct = default)
    {
        eventType = (eventType ?? string.Empty).Trim();
        if (eventType.Length == 0) return;

        source = string.IsNullOrWhiteSpace(source) ? "server" : source.Trim();
        if (source.Length > 20) source = source[..20];

        _db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CallSessionId = callSessionId,
            EventType = eventType.Length > 80 ? eventType[..80] : eventType,
            MetadataJson = metadataJson,
            Source = source,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (userId.HasValue)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId.Value, ct);
            if (u != null)
                u.LastActiveAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
