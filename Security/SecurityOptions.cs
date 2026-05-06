using System.ComponentModel.DataAnnotations;

namespace PKeetDashboard.API.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    [Range(1024, 100 * 1024 * 1024)]
    public long MaxRequestBodyBytes { get; init; } = 10 * 1024 * 1024; // 10 MB default

    [Range(8, 128)]
    public int JsonMaxDepth { get; init; } = 32;

    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; init; } = 30;

    [Range(1, 1800)]
    public int LongRequestTimeoutSeconds { get; init; } = 120;

    [Range(1, 600)]
    public int AuthRequestTimeoutSeconds { get; init; } = 15;

    [Range(0, 120)]
    public int JwtClockSkewSeconds { get; init; } = 30;

    [Range(1, 10)]
    public int ForwardedHeadersForwardLimit { get; init; } = 1;

    public string[]? TrustedProxies { get; init; }
    public string[]? TrustedNetworks { get; init; }

    public RateLimitingOptions RateLimiting { get; init; } = new();
    public ConcurrencyOptions Concurrency { get; init; } = new();

    public sealed class RateLimitingOptions
    {
        [Range(10, 100000)]
        public int GlobalPerIpTokenLimit { get; init; } = 120;

        [Range(1, 100000)]
        public int GlobalPerIpTokensPerPeriod { get; init; } = 120;

        [Range(1, 3600)]
        public int GlobalPerIpReplenishSeconds { get; init; } = 60;

        [Range(0, 10000)]
        public int GlobalPerIpQueueLimit { get; init; } = 0;

        [Range(1, 100000)]
        public int PerUserPermitLimit { get; init; } = 300;

        [Range(1, 3600)]
        public int PerUserWindowSeconds { get; init; } = 60;

        [Range(1, 60)]
        public int PerUserSegmentsPerWindow { get; init; } = 6;

        [Range(0, 10000)]
        public int PerUserQueueLimit { get; init; } = 0;

        // Brute-force/costly endpoints (login, register, OTP, token exchange, etc).
        [Range(1, 10000)]
        public int AuthTokenLimit { get; init; } = 10;

        [Range(1, 10000)]
        public int AuthTokensPerPeriod { get; init; } = 10;

        [Range(1, 3600)]
        public int AuthReplenishSeconds { get; init; } = 60;

        [Range(0, 10000)]
        public int AuthQueueLimit { get; init; } = 0;
    }

    public sealed class ConcurrencyOptions
    {
        [Range(1, 10000)]
        public int GlobalPermitLimit { get; init; } = 200;

        [Range(0, 10000)]
        public int GlobalQueueLimit { get; init; } = 0;
    }
}

