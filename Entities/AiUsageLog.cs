namespace PKeetDashboard.API.Entities;

public sealed class AiUsageLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CallSessionId { get; set; }
    public string DeploymentName { get; set; } = string.Empty;
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
