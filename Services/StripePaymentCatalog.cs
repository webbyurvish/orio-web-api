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
                "3 call credits",
                3m,
                999,
                89_900,
                StripeCheckoutPriceMode.OneTime),
            ["credits_plus"] = new StripeCatalogItem(
                "credits_plus",
                "Smeed AI — Plus call credits",
                "6 call credits + 2 free",
                8m,
                1665,
                149_900,
                StripeCheckoutPriceMode.OneTime),
            ["credits_pro"] = new StripeCatalogItem(
                "credits_pro",
                "Smeed AI — Pro call credits",
                "9 call credits + 4 free",
                13m,
                2777,
                249_900,
                StripeCheckoutPriceMode.OneTime),
            ["sub_monthly"] = new StripeCatalogItem(
                "sub_monthly",
                "Smeed AI — Monthly unlimited",
                "Unlimited calls — billed monthly",
                0m,
                2221,
                199_900,
                StripeCheckoutPriceMode.SubscriptionMonthly),
            ["sub_yearly"] = new StripeCatalogItem(
                "sub_yearly",
                "Smeed AI — Yearly unlimited",
                "Unlimited calls — billed yearly",
                0m,
                14_443,
                1_299_900,
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
