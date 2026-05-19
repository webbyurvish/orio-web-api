using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Analytics;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;

namespace PKeetDashboard.API.Services;

public sealed class AdminAnalyticsService
{
    private readonly AppDbContext _db;

    public AdminAnalyticsService(AppDbContext db) => _db = db;

    public async Task<AdminDashboardDto> GetDashboardAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var totalUsers = await _db.Users.AsNoTracking().CountAsync(ct);
        var newUsers = await _db.Users.AsNoTracking()
            .CountAsync(u => u.CreatedAt >= fromUtc && u.CreatedAt <= toUtc, ct);

        var today = DateTime.UtcNow.Date;
        var dau = await _db.Users.AsNoTracking()
            .CountAsync(u => u.LastActiveAtUtc >= today && u.LastActiveAtUtc < today.AddDays(1), ct);
        var wauStart = today.AddDays(-7);
        var wau = await _db.Users.AsNoTracking()
            .CountAsync(u => u.LastActiveAtUtc >= wauStart, ct);
        var mauStart = today.AddDays(-30);
        var mau = await _db.Users.AsNoTracking()
            .CountAsync(u => u.LastActiveAtUtc >= mauStart, ct);

        var totalSessions = await _db.CallSessions.AsNoTracking().CountAsync(ct);
        var sessionsInRange = await _db.CallSessions.AsNoTracking()
            .CountAsync(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc, ct);

