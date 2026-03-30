using System.Text.Json.Serialization;

namespace PKeetDashboard.API.DTOs;

public class CreateStripeCheckoutRequest
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = "";

    /// <summary>Active pricing tab on the SPA (credits, subscription, lifetime) for cancel URL.</summary>
    [JsonPropertyName("billingTab")]
    public string? BillingTab { get; set; }
}

public class CreateStripeCheckoutResponse
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class VerifyStripeSessionResponse
{
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
}
