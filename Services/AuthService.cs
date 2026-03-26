using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Entities;
using PKeetDashboard.API.Services;
using Google.Apis.Auth;

namespace PKeetDashboard.API.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, JwtService jwt, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
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
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = MapToDto(user) };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (user.PasswordHash == null)
            throw new UnauthorizedAccessException("This account uses Google sign-in. Please sign in with Google.");

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is inactive.");

        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = MapToDto(user) };
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
                IsActive = true
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is inactive.");

        var token = _jwt.GenerateToken(user);
        return new AuthResponse { Token = token, User = MapToDto(user) };
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        var id = Guid.TryParse(userId, out var guid) ? guid : (Guid?)null;
        if (id == null) return null;
        var user = await _db.Users.FindAsync(id);
        return user == null ? null : MapToDto(user);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters.");
    }

    private static UserDto MapToDto(User u) => new UserDto
    {
        Id = u.Id,
        Email = u.Email,
        FirstName = u.FirstName,
        LastName = u.LastName,
        ProfilePictureUrl = u.ProfilePictureUrl,
        IsEmailVerified = u.IsEmailVerified
    };
}
