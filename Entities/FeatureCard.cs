namespace PKeetDashboard.API.Entities;

public class FeatureCard
{
    public Guid Id { get; set; }
    public string SectionKey { get; set; } = string.Empty; // HowItHelps, MoreFeatures
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ButtonText { get; set; }
    public string? ButtonUrl { get; set; }
    public string? IconName { get; set; }
    public string? ImageUrl { get; set; }
    public string? ExtraData { get; set; } // JSON for score, list items, etc.
    public int SortOrder { get; set; }
}
