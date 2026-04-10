namespace PKeetDashboard.API.DTOs;

public sealed class AdminDateRangeQuery
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}

public sealed class AdminDashboardDto
{
    public AdminKpiBlock Kpis { get; set; } = new();
    public List<AdminTimeSeriesPointDto> UserGrowthDaily { get; set; } = new();
    public List<AdminTimeSeriesPointDto> SessionsDaily { get; set; } = new();
    public List<AdminTimeSeriesPointDto> RevenueDailyUsd { get; set; } = new();
    public AdminCohortSummaryDto Cohort { get; set; } = new();
}

public sealed class AdminKpiBlock
{
    public int TotalUsers { get; set; }
    public int NewUsersInRange { get; set; }
    public int Dau { get; set; }
    public int Wau { get; set; }
    public int Mau { get; set; }
    public int TotalSessions { get; set; }
    public int SessionsInRange { get; set; }
    public double AvgSessionMinutes { get; set; }
    public int TotalAiAnswers { get; set; }
    public decimal RevenueUsdInRange { get; set; }
    public decimal MrrUsdEstimate { get; set; }
    public double SignupToPaidConversionPercent { get; set; }
}

public sealed class AdminTimeSeriesPointDto
{
    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
}

public sealed class AdminCohortSummaryDto
{
    public double RetentionDay1Percent { get; set; }
    public double RetentionDay7Percent { get; set; }
    public double RetentionDay30Percent { get; set; }
    public double ChurnRateMonthlyPercent { get; set; }
}

public sealed class AdminPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public sealed class AdminUserRowDto

{

    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastActiveAtUtc { get; set; }

    public decimal CallCredits { get; set; }

    public int SessionCount { get; set; }

    public int TotalAiUsage { get; set; }

}



public sealed class AdminSubscriptionSummaryDto

{

    public int TotalPayingUsers { get; set; }

    public Dictionary<string, int> ProductPurchaseCounts { get; set; } = new();

    public string? MostPopularProductId { get; set; }

    public decimal RevenueUsdInRange { get; set; }

    public decimal MrrUsdEstimate { get; set; }

    public double FreeToPaidConversionPercent { get; set; }

    public int CancellationsInRange { get; set; }

    public int RenewalsInRange { get; set; }

}



public sealed class AdminUsageSummaryDto

{

    public int TotalSessions { get; set; }

    public double TotalMinutesEstimated { get; set; }

    public double AvgSessionMinutes { get; set; }

    public double AvgSessionsPerUser { get; set; }

    public List<AdminHourlyBucketDto> SessionsByHourUtc { get; set; } = new();

    public List<AdminFeatureUsageDto> FeatureEvents { get; set; } = new();

}

public sealed class AdminHourlyBucketDto
{
    public int HourUtc { get; set; }
    public int Count { get; set; }
}

public sealed class AdminFeatureUsageDto

{

    public string EventType { get; set; } = string.Empty;

    public long Count { get; set; }

}



public sealed class AdminAiMetricsDto

{

    public long TotalRequests { get; set; }

    public double AvgLatencyMs { get; set; }

    public double ErrorRatePercent { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    public decimal TotalCostUsdEstimate { get; set; }

    public decimal AvgCostPerUserUsd { get; set; }

    public List<AdminTimeSeriesPointDto> LatencyDailyAvgMs { get; set; } = new();

}



public sealed class AdminFunnelDto

{

    public List<AdminFunnelStepDto> Steps { get; set; } = new();

}



public sealed class AdminFunnelStepDto

{

    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public long Count { get; set; }

    public double ConversionFromPreviousPercent { get; set; }

}



public sealed class AdminFeedbackRowDto

{

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string UserEmail { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? Rating { get; set; }

    public string? SentimentTags { get; set; }

    public DateTime CreatedAtUtc { get; set; }

}



public sealed class AdminSystemMetricsDto

{

    public long ServerErrorEvents { get; set; }

    public long ClientErrorEvents { get; set; }

    public double AvgServerLatencyMsFromAiLogs { get; set; }

    public string Note { get; set; } = "Deep APM should augment this (App Insights, etc.).";

}

