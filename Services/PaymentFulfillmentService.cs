using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Analytics;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Entities;

namespace PKeetDashboard.API.Services;

public sealed class PaymentFulfillmentService
{
    private readonly AppDbContext _db;
    private readonly IAnalyticsRecorder _analytics;

    public PaymentFulfillmentService(AppDbContext db, IAnalyticsRecorder analytics)
    {
        _db = db;
        _analytics = analytics;
    }

    public async Task<(bool Applied, decimal CreditsApplied)> ApplyPaidOrderAsync(
        Guid userId,
        string razorpayOrderId,
        string razorpayPaymentId,
        PaymentCatalogItem item,
        CancellationToken ct)
    {
        var already = await _db.PaymentReceipts.AnyAsync(r => r.RazorpayOrderId == razorpayOrderId, ct);
        if (already) return (false, 0m);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return (false, 0m);

        decimal appliedCredits = 0m;
        if (item.CreditsDelta > 0)
        {
            appliedCredits = item.CreditsDelta;
            user.CallCredits += appliedCredits;
        }

        _db.PaymentReceipts.Add(new PaymentReceipt
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RazorpayOrderId = razorpayOrderId,
            RazorpayPaymentId = razorpayPaymentId,
            ProductId = item.Id,
            CreditsApplied = appliedCredits,
            CreatedAt = DateTime.UtcNow,
            AmountInrPaise = item.UnitAmountInrPaise,
            Currency = "INR",
        });
        await _db.SaveChangesAsync(ct);

        var purchaseEvent = item.Id switch
        {
            "sub_monthly" or "sub_yearly" => AnalyticsEventTypes.SubscriptionPurchased,
            "lifetime" => AnalyticsEventTypes.LifetimePurchased,
            _ => AnalyticsEventTypes.CreditsPurchased,
        };
        await _analytics.RecordAsync(
            user.Id,
            purchaseEvent,
            System.Text.Json.JsonSerializer.Serialize(new { productId = item.Id, credits = appliedCredits }),
            "server",
            null,
            ct);

        return (true, appliedCredits);
    }
}
