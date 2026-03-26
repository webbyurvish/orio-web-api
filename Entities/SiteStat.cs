namespace PKeetDashboard.API.Entities;

public class SiteStat
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty; // e.g. HappyEngineers, QuestionsCount, TopicsCount
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
