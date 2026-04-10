namespace PKeetDashboard.API.Analytics;

/// <summary>Stable event names for product analytics (web, desktop, server).</summary>
public static class AnalyticsEventTypes
{
    public const string UserSignup = "USER_SIGNUP";
    public const string UserLogin = "USER_LOGIN";
    public const string DashboardViewed = "DASHBOARD_VIEWED";

    public const string SessionCreated = "SESSION_CREATED";
    public const string SessionActivated = "SESSION_ACTIVATED";
    public const string SessionEnded = "SESSION_ENDED";

    public const string AnalyzeScreenRequested = "ANALYZE_SCREEN_REQUESTED";
    public const string AiResponseGenerated = "AI_RESPONSE_GENERATED";

    public const string CreditsPurchased = "CREDITS_PURCHASED";
    public const string SubscriptionPurchased = "SUBSCRIPTION_PURCHASED";
    public const string LifetimePurchased = "LIFETIME_PURCHASED";

    public const string FeedbackSubmitted = "FEEDBACK_SUBMITTED";

    public const string ApiClientError = "API_CLIENT_ERROR";
    public const string ApiServerError = "API_SERVER_ERROR";
}
