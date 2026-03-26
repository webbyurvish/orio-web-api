namespace PKeetDashboard.API.DTOs;

public class DesktopAuthInitiateRequest
{
    public string Client { get; set; } = "desktop";
    public string RedirectUri { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public class DesktopAuthInitiateResponse
{
    public string RedirectUrl { get; set; } = string.Empty;
}

public class DesktopAuthExchangeRequest
{
    public string Code { get; set; } = string.Empty;
}

public class DesktopAuthExchangeResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}
