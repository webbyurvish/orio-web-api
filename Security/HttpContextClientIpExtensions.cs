using System.Net;

namespace PKeetDashboard.API.Security;

public static class HttpContextClientIpExtensions
{
    public static string? GetClientIpForRateLimiting(this HttpContext context)
    {
        // After UseForwardedHeaders, RemoteIpAddress should reflect the client IP (when proxy is trusted).
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null) return null;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return "127.0.0.1";

        return ip.ToString();
    }
}

