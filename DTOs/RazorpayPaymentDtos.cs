using System.Text.Json.Serialization;

namespace PKeetDashboard.API.DTOs;

public class CreateRazorpayOrderRequest
{
    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("billingTab")]
    public string? BillingTab { get; set; }
}

public class CreateRazorpayOrderResponse
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = "";

    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "INR";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Smeed AI";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("prefillEmail")]
    public string? PrefillEmail { get; set; }

    [JsonPropertyName("prefillName")]
    public string? PrefillName { get; set; }

    [JsonPropertyName("testMode")]
    public bool TestMode { get; set; }
}

public class RazorpayCheckoutOptionsResponse
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = "";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "INR";

    [JsonPropertyName("testMode")]
    public bool TestMode { get; set; }

    [JsonPropertyName("methodsNote")]
    public string MethodsNote { get; set; } =
        "UPI, cards, netbanking, and wallets (as enabled on your Razorpay account).";
}

public class VerifyRazorpayPaymentRequest
{
    [JsonPropertyName("razorpayOrderId")]
    public string? RazorpayOrderId { get; set; }

    [JsonPropertyName("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [JsonPropertyName("razorpaySignature")]
    public string? RazorpaySignature { get; set; }
}

public class VerifyRazorpayPaymentResponse
{
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    [JsonPropertyName("creditsApplied")]
    public decimal CreditsApplied { get; set; }

    [JsonPropertyName("creditsBalance")]
    public decimal CreditsBalance { get; set; }
}
