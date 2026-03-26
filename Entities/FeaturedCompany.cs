namespace PKeetDashboard.API.Entities;

public class FeaturedCompany
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int SortOrder { get; set; }
}
