namespace PKeetDashboard.API.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>
    /// Email mode:
    /// - "smtp": send via SMTP
    /// - "console": do not send; log the email content (dev-friendly)
    /// </summary>
    public string Mode { get; set; } = "smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Smeed AI";
}

