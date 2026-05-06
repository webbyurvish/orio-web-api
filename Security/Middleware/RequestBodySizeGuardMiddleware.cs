using Microsoft.Extensions.Options;

namespace PKeetDashboard.API.Security.Middleware;

public sealed class RequestBodySizeGuardMiddleware : IMiddleware
{
    private readonly long _maxBytes;

    public RequestBodySizeGuardMiddleware(IOptions<SecurityOptions> options)
    {
        _maxBytes = options.Value.MaxRequestBodyBytes;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Kestrel can still be configured with a hard cap; this is a defense-in-depth early rejection.
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { message = "Request body too large." });
            return;
        }

        await next(context);
    }
}

