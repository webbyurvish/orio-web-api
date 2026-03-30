using System.Text.Json.Serialization;

namespace PKeetDashboard.API.DTOs;

public class StripeCheckoutOptionsResponse
{
    [JsonPropertyName("checkoutCurrency")]
    public string CheckoutCurrency { get; set; } = "USD";

    /// <summary>True when server uses INR checkout — Stripe Checkout can show UPI for eligible customers.</summary>
    [JsonPropertyName("upiEnabled")]
    public bool UpiEnabled { get; set; }
}
