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
    /// <summary>When true, AI answers are steered toward conversational, spoken-human tone (desktop interview flow).</summary>
    public bool NaturalSpeakingMode { get; set; }
    public string? ExtraContext { get; set; }
    public string AiModel { get; set; } = "GPT-4.1 Mini";
    public bool SaveTranscript { get; set; }
    public bool IsFreeSession { get; set; } = true;
    public string Status { get; set; } = "Not Activated";   // Not Activated, Expired, etc.
    /// <summary>
    /// When the session was activated (started) by the user (UTC). Used for usage-based billing.
    /// </summary>
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AiUsage { get; set; }
    /// <summary>Total credits charged for this session (for audit/debug).</summary>
    public decimal CreditsCharged { get; set; } = 0m;

    /// <summary>Auto-generated notes after the call ends (markdown).</summary>
    public string? AiNotes { get; set; }
    public DateTime? AiNotesUpdatedAt { get; set; }
}
