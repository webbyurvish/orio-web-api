namespace PKeetDashboard.API.Entities;

public class StripePaymentReceipt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string StripeSessionId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public decimal CreditsApplied { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? AmountUsdCents { get; set; }
    public string? Currency { get; set; }
}


