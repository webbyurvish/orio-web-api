namespace PKeetDashboard.API.Entities;

public class CallSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;       // Company name
    public string Description { get; set; } = string.Empty; // Job description
    public Guid? ResumeId { get; set; }
    public string Language { get; set; } = "English";
    public bool SimpleLanguage { get; set; }
    public string? ExtraContext { get; set; }
    public string AiModel { get; set; } = "GPT-4.1 Mini";
    public bool SaveTranscript { get; set; }
    public bool IsFreeSession { get; set; } = true;
    public string Status { get; set; } = "Not Activated";   // Not Activated, Expired, etc.
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AiUsage { get; set; }
}
