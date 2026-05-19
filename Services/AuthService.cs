using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Analytics;
using PKeetDashboard.API.Services;
using Google.Apis.Auth;
using System.Security.Cryptography;

namespace PKeetDashboard.API.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly IConfiguration _config;
    private readonly IEmailSender _emailSender;
    private readonly IAnalyticsRecorder _analytics;
    private const decimal FirstSignupFreeCredits = 1m;

    public AuthService(AppDbContext db, JwtService jwt, IConfiguration config, IEmailSender emailSender, IAnalyticsRecorder analytics)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
        _emailSender = emailSender;
        _analytics = analytics;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Email is required.");

        if (await _db.Users.AnyAsync(u => u.Email == normalizedEmail))
            throw new InvalidOperationException("An account with this email already exists.");

        ValidatePassword(req.Password);
        var firstName = (req.FirstName ?? "").Trim();
        var lastName = (req.LastName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            FirstName = firstName,
            LastName = lastName ?? "",
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            CallCredits = FirstSignupFreeCredits
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _analytics.RecordAsync(user.Id, AnalyticsEventTypes.UserSignup, null, "server", null);

        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = await MapToDtoAsync(user) };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        if (req == null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            throw new UnauthorizedAccessException("Invalid email or password.");

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (user.PasswordHash == null)
            throw new UnauthorizedAccessException("This account uses Google sign-in. Please sign in with Google.");

        try
        {
            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception)
        {
            // Corrupt hash / unexpected bcrypt payload: do not leak details; treat as bad credentials.
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is inactive.");

        await _analytics.RecordAsync(user.Id, AnalyticsEventTypes.UserLogin, null, "server", null);
        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = await MapToDtoAsync(user) };
    }

    public async Task<AuthResponse> GoogleLoginAsync(string idToken)
    {
        var clientId = _config["GoogleAuth:ClientId"];
        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Google authentication is not configured.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch (Exception)
        {
            throw new UnauthorizedAccessException("Invalid Google token.");
        }

        var email = payload.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
            throw new UnauthorizedAccessException("Google account email not found.");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.GoogleId == payload.Subject || u.Email == email);

        var googleIsNewUser = false;
        if (user != null)
        {
            if (user.GoogleId == null)
            {
                user.GoogleId = payload.Subject;
                user.ProfilePictureUrl = payload.Picture;
                user.IsEmailVerified = true;
                user.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            googleIsNewUser = true;
            var name = payload.Name ?? "";
            var parts = name.Split(' ', 2);
            var firstName = parts.Length > 0 ? parts[0] : "User";
            var lastName = parts.Length > 1 ? parts[1] : "";

            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = null,
                FirstName = firstName,
                LastName = lastName,
                GoogleId = payload.Subject,
                ProfilePictureUrl = payload.Picture,
                IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
                CallCredits = FirstSignupFreeCredits
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            await _analytics.RecordAsync(user.Id, AnalyticsEventTypes.UserSignup, """{"provider":"google"}""", "server", null);
        }

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is inactive.");

        if (!googleIsNewUser)
            await _analytics.RecordAsync(user.Id, AnalyticsEventTypes.UserLogin, """{"provider":"google"}""", "server", null);
        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = await MapToDtoAsync(user) };
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        var id = Guid.TryParse(userId, out var guid) ? guid : (Guid?)null;
        if (id == null) return null;
        var user = await _db.Users.FindAsync(id);
        return user == null ? null : await MapToDtoAsync(user);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters.");
    }

    public async Task SendRegisterVerificationCodeAsync(string email)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Email is required.");

        if (await _db.Users.AnyAsync(u => u.Email == normalizedEmail))
            throw new InvalidOperationException("An account with this email already exists.");

        var now = DateTime.UtcNow;

        // Soft rate-limit by email.
        var last = await _db.EmailVerificationCodes
            .Where(x => x.Email == normalizedEmail)
            .OrderByDescending(x => x.LastSentAtUtc)
            .FirstOrDefaultAsync();

        if (last != null && (now - last.LastSentAtUtc) < TimeSpan.FromSeconds(45))
            throw new InvalidOperationException("Please wait a moment before requesting another code.");

        // Invalidate older unused codes.
        var oldUnused = await _db.EmailVerificationCodes
            .Where(x => x.Email == normalizedEmail && !x.IsUsed && x.ExpiresAtUtc > now)
            .ToListAsync();
        foreach (var x in oldUnused)
            x.IsUsed = true;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var entry = new EmailVerificationCode
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            CreatedAtUtc = now,
            LastSentAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            VerifyAttempts = 0,
            IsUsed = false
        };
        _db.EmailVerificationCodes.Add(entry);
        await _db.SaveChangesAsync();

        var html = BuildVerificationEmailHtml(code);
        try
        {
            await _emailSender.SendEmailAsync(normalizedEmail, "Your Smeed AI verification code", html);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            throw new InvalidOperationException("Email service authentication failed. Check Brevo SMTP username/password (SMTP key) and try again.");
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex)
        {
            throw new InvalidOperationException($"Email service error: {ex.Message}");
        }
    }

    public async Task<AuthResponse> VerifyRegisterCodeAndCreateUserAsync(RegisterVerifyRequest req)
    {
        var normalizedEmail = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Email is required.");
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new ArgumentException("Verification code is required.");

        if (await _db.Users.AnyAsync(u => u.Email == normalizedEmail))
            throw new InvalidOperationException("An account with this email already exists.");

        ValidatePassword(req.Password);

        var firstName = (req.FirstName ?? "").Trim();
        var lastName = (req.LastName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required.");

        var now = DateTime.UtcNow;
        var entry = await _db.EmailVerificationCodes
            .Where(x => x.Email == normalizedEmail && !x.IsUsed)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (entry == null)
            throw new InvalidOperationException("No verification code found. Please request a new code.");
        if (entry.ExpiresAtUtc <= now)
            throw new InvalidOperationException("Verification code expired. Please request a new code.");
        if (entry.VerifyAttempts >= 8)
            throw new InvalidOperationException("Too many attempts. Please request a new code.");

        entry.VerifyAttempts += 1;

        var ok = BCrypt.Net.BCrypt.Verify(req.Code.Trim(), entry.CodeHash);
        if (!ok)
        {
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("Invalid verification code.");
        }

        entry.IsUsed = true;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            FirstName = firstName,
            LastName = lastName ?? "",
            IsEmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            CallCredits = FirstSignupFreeCredits
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = await MapToDtoAsync(user) };
    }

    public async Task SaveDiscoveryResponseAsync(Guid userId, UserDiscoveryRequest req)
    {
        var source = (req.Source ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source is required.");

        var otherText = (req.OtherText ?? string.Empty).Trim();
        if (source.Equals("Other", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(otherText))
            throw new ArgumentException("otherText is required when source is Other.");

        var existing = await _db.UserDiscoveryResponses.FirstOrDefaultAsync(x => x.UserId == userId);
        if (existing != null)
        {
            existing.Source = source;
            existing.OtherText = string.IsNullOrWhiteSpace(otherText) ? null : otherText;
            await _db.SaveChangesAsync();
            return;
        }

        _db.UserDiscoveryResponses.Add(new UserDiscoveryResponse
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Source = source,
            OtherText = string.IsNullOrWhiteSpace(otherText) ? null : otherText,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private async Task<UserDto> MapToDtoAsync(User u)
    {
        var hasDiscovery = await _db.UserDiscoveryResponses.AnyAsync(x => x.UserId == u.Id);
        // Unlimited access is derived from purchase receipts. This is a lightweight entitlement signal for clients
        // (desktop overlay especially) so they don't block users with lifetime/subscription purchases just because
        // their credit balance is zero.
        var planDisplay = await _db.PaymentReceipts.AsNoTracking()
            .Where(r => r.UserId == u.Id && (r.ProductId == "lifetime" || r.ProductId == "sub_monthly" || r.ProductId == "sub_yearly"))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.ProductId)
            .FirstOrDefaultAsync();

        var unlimited = !string.IsNullOrEmpty(planDisplay);
        var planLabel = planDisplay switch
        {
            "lifetime" => "Lifetime",
            "sub_monthly" => "Monthly",
            "sub_yearly" => "Yearly",
            _ => null
        };
        return new UserDto
        {
            Id = u.Id,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            ProfilePictureUrl = u.ProfilePictureUrl,
            IsEmailVerified = u.IsEmailVerified,
            CallCredits = u.CallCredits,
            UnlimitedAccess = unlimited,
            PlanDisplay = planLabel,
            HasDiscoveryResponse = hasDiscovery,
            IsAdmin = u.IsAdmin
        };
    }

    private static string BuildVerificationEmailHtml(string code)
    {
        var safeCode = System.Net.WebUtility.HtmlEncode(code);
        return $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
</head>
<body style=""margin:0;padding:0;background:#ffffff;font-family:Segoe UI, Arial, sans-serif;color:#0f172a;"">
  <div style=""max-width:640px;margin:0 auto;padding:28px 22px;"">
    <h2 style=""margin:0 0 10px 0;color:#312e81;font-weight:800;"">Welcome to Smeed AI!</h2>
    <p style=""margin:0 0 18px 0;color:#334155;font-size:14px;"">Your verification code is:</p>
    <div style=""background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;padding:22px;text-align:center;"">
      <div style=""font-size:34px;letter-spacing:10px;font-weight:800;color:#0f172a;"">{safeCode}</div>
    </div>
    <p style=""margin:16px 0 0 0;color:#64748b;font-size:13px;"">This code will expire in 10 minutes.</p>
    <p style=""margin:10px 0 0 0;color:#94a3b8;font-size:12px;"">If you didn’t request this code, you can ignore this email.</p>
    <div style=""margin-top:20px;color:#94a3b8;font-size:12px;"">Smeed AI - Your AI Interview Copilot</div>
  </div>
</body>
</html>";
    }
}
