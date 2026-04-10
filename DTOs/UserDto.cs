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
    public bool HasDiscoveryResponse { get; set; }
    public bool IsAdmin { get; set; }
}
