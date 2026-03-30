namespace PKeetDashboard.API.Options;

/// <summary>
/// Stripe keys: Dashboard → Developers → API keys. Use sk_test_… / pk_test_… for free testing.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Secret key (sk_test_… or sk_live_…). Never expose to the browser.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Webhook signing secret (whsec_…). Optional until you enable webhooks.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Where the SPA is hosted (no trailing slash). Used for Checkout success/cancel URLs.</summary>
    public string DashboardBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// <c>USD</c> — card (and other methods Stripe enables automatically). <c>INR</c> — Indian rupees with
    /// <c>card</c> + <c>upi</c> on Checkout (UPI requires INR; use a Stripe account eligible for India/UPI).
    /// </summary>
    public string CheckoutCurrency { get; set; } = "USD";
}
