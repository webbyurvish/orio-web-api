using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PKeetDashboard.API.Services;

public sealed class DesktopAuthCodeStore
{
    private readonly ConcurrentDictionary<string, DesktopAuthCodeEntry> _codes = new();

    public string IssueCode(string accessToken, string refreshToken, TimeSpan ttl)
    {
        CleanupExpired();
        var code = CreateCode();
        var entry = new DesktopAuthCodeEntry(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.Add(ttl));
        _codes[code] = entry;
        return code;
    }

    public bool TryExchangeCode(string code, out DesktopAuthCodeEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(code))
            return false;

        if (!_codes.TryRemove(code, out var value))
            return false;

        if (value.ExpiresAtUtc < DateTimeOffset.UtcNow)
            return false;

        entry = value;
        return true;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _codes)
        {
            if (pair.Value.ExpiresAtUtc < now)
                _codes.TryRemove(pair.Key, out _);
        }
    }

    private static string CreateCode()
    {
        Span<byte> random = stackalloc byte[24];
        RandomNumberGenerator.Fill(random);
        return Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed record DesktopAuthCodeEntry(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);
