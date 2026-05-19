using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PKeetDashboard.API.Options;

namespace PKeetDashboard.API.Services;

public sealed class RazorpayApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpayApiClient> _logger;

    public RazorpayApiClient(HttpClient http, IOptions<RazorpayOptions> options, ILogger<RazorpayApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.KeyId) && !string.IsNullOrWhiteSpace(_options.KeySecret);

    public bool IsTestMode => _options.KeyId.Trim().StartsWith("rzp_test_", StringComparison.OrdinalIgnoreCase);

    public string KeyId => _options.KeyId.Trim();

    public async Task<RazorpayOrderDto> CreateOrderAsync(
        long amountPaise,
        string receipt,
        IReadOnlyDictionary<string, string> notes,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["amount"] = amountPaise,
            ["currency"] = "INR",
            ["receipt"] = receipt,
            ["notes"] = notes,
        };

        using var doc = await PostJsonAsync("/v1/orders", body, ct);
        return ParseOrder(doc.RootElement);
    }

    public async Task<RazorpayOrderDto> GetOrderAsync(string orderId, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"/v1/orders/{Uri.EscapeDataString(orderId)}", ct);
        return ParseOrder(doc.RootElement);
    }

    private static RazorpayOrderDto ParseOrder(JsonElement root)
    {
        Dictionary<string, string>? notes = null;
        if (root.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.Object)
        {
            notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in notesEl.EnumerateObject())
                notes[p.Name] = p.Value.GetString() ?? "";
        }

        return new RazorpayOrderDto(
            root.GetProperty("id").GetString() ?? "",
            root.GetProperty("amount").GetInt64(),
            root.GetProperty("currency").GetString() ?? "INR",
            root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            notes);
    }

    public async Task<RazorpayPaymentDto?> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync($"/v1/payments/{Uri.EscapeDataString(paymentId)}", ct);
            var root = doc.RootElement;
            return new RazorpayPaymentDto(
                root.GetProperty("id").GetString() ?? "",
                root.GetProperty("order_id").GetString() ?? "",
                root.GetProperty("status").GetString() ?? "",
                root.GetProperty("amount").GetInt64(),
                root.GetProperty("currency").GetString() ?? "INR");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Razorpay payment fetch failed for {PaymentId}", paymentId);
            return null;
        }
    }

    private async Task<JsonDocument> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        ApplyAuth(req);
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseRazorpayError(text, res.StatusCode));
        return JsonDocument.Parse(text);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException(ParseRazorpayError(text, res.StatusCode));
        return JsonDocument.Parse(text);
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        var raw = $"{_options.KeyId.Trim()}:{_options.KeySecret.Trim()}";
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    private static string ParseRazorpayError(string body, System.Net.HttpStatusCode code)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("description", out var desc))
            {
                return desc.GetString() ?? $"Razorpay error ({(int)code})";
            }
        }
        catch
        {
            /* ignore */
        }

        return $"Razorpay error ({(int)code})";
    }
}

public sealed record RazorpayOrderDto(
    string Id,
    long Amount,
    string Currency,
    string Status,
    IReadOnlyDictionary<string, string>? Notes);

public sealed record RazorpayPaymentDto(
    string Id,
    string OrderId,
    string Status,
    long Amount,
    string Currency);
