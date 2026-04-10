namespace PKeetDashboard.API.Services;

public interface IAnalyticsRecorder
{
    Task RecordAsync(
        Guid? userId,
        string eventType,
        string? metadataJson,
        string source,
        Guid? callSessionId = null,
        CancellationToken ct = default);
}
