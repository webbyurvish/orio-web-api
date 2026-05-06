namespace PKeetDashboard.API.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public bool IsEmailVerified { get; set; }
    public decimal CallCredits { get; set; }
    /// <summary>True when the account has unlimited usage via subscription or lifetime purchase.</summary>
    public bool UnlimitedAccess { get; set; }
    /// <summary>Optional display label for unlimited plan (e.g. "Monthly", "Yearly", "Lifetime").</summary>
    public string? PlanDisplay { get; set; }
    public bool HasDiscoveryResponse { get; set; }
    public bool IsAdmin { get; set; }
}
