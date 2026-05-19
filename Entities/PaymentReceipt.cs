namespace PKeetDashboard.API.Entities;

public class PaymentReceipt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string RazorpayOrderId { get; set; } = string.Empty;
    public string? RazorpayPaymentId { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public decimal CreditsApplied { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? AmountInrPaise { get; set; }
    public string? Currency { get; set; }
}
