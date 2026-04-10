using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace PKeetDashboard.API.Data;

/// <summary>Grants <see cref="Entities.User.IsAdmin"/> for emails listed in config (re-run safe).</summary>
public static class AdminBootstrap
{
    public static async Task ApplyAsync(AppDbContext db, IConfiguration config, CancellationToken ct = default)
    {
        var raw = config["Admin:BootstrapEmails"];
        if (string.IsNullOrWhiteSpace(raw)) return;

        var emails = raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Where(e => e.Length > 0 && e.Contains('@', StringComparison.Ordinal))
            .Distinct()
            .ToList();
        if (emails.Count == 0) return;

        var users = await db.Users
            .Where(u => u.Email != null && emails.Contains(u.Email.ToLower()))
            .ToListAsync(ct);
        var changed = false;
        foreach (var u in users)
        {
            if (u.IsAdmin) continue;
            u.IsAdmin = true;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }
}
