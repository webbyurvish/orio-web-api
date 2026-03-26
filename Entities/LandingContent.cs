namespace PKeetDashboard.API.Entities;

/// <summary>Key-value content for hero, section titles, CTAs, etc.</summary>
public class LandingContent
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty; // e.g. HeroHeadline, HeroSubtitle, FeatureIntroTitle
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
