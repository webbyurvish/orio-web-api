namespace PKeetDashboard.API.Entities;

public class CallSessionMessage
{
    public Guid Id { get; set; }
    public Guid CallSessionId { get; set; }
    public string Role { get; set; } = "User"; // User, Assistant, System
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
