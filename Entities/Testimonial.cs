namespace PKeetDashboard.API.Entities;

public class Testimonial
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Quote { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public int SortOrder { get; set; }
}
