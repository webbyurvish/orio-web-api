namespace PKeetDashboard.API.Entities;

public sealed class UserDiscoveryResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>One of the UI options (e.g., YouTube, Instagram - Chiku AI, Google Search, Other).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Free text when Source == Other.</summary>
    public string? OtherText { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

