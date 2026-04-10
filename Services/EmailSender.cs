using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PKeetDashboard.API.Options;

namespace PKeetDashboard.API.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

public sealed class EmailSender : IEmailSender
{
    private readonly SmtpOptions _opt;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IOptions<SmtpOptions> opt, ILogger<EmailSender> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.Equals((_opt.Mode ?? "").Trim(), "console", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "EMAIL(CONSOLE MODE) to={To} subject={Subject}\n{Body}",
                toEmail,
                subject,
                htmlBody);
            return;
        }

        if (string.IsNullOrWhiteSpace(_opt.Host) ||
            string.IsNullOrWhiteSpace(_opt.FromEmail))
        {
            throw new InvalidOperationException("SMTP is not configured. Set Smtp:Host and Smtp:FromEmail (and credentials if required).");
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_opt.FromName ?? "Smeed AI", _opt.FromEmail));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 15_000;

        var sec = _opt.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_opt.Host, _opt.Port, sec, ct);

        if (!string.IsNullOrWhiteSpace(_opt.Username))
            await client.AuthenticateAsync(_opt.Username, _opt.Password, ct);

        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
        _logger.LogInformation("SMTP email sent to={To} subject={Subject}", toEmail, subject);
    }
}

