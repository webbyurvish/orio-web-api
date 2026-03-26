using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/desktop/speech")]
[Authorize]
public class DesktopSpeechController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DesktopSpeechController> _logger;

    public DesktopSpeechController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<DesktopSpeechController> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("token")]
    public async Task<ActionResult<DesktopSpeechTokenResponse>> GetToken()
    {
        var key = _configuration["AzureSpeech:Key"];
        var region = _configuration["AzureSpeech:Region"];

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            _logger.LogWarning("Desktop speech token request rejected: AzureSpeech config missing.");
            return StatusCode(500, new { message = "Speech is not configured on the server." });
        }

        var client = _httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken");
        req.Headers.Add("Ocp-Apim-Subscription-Key", key);
        req.Content = new StringContent(string.Empty);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var res = await client.SendAsync(req);
        var token = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Desktop speech token request failed with status {StatusCode}.", (int)res.StatusCode);
            return StatusCode(500, new { message = "Failed to issue speech token." });
        }

        return Ok(new DesktopSpeechTokenResponse
        {
            Region = region,
            Token = token.Trim(),
            ExpiresInSeconds = 540
        });
    }
}

public sealed class DesktopSpeechTokenResponse
{
    public string Region { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

