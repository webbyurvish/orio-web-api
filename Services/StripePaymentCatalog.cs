namespace PKeetDashboard.API.Services;

public enum StripeCheckoutPriceMode
{
    OneTime,
    SubscriptionMonthly,
    SubscriptionYearly,
}

/// <summary>
/// Server-side catalog. USD amounts in cents; INR amounts in paise (₹1 = 100 paise) for Stripe.
/// INR figures match the dashboard rupee prices (UPI is only available with INR checkout).
/// </summary>
public sealed record StripeCatalogItem(
    string Id,
    string Name,
    string Description,
    decimal CreditsDelta,
    long UnitAmountUsdCents,
    long UnitAmountInrPaise,
    StripeCheckoutPriceMode PriceMode);

public static class StripePaymentCatalog
{
    // Amounts must match orio-web-app billing/plans.ts + LandingPage + Dashboard pricing display.
    private static readonly IReadOnlyDictionary<string, StripeCatalogItem> ById =
        new Dictionary<string, StripeCatalogItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits_basic"] = new StripeCatalogItem(
                "credits_basic",
                "Smeed AI — Basic call credits",
                "1 interview credit",
                1m,
                110,
                9_900,
                StripeCheckoutPriceMode.OneTime),
            ["credits_plus"] = new StripeCatalogItem(
                "credits_plus",
                "Smeed AI — Plus call credits",
                "3 interview credits (2 + 1 free)",
                3m,
                221,
                19_900,
                StripeCheckoutPriceMode.OneTime),
            ["credits_pro"] = new StripeCatalogItem(
                "credits_pro",
                "Smeed AI — Pro call credits",
                "15 interview credits (10 + 5 free)",
                15m,
                777,
                69_900,
                StripeCheckoutPriceMode.OneTime),
            ["sub_monthly"] = new StripeCatalogItem(
                "sub_monthly",
                "Smeed AI — Monthly unlimited",
                "Unlimited calls — billed monthly",
                0m,
                1110,
                99_900,
                StripeCheckoutPriceMode.SubscriptionMonthly),
            ["sub_yearly"] = new StripeCatalogItem(
                "sub_yearly",
                "Smeed AI — Yearly unlimited",
                "Unlimited calls — billed yearly",
                0m,
                11_110,
                999_900,
                StripeCheckoutPriceMode.SubscriptionYearly),
            ["lifetime"] = new StripeCatalogItem(
                "lifetime",
                "Smeed AI — Lifetime access",
                "Unlimited calls — one-time",
                0m,
                22_221,
                1_999_900,
                StripeCheckoutPriceMode.OneTime),
        };

    public static StripeCatalogItem? TryGet(string productId) =>
        ById.TryGetValue(productId, out var item) ? item : null;
}
