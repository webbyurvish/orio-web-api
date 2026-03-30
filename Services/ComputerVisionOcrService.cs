using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PKeetDashboard.API.Services;

/// <summary>
/// Calls Azure AI Vision Image Analysis (Read / OCR) on a screenshot image.
/// Configure: AzureComputerVision:Endpoint, AzureComputerVision:Key, optional ApiVersion (default 2023-10-01).
/// </summary>
public sealed class ComputerVisionOcrService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComputerVisionOcrService> _logger;

    public ComputerVisionOcrService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ComputerVisionOcrService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Extracts plain text from an image using the Read feature.
    /// </summary>
    /// <exception cref="InvalidOperationException">Configuration missing or API error.</exception>
    public async Task<string> ReadTextFromImageAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default)
    {
        var endpoint = (_configuration["AzureComputerVision:Endpoint"] ?? string.Empty).Trim().TrimEnd('/');
        var key = _configuration["AzureComputerVision:Key"] ?? string.Empty;
        var apiVersion = (_configuration["AzureComputerVision:ApiVersion"] ?? "2023-10-01").Trim();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Azure Computer Vision is not configured (AzureComputerVision:Endpoint / Key).");

        if (imageBytes.Length == 0)
            throw new ArgumentException("Image is empty.", nameof(imageBytes));

        var url =
            $"{endpoint}/computervision/imageanalysis:analyze?api-version={Uri.EscapeDataString(apiVersion)}&features=read";

        var client = _httpClientFactory.CreateClient("ComputerVision");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
        request.Content = new ByteArrayContent(imageBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Computer Vision HTTP {Status}: {Body}", (int)response.StatusCode,
                body.Length > 800 ? body[..800] + "…" : body);
            throw new InvalidOperationException(
                $"Computer Vision request failed ({(int)response.StatusCode}). Check Endpoint, Key, and that the resource supports Image Analysis Read.");
        }

        string text;
        try
        {
            using var doc = JsonDocument.Parse(body);
            text = ExtractReadText(doc.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Computer Vision JSON parse failed. Body prefix: {Prefix}",
                body.Length > 400 ? body[..400] : body);
            throw new InvalidOperationException("Could not parse Computer Vision response.", ex);
        }

        return text;
    }

    private static string ExtractReadText(JsonElement root)
    {
        if (!root.TryGetProperty("readResult", out var readResult))
            return string.Empty;

        var sb = new StringBuilder();
        if (readResult.TryGetProperty("blocks", out var blocks))
        {
            foreach (var block in blocks.EnumerateArray())
            {
                if (!block.TryGetProperty("lines", out var lines))
                    continue;
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.TryGetProperty("text", out var t))
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            sb.AppendLine(s);
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }
}
