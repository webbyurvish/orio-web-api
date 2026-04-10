namespace PKeetDashboard.API.Entities;

public sealed class AnalyticsEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CallSessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    /// <summary>web | desktop | server</summary>
    public string Source { get; set; } = "server";
    public DateTime CreatedAtUtc { get; set; }
}
