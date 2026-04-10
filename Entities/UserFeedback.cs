namespace PKeetDashboard.API.Entities;

public sealed class UserFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int? Rating { get; set; }
    /// <summary>Optional JSON array of sentiment / theme tags.</summary>
    public string? SentimentTags { get; set; }
}

