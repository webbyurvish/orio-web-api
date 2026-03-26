namespace PKeetDashboard.API.Entities;

public class Resume
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
}
