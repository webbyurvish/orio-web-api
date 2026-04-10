namespace PKeetDashboard.API.DTOs;

public sealed class UserDiscoveryRequest
{
    public string Source { get; set; } = string.Empty;
    public string? OtherText { get; set; }
}

