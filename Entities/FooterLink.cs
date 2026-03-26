namespace PKeetDashboard.API.Entities;

public class FooterLink
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty; // Product, Resources, Company
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = "#";
    public int SortOrder { get; set; }
}
