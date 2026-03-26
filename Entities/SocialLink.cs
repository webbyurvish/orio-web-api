namespace PKeetDashboard.API.Entities;

public class SocialLink
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty; // LinkedIn, Twitter, Facebook, Instagram
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
