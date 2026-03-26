namespace PKeetDashboard.API.Entities;

public class Topic
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
}
