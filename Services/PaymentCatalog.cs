namespace PKeetDashboard.API.Services;

public enum PaymentPriceMode
{
    OneTime,
    SubscriptionMonthly,
    SubscriptionYearly,
}

/// <summary>Server-side catalog. Amounts in INR paise (₹1 = 100 paise).</summary>
public sealed record PaymentCatalogItem(
    string Id,
    string Name,
    string Description,
    decimal CreditsDelta,
    long UnitAmountInrPaise,
    PaymentPriceMode PriceMode);

public static class PaymentCatalog
{
    private static readonly IReadOnlyDictionary<string, PaymentCatalogItem> ById =
        new Dictionary<string, PaymentCatalogItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits_basic"] = new PaymentCatalogItem(
                "credits_basic",
                "Smeed AI — Basic call credits",
                "1 interview credit",
                1m,
                9_900,
                PaymentPriceMode.OneTime),
            ["credits_plus"] = new PaymentCatalogItem(
                "credits_plus",
                "Smeed AI — Plus call credits",
                "3 interview credits (2 + 1 free)",
                3m,
                19_900,
                PaymentPriceMode.OneTime),
            ["credits_pro"] = new PaymentCatalogItem(
                "credits_pro",
                "Smeed AI — Pro call credits",
                "15 interview credits (10 + 5 free)",
                15m,
                69_900,
                PaymentPriceMode.OneTime),
            ["sub_monthly"] = new PaymentCatalogItem(
                "sub_monthly",
                "Smeed AI — Monthly unlimited",
                "Unlimited calls — billed monthly",
                0m,
                99_900,
                PaymentPriceMode.SubscriptionMonthly),
            ["sub_yearly"] = new PaymentCatalogItem(
                "sub_yearly",
                "Smeed AI — Yearly unlimited",
                "Unlimited calls — billed yearly",
                0m,
                999_900,
                PaymentPriceMode.SubscriptionYearly),
            ["lifetime"] = new PaymentCatalogItem(
                "lifetime",
                "Smeed AI — Lifetime access",
                "Unlimited calls — one-time",
                0m,
                1_999_900,
                PaymentPriceMode.OneTime),
        };

    public static PaymentCatalogItem? TryGet(string productId) =>
        ById.TryGetValue(productId, out var item) ? item : null;
}
