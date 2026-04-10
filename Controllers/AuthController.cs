using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly DesktopAuthCodeStore _desktopAuthCodeStore;

    public AuthController(AuthService authService, ILogger<AuthController> logger, DesktopAuthCodeStore desktopAuthCodeStore)
    {
        _authService = authService;
        _logger = logger;
        _desktopAuthCodeStore = desktopAuthCodeStore;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("register/initiate")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RegisterInitiate([FromBody] RegisterInitiateRequest request)
    {
        try
        {
            await _authService.SendRegisterVerificationCodeAsync(request.Email);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("register/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<AuthResponse>> RegisterVerify([FromBody] RegisterVerifyRequest request)
    {
        try
        {
            var response = await _authService.VerifyRegisterCodeAndCreateUserAsync(request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("WEB-AUTH login attempt email={Email} ip={Ip}", request.Email, HttpContext.Connection.RemoteIpAddress?.ToString());
        try
        {
            var response = await _authService.LoginAsync(request);
            _logger.LogInformation("WEB-AUTH login success email={Email}", request.Email);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("WEB-AUTH login failed email={Email} reason={Reason}", request.Email, ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("google-login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.IdToken))
            return BadRequest(new { message = "Google ID token is required." });
        _logger.LogInformation("WEB-AUTH google-login attempt ip={Ip}", HttpContext.Connection.RemoteIpAddress?.ToString());
        try
        {
            var response = await _authService.GoogleLoginAsync(request.IdToken.Trim());
            _logger.LogInformation("WEB-AUTH google-login success");
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("WEB-AUTH google-login unauthorized reason={Reason}", ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("WEB-AUTH google-login invalid-operation reason={Reason}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<UserDto>> Me()
    {
        _logger.LogInformation("WEB-AUTH me request authorized={Authorized}", User?.Identity?.IsAuthenticated == true);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _authService.GetCurrentUserAsync(userId);
        if (user == null)
            return NotFound(new { message = "User not found." });
        return Ok(user);
    }

    [HttpPost("discovery")]
    [Authorize]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SaveDiscovery([FromBody] UserDiscoveryRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var guid))
            return Unauthorized();

        try
        {
            await _authService.SaveDiscoveryResponseAsync(guid, request);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("desktop/initiate")]
    [Authorize]
    [ProducesResponseType(typeof(DesktopAuthInitiateResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public ActionResult<DesktopAuthInitiateResponse> DesktopInitiate([FromBody] DesktopAuthInitiateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RedirectUri) || string.IsNullOrWhiteSpace(request.State))
            return BadRequest(new { message = "redirectUri and state are required." });

        if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var callbackUri) ||
            !(callbackUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
              || callbackUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = "Invalid redirectUri." });
        }

        var authHeader = Request.Headers.Authorization.ToString();
        var bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        var accessToken = authHeader[bearerPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized();

        var refreshToken = $"rt_{Guid.NewGuid():N}";
        var code = _desktopAuthCodeStore.IssueCode(accessToken, refreshToken, TimeSpan.FromSeconds(60));
        var separator = request.RedirectUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var redirectUrl = $"{request.RedirectUri}{separator}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(request.State)}";

        _logger.LogInformation("WEB-AUTH desktop initiate success client={Client} redirectHost={Host} codeLength={CodeLength}", request.Client, callbackUri.Host, code.Length);
        return Ok(new DesktopAuthInitiateResponse { RedirectUrl = redirectUrl });
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DesktopAuthExchangeResponse), 200)]
    [ProducesResponseType(400)]
    public ActionResult<DesktopAuthExchangeResponse> ExchangeDesktopCode([FromBody] DesktopAuthExchangeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "code is required." });

        if (!_desktopAuthCodeStore.TryExchangeCode(request.Code.Trim(), out var entry) || entry == null)
            return BadRequest(new { message = "Invalid or expired code." });

        _logger.LogInformation("WEB-AUTH desktop exchange success codeLength={CodeLength}", request.Code.Length);
        return Ok(new DesktopAuthExchangeResponse
        {
            AccessToken = entry.AccessToken,
            RefreshToken = entry.RefreshToken
        });
    }
}
