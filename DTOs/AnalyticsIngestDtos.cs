using System.ComponentModel.DataAnnotations;

namespace PKeetDashboard.API.DTOs;

public sealed class AnalyticsBatchRequest
{
    [Required]
    public List<AnalyticsEventItemDto> Events { get; set; } = new();
}

public sealed class AnalyticsEventItemDto
{
    [Required]
    [MaxLength(80)]
    public string EventType { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }
    public Guid? CallSessionId { get; set; }
    /// <summary>web | desktop | mobile</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "web";
}
