namespace PKeetDashboard.API.Options;

/// <summary>
/// Razorpay keys from Dashboard → Settings → API keys. Use rzp_test_… in test mode.
/// </summary>
public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string DashboardBaseUrl { get; set; } = "http://localhost:5173";
}
