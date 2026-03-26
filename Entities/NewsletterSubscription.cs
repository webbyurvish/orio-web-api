namespace PKeetDashboard.API.Entities;

public class NewsletterSubscription
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