        var endedDurations = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc &&
                        s.Status == "Ended" && s.EndsAt.HasValue)
            .Select(s => new { s.CreatedAt, s.EndsAt })
            .ToListAsync(ct);
        var avgMinutes = endedDurations.Count > 0
            ? endedDurations.Average(s => (s.EndsAt!.Value - s.CreatedAt).TotalMinutes)
            : 0;

        var aiAnswers = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .SumAsync(s => (long)s.AiUsage, ct);

        var revenuePaise = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc && r.AmountInrPaise.HasValue)
            .SumAsync(r => r.AmountInrPaise ?? 0, ct);
        var revenueInr = revenuePaise / 100m;

        var mrrWindowStart = today.AddDays(-30);
        var subMonthlyPaise = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= mrrWindowStart && r.ProductId == "sub_monthly" && r.AmountInrPaise.HasValue)
            .SumAsync(r => r.AmountInrPaise ?? 0, ct);
        var mrrEstimate = subMonthlyPaise / 100m;

        var usersWithPayment = await _db.PaymentReceipts.AsNoTracking()
            .Select(r => r.UserId).Distinct().CountAsync(ct);
        var signupToPaid = totalUsers > 0 ? 100.0 * usersWithPayment / totalUsers : 0;

        var cohort = await BuildCohortAsync(ct);

        return new AdminDashboardDto
        {
            Kpis = new AdminKpiBlock
            {
                TotalUsers = totalUsers,
                NewUsersInRange = newUsers,
                Dau = dau,
                Wau = wau,
                Mau = mau,
                TotalSessions = totalSessions,
                SessionsInRange = sessionsInRange,
                AvgSessionMinutes = avgMinutes,
                TotalAiAnswers = (int)Math.Min(aiAnswers, int.MaxValue),
                RevenueUsdInRange = revenueInr,
                MrrUsdEstimate = mrrEstimate,
                SignupToPaidConversionPercent = signupToPaid
            },
            UserGrowthDaily = await DailySeriesUsersAsync(fromUtc, toUtc, ct),
            SessionsDaily = await DailySeriesSessionsAsync(fromUtc, toUtc, ct),
            RevenueDailyUsd = await DailySeriesRevenueAsync(fromUtc, toUtc, ct),
            Cohort = cohort
        };
    }

    private async Task<AdminCohortSummaryDto> BuildCohortAsync(CancellationToken ct)
    {
        var cohortDay = DateTime.UtcNow.Date.AddDays(-35);
        var cohortUsers = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= cohortDay && u.CreatedAt < cohortDay.AddDays(1))
            .Select(u => u.Id)
            .ToListAsync(ct);
        if (cohortUsers.Count == 0)
            return new AdminCohortSummaryDto();

        var d1 = cohortDay.AddDays(1);
        var d7 = cohortDay.AddDays(7);
        var d30 = cohortDay.AddDays(30);

        var activeD1 = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.UserId.HasValue && cohortUsers.Contains(e.UserId.Value) && e.CreatedAtUtc < d1)
            .Select(e => e.UserId).Distinct().CountAsync(ct);

        var activeD7 = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.UserId.HasValue && cohortUsers.Contains(e.UserId.Value) && e.CreatedAtUtc < d7)
            .Select(e => e.UserId).Distinct().CountAsync(ct);

        var activeD30 = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.UserId.HasValue && cohortUsers.Contains(e.UserId.Value) && e.CreatedAtUtc < d30)
            .Select(e => e.UserId).Distinct().CountAsync(ct);

        var n = cohortUsers.Count;
        return new AdminCohortSummaryDto
        {
            RetentionDay1Percent = n > 0 ? 100.0 * activeD1 / n : 0,
            RetentionDay7Percent = n > 0 ? 100.0 * activeD7 / n : 0,
            RetentionDay30Percent = n > 0 ? 100.0 * activeD30 / n : 0,
            ChurnRateMonthlyPercent = 0
        };
    }

    private async Task<List<AdminTimeSeriesPointDto>> DailySeriesUsersAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Group by calendar components — EF Core cannot translate DateOnly/SpecifyKind in GroupBy.
        var list = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= fromUtc && u.CreatedAt <= toUtc)
            .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month, u.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, C = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);
        return list.Select(x => new AdminTimeSeriesPointDto
            {
                Date = new DateOnly(x.Year, x.Month, x.Day),
                Value = x.C
            })
            .ToList();
    }

    private async Task<List<AdminTimeSeriesPointDto>> DailySeriesSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var list = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .GroupBy(s => new { s.CreatedAt.Year, s.CreatedAt.Month, s.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, C = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);
        return list.Select(x => new AdminTimeSeriesPointDto
            {
                Date = new DateOnly(x.Year, x.Month, x.Day),
                Value = x.C
            })
            .ToList();
    }

    private async Task<List<AdminTimeSeriesPointDto>> DailySeriesRevenueAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var list = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc && r.AmountInrPaise.HasValue)
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Sum = g.Sum(x => x.AmountInrPaise ?? 0) })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync(ct);
        return list.Select(x => new AdminTimeSeriesPointDto
            {
                Date = new DateOnly(x.Year, x.Month, x.Day),
                Value = x.Sum / 100m
            })
            .ToList();
    }

    public async Task<AdminPagedResult<AdminUserRowDto>> GetUsersAsync(
        DateTime? fromUtc, DateTime? toUtc, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = _db.Users.AsNoTracking();
        if (fromUtc.HasValue)
        {
            var f = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            q = q.Where(u => u.CreatedAt >= f);
        }

        if (toUtc.HasValue)
        {
            var t = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            q = q.Where(u => u.CreatedAt <= t);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Email.ToLower().Contains(s) || u.FirstName.ToLower().Contains(s) ||
                             u.LastName.ToLower().Contains(s));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserRowDto
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                CreatedAt = u.CreatedAt,
                LastActiveAtUtc = u.LastActiveAtUtc,
                CallCredits = u.CallCredits,
                SessionCount = _db.CallSessions.Count(s => s.UserId == u.Id),
                TotalAiUsage = _db.CallSessions.Where(s => s.UserId == u.Id).Sum(s => s.AiUsage)
            })
            .ToListAsync(ct);

        return new AdminPagedResult<AdminUserRowDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<AdminSubscriptionSummaryDto> GetSubscriptionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var receipts = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc)
            .ToListAsync(ct);

        var byProduct = receipts.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Count());
        var payingUsers = receipts.Select(r => r.UserId).Distinct().Count();
        var mostPopular = byProduct.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var totalUsers = await _db.Users.AsNoTracking().CountAsync(ct);
        var revenueCents = receipts.Where(r => r.AmountInrPaise.HasValue).Sum(r => r.AmountInrPaise!.Value);
        var distinctPayingEver = await _db.PaymentReceipts.AsNoTracking()
            .Select(r => r.UserId).Distinct().CountAsync(ct);

        var today = DateTime.UtcNow.Date;
        var mrrWindowStart = today.AddDays(-30);
        var subMonthlyCents = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= mrrWindowStart && r.ProductId == "sub_monthly" && r.AmountInrPaise.HasValue)
            .SumAsync(r => r.AmountInrPaise ?? 0, ct);

        return new AdminSubscriptionSummaryDto
        {
            TotalPayingUsers = payingUsers,
            ProductPurchaseCounts = byProduct,
            MostPopularProductId = mostPopular.Key,
            RevenueUsdInRange = revenueCents / 100m,
            MrrUsdEstimate = subMonthlyCents / 100m,
            FreeToPaidConversionPercent = totalUsers > 0 ? 100.0 * distinctPayingEver / totalUsers : 0,
            CancellationsInRange = await _db.AnalyticsEvents.AsNoTracking()
                .CountAsync(e => e.EventType == "SUBSCRIPTION_CANCELLED" && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct),
            RenewalsInRange = await _db.AnalyticsEvents.AsNoTracking()
                .CountAsync(e => e.EventType == "SUBSCRIPTION_RENEWED" && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct)
        };
    }

    public async Task<AdminUsageSummaryDto> GetUsageAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var sessions = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .ToListAsync(ct);

        var totalMinutes = sessions
            .Where(s => s.Status == "Ended" && s.EndsAt.HasValue)
            .Sum(s => Math.Max(0, (s.EndsAt!.Value - s.CreatedAt).TotalMinutes));

        var distinctUsers = sessions.Select(s => s.UserId).Distinct().Count();
        var avgDur = sessions.Count > 0 ? sessions
            .Where(s => s.Status == "Ended" && s.EndsAt.HasValue)
            .Select(s => (s.EndsAt!.Value - s.CreatedAt).TotalMinutes)
            .DefaultIfEmpty(0)
            .Average() : 0;

        var featureEvents = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc)
            .GroupBy(e => e.EventType)
            .Select(g => new AdminFeatureUsageDto { EventType = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(30)
            .ToListAsync(ct);

        var hourly = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .GroupBy(s => s.CreatedAt.Hour)
            .Select(g => new AdminHourlyBucketDto { HourUtc = g.Key, Count = g.Count() })
            .OrderBy(x => x.HourUtc)
            .ToListAsync(ct);

        return new AdminUsageSummaryDto
        {
            TotalSessions = sessions.Count,
            TotalMinutesEstimated = totalMinutes,
            AvgSessionMinutes = avgDur,
            AvgSessionsPerUser = distinctUsers > 0 ? (double)sessions.Count / distinctUsers : 0,
            SessionsByHourUtc = hourly,
            FeatureEvents = featureEvents
        };
    }

    public async Task<AdminAiMetricsDto> GetAiMetricsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var logs = await _db.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAtUtc >= fromUtc && l.CreatedAtUtc <= toUtc)
            .ToListAsync(ct);

        var total = logs.Count;
        var fails = logs.Count(l => !l.Success);
        var distinctUsers = logs.Select(l => l.UserId).Distinct().Count();

        var latencyDaily = logs
            .GroupBy(l => DateOnly.FromDateTime(DateTime.SpecifyKind(l.CreatedAtUtc, DateTimeKind.Utc)))
            .OrderBy(g => g.Key)
            .Select(g => new AdminTimeSeriesPointDto
            {
                Date = g.Key,
                Value = (decimal)g.Average(x => x.LatencyMs)
            })
            .ToList();

        return new AdminAiMetricsDto
        {
            TotalRequests = total,
            AvgLatencyMs = total > 0 ? logs.Average(l => l.LatencyMs) : 0,
            ErrorRatePercent = total > 0 ? 100.0 * fails / total : 0,
            TotalInputTokens = logs.Sum(l => l.PromptTokens ?? 0),
            TotalOutputTokens = logs.Sum(l => l.CompletionTokens ?? 0),
            TotalCostUsdEstimate = logs.Sum(l => l.EstimatedCostUsd),
            AvgCostPerUserUsd = distinctUsers > 0 ? logs.Sum(l => l.EstimatedCostUsd) / distinctUsers : 0,
            LatencyDailyAvgMs = latencyDaily
        };
    }

    public async Task<AdminFunnelDto> GetFunnelAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var signups = await _db.Users.AsNoTracking()
            .CountAsync(u => u.CreatedAt >= fromUtc && u.CreatedAt <= toUtc, ct);
        var dashboardViews = await _db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.EventType == AnalyticsEventTypes.DashboardViewed && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct);
        var firstSessionCreatedUsers = await _db.CallSessions.AsNoTracking()
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .Select(s => s.UserId).Distinct().CountAsync(ct);
        var activated = await _db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.EventType == AnalyticsEventTypes.SessionActivated && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct);
        var paid = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc)
            .Select(r => r.UserId).Distinct().CountAsync(ct);

        long[] counts = { signups, dashboardViews, firstSessionCreatedUsers, activated, paid };
        var labels = new[]
        {
            ("signup", "Signups"),
            ("dashboard", "Dashboard viewed"),
            ("session_created", "Users with session"),
            ("session_activated", "Session activated events"),
            ("paid", "Paying users (window)")
        };

        var steps = new List<AdminFunnelStepDto>();
        long prev = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var c = counts[i];
            var pct = i > 0 && prev > 0 ? 100.0 * c / prev : (i == 0 ? 100 : 0);
            steps.Add(new AdminFunnelStepDto
            {
                Key = labels[i].Item1,
                Label = labels[i].Item2,
                Count = c,
                ConversionFromPreviousPercent = Math.Round(pct, 1)
            });
            prev = c;
        }

        return new AdminFunnelDto { Steps = steps };
    }

    public async Task<AdminPagedResult<AdminFeedbackRowDto>> GetFeedbackAsync(
        DateTime fromUtc, DateTime toUtc, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var q = from f in _db.UserFeedbacks.AsNoTracking()
            join u in _db.Users.AsNoTracking() on f.UserId equals u.Id
            where f.CreatedAtUtc >= fromUtc && f.CreatedAtUtc <= toUtc
            orderby f.CreatedAtUtc descending
            select new AdminFeedbackRowDto
            {
                Id = f.Id,
                UserId = f.UserId,
                UserEmail = u.Email,
                Message = f.Message,
                Rating = f.Rating,
                SentimentTags = f.SentimentTags,
                CreatedAtUtc = f.CreatedAtUtc
            };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new AdminPagedResult<AdminFeedbackRowDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<AdminSystemMetricsDto> GetSystemMetricsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var serverErr = await _db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.EventType == AnalyticsEventTypes.ApiServerError && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct);
        var clientErr = await _db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.EventType == AnalyticsEventTypes.ApiClientError && e.CreatedAtUtc >= fromUtc && e.CreatedAtUtc <= toUtc, ct);
        var avgLat = await _db.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAtUtc >= fromUtc && l.CreatedAtUtc <= toUtc && l.Success)
            .AverageAsync(l => (double?)l.LatencyMs, ct) ?? 0;

        return new AdminSystemMetricsDto
        {
            ServerErrorEvents = serverErr,
            ClientErrorEvents = clientErr,
            AvgServerLatencyMsFromAiLogs = avgLat
        };
    }
}
