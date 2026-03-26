namespace PKeetDashboard.API.Entities;

public class PromoBanner
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? CtaText { get; set; }
    public string? CtaUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
