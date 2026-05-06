namespace PKeetDashboard.API.Security.Middleware;

public sealed class SecurityHeadersMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var isSwagger = context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);

        // Set headers just before response is sent.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            // This is an API; for most routes we keep CSP extremely strict.
            // Swagger UI, however, serves HTML/JS/CSS from this same origin and needs a looser policy.
            headers["Content-Security-Policy"] = isSwagger
                ? "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; " +
                  "script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
                  "img-src 'self' data:; font-src 'self' data:; connect-src 'self'"
                : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

            // Reduce fingerprinting / proxy leaks.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await next(context);
    }
}

