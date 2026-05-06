using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PKeetDashboard.API.Security.Middleware;

namespace PKeetDashboard.API.Security;

public static class SecurityHardeningExtensions
{
    public static IServiceCollection AddSecurityHardening(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(config.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.JwtClockSkewSeconds is >= 0 and <= 120, "Security:JwtClockSkewSeconds must be between 0 and 120.")
            .Validate(o => o.MaxRequestBodyBytes is >= 1024 and <= 100 * 1024 * 1024, "Security:MaxRequestBodyBytes out of range.")
            .ValidateOnStart();

        var sec = config.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = sec.ForwardedHeadersForwardLimit;

            // Only trust proxy headers from explicitly configured proxies/networks.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var ip in sec.TrustedProxies ?? Array.Empty<string>())
            {
                if (IPAddress.TryParse(ip, out var parsed))
                    options.KnownProxies.Add(parsed);
            }

            foreach (var cidr in sec.TrustedNetworks ?? Array.Empty<string>())
            {
                if (TryParseCidr(cidr, out var network))
                    options.KnownNetworks.Add(network);
            }
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(sec.RequestTimeoutSeconds) };

            options.AddPolicy("LongRunning", new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(sec.LongRequestTimeoutSeconds) });
            options.AddPolicy("Auth", new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(sec.AuthRequestTimeoutSeconds) });
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");
                logger.LogWarning("Rate limit rejected path={Path} ip={Ip} user={User}",
                    context.HttpContext.Request.Path.Value,
                    context.HttpContext.GetClientIpForRateLimiting(),
                    context.HttpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier));
                return ValueTask.CompletedTask;
            };

            var perIpLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                // Defense-in-depth: always apply *some* limiter, even if IP can't be resolved.
                var ip = httpContext.GetClientIpForRateLimiting() ?? "unknown";
                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: ip,
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = sec.RateLimiting.GlobalPerIpTokenLimit,
                        TokensPerPeriod = sec.RateLimiting.GlobalPerIpTokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(sec.RateLimiting.GlobalPerIpReplenishSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = sec.RateLimiting.GlobalPerIpQueueLimit,
                        AutoReplenishment = true
                    });
            });

            var concurrencyLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            {
                return RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey: "global",
                    factory: _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = sec.Concurrency.GlobalPermitLimit,
                        QueueLimit = sec.Concurrency.GlobalQueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // Chain limiters to prevent bypass via "cheap" endpoints or parallel flooding.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(perIpLimiter, concurrencyLimiter);

            options.AddPolicy("PerUser", httpContext =>
            {
                var userId = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                    userId = $"anon:{httpContext.GetClientIpForRateLimiting() ?? "unknown"}";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: userId,
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = sec.RateLimiting.PerUserPermitLimit,
                        Window = TimeSpan.FromSeconds(sec.RateLimiting.PerUserWindowSeconds),
                        SegmentsPerWindow = sec.RateLimiting.PerUserSegmentsPerWindow,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = sec.RateLimiting.PerUserQueueLimit
                    });
            });

            options.AddPolicy("AuthSensitive", httpContext =>
            {
                // Stricter for login/verification endpoints to reduce brute-force + credential stuffing.
                var ip = httpContext.GetClientIpForRateLimiting() ?? "unknown";
                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: ip,
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = sec.RateLimiting.AuthTokenLimit,
                        TokensPerPeriod = sec.RateLimiting.AuthTokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(sec.RateLimiting.AuthReplenishSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = sec.RateLimiting.AuthQueueLimit,
                        AutoReplenishment = true
                    });
            });
        });

        services.ConfigureHttpJsonOptions(options =>
        {
            // Prevent deep object attacks & reduce payload abuse.
            options.SerializerOptions.MaxDepth = sec.JsonMaxDepth;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Disallow;
            options.SerializerOptions.AllowTrailingCommas = false;
        });

        services.AddSingleton<CorrelationIdMiddleware>();
        services.AddSingleton<SecurityHeadersMiddleware>();
        services.AddSingleton<ProblemDetailsExceptionMiddleware>();
        services.AddSingleton<ContentTypeGuardMiddleware>();
        services.AddSingleton<RequestBodySizeGuardMiddleware>();

        // MVC model binding hardening: avoid silent binding of unknown fields (mass assignment).
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.MaxModelValidationErrors = 50;
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = false;
        });

        return services;
    }

    public static IApplicationBuilder UseSecurityHardening(
        this WebApplication app,
        IConfiguration config,
        IHostEnvironment env)
    {
        var sec = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

        if (!env.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ProblemDetailsExceptionMiddleware>();

        // These are intentionally early: reject malformed/abusive requests before heavier middleware runs.
        app.UseMiddleware<RequestBodySizeGuardMiddleware>();
        app.UseMiddleware<ContentTypeGuardMiddleware>();

        app.UseRequestTimeouts();

        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Concurrency protection before authentication/authorization (cheaper under floods).
        app.UseRateLimiter();

        return app;
    }

    private static bool TryParseCidr(string cidr, out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
    {
        network = default!;
        var parts = cidr.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
        if (!int.TryParse(parts[1], out var prefix)) return false;
        try
        {
            network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(baseIp, prefix);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

