using System.Text.Json;

namespace PKeetDashboard.API.Security.Middleware;

public sealed class ProblemDetailsExceptionMiddleware : IMiddleware
{
    private readonly ILogger<ProblemDetailsExceptionMiddleware> _logger;

    public ProblemDetailsExceptionMiddleware(ILogger<ProblemDetailsExceptionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected / cancelled request; avoid noisy logs.
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception ex)
        {
            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var cid) ? cid?.ToString() : null;
            _logger.LogError(ex, "Unhandled exception path={Path} correlationId={CorrelationId}", context.Request.Path.Value, correlationId);

            if (context.Response.HasStarted)
                throw;

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problem = new
            {
                type = "about:blank",
                title = "An unexpected error occurred.",
                status = 500,
                traceId = correlationId
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}

