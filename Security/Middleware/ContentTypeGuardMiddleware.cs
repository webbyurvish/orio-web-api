namespace PKeetDashboard.API.Security.Middleware;

public sealed class ContentTypeGuardMiddleware : IMiddleware
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "multipart/form-data",
        "application/x-www-form-urlencoded"
    };

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Only validate for methods that typically have bodies.
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                var ct = context.Request.ContentType.Split(';', 2)[0].Trim();
                if (!Allowed.Contains(ct))
                {
                    context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    await context.Response.WriteAsJsonAsync(new { message = "Unsupported Content-Type." });
                    return;
                }
            }
        }

        await next(context);
    }
}

