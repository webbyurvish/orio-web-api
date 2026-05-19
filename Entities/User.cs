namespace PKeetDashboard.API.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? GoogleId { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Call credits balance. Rule: 30 minutes = 1 credit.</summary>
    public decimal CallCredits { get; set; } = 0m;

    public bool IsAdmin { get; set; }
    public DateTime? LastActiveAtUtc { get; set; }
}

