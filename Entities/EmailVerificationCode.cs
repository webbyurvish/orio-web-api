namespace PKeetDashboard.API.Entities;

public sealed class EmailVerificationCode
{
    public Guid Id { get; set; }

    /// <summary>Normalized (lowercased) email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the 6-digit code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int VerifyAttempts { get; set; }

    public DateTime LastSentAtUtc { get; set; }

    public bool IsUsed { get; set; }
}

